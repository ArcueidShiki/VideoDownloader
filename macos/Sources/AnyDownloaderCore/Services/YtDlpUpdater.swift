import Foundation
import CryptoKit
import os

/// Keeps an app-managed, always-current yt-dlp in Application Support.
///
/// Site extractors (Bilibili, YouTube…) break whenever the site changes its API — an
/// eight-month-old Homebrew yt-dlp only gets 480P from Bilibili while the current
/// release gets 1080P. Mirrors the Windows app's startup updater.
///
/// Layout under `root`:
///   2026.10.26/yt-dlp_macos + _internal/   one directory per verified release
///   current -> 2026.10.26                   swapped atomically after validation
///
/// Uses the "onedir" build: the single-file build unpacks itself on every launch and
/// macOS re-scans the unpacked libraries each time (~50 s per call).
public struct YtDlpUpdater: Sendable {
    public static let archiveName = "yt-dlp_macos.zip"
    public static let executableName = "yt-dlp_macos"
    static let checksumsName = "SHA2-256SUMS"
    static let releaseBase = "https://github.com/yt-dlp/yt-dlp/releases/download/"
    static let latestReleaseAPI = URL(string: "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest")!
    /// The first launch of freshly unpacked libraries triggers a one-time macOS security
    /// scan (~30 s). Validation absorbs it so later launches take ~1 s.
    static let validationTimeout: TimeInterval = 180

    public struct Source: Sendable {
        public let latestTag: @Sendable () async throws -> String
        /// Downloads `url` and returns a temporary file the caller owns.
        public let download: @Sendable (URL) async throws -> URL

        public init(latestTag: @escaping @Sendable () async throws -> String,
                    download: @escaping @Sendable (URL) async throws -> URL) {
            self.latestTag = latestTag
            self.download = download
        }
    }

    public enum UpdateError: Error, Equatable, LocalizedError {
        case invalidTag(String)
        case httpStatus(Int)
        case checksumMissing
        case checksumMismatch
        case extractionFailed
        case validationFailed(String)
        case activationFailed(Int32)

        public var errorDescription: String? {
            switch self {
            case .invalidTag(let tag): return "Unexpected yt-dlp release tag: \(tag)"
            case .httpStatus(let code): return "GitHub returned HTTP \(code)"
            case .checksumMissing: return "yt-dlp checksum list has no entry for \(YtDlpUpdater.archiveName)"
            case .checksumMismatch: return "Downloaded yt-dlp failed SHA-256 verification"
            case .extractionFailed: return "Could not unpack the yt-dlp release"
            case .validationFailed(let output): return "New yt-dlp did not start correctly: \(output)"
            case .activationFailed(let errno): return "Could not activate the new yt-dlp (errno \(errno))"
            }
        }
    }

    public enum Outcome: Equatable, Sendable {
        case upToDate(String)
        case installed(String)
    }

    public static var defaultRoot: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("AnyDownloader/yt-dlp", isDirectory: true) // legacy data folder name, kept
    }

    public let root: URL
    let source: Source

    public init(root: URL = YtDlpUpdater.defaultRoot, source: Source = .github) {
        self.root = root
        self.source = source
    }

    private var currentLink: URL { root.appendingPathComponent("current") }

    /// Version the `current` link points at, if any.
    public var installedVersion: String? {
        guard let target = try? FileManager.default.destinationOfSymbolicLink(atPath: currentLink.path) else { return nil }
        let name = URL(fileURLWithPath: target).lastPathComponent
        return Self.isValidTag(name) ? name : nil
    }

    /// Resolved (versioned) executable path. Callers get the versioned directory rather
    /// than `current/…`, so a running download keeps its own libraries if `current` is
    /// swapped mid-run.
    public var currentExecutable: String? {
        guard let version = installedVersion else { return nil }
        let path = root.appendingPathComponent(version).appendingPathComponent(Self.executableName).path
        return FileManager.default.isExecutableFile(atPath: path) ? path : nil
    }

    // MARK: - Update

    public func update() async throws -> Outcome {
        let tag = try await source.latestTag()
        guard Self.isValidTag(tag) else { throw UpdateError.invalidTag(tag) }
        if installedVersion == tag, currentExecutable != nil { return .upToDate(tag) }

        let fm = FileManager.default
        try fm.createDirectory(at: root, withIntermediateDirectories: true)
        let base = URL(string: Self.releaseBase + tag + "/")!

        let sumsFile = try await source.download(base.appendingPathComponent(Self.checksumsName))
        defer { try? fm.removeItem(at: sumsFile) }
        guard let expected = Self.checksum(in: try String(contentsOf: sumsFile, encoding: .utf8), for: Self.archiveName) else {
            throw UpdateError.checksumMissing
        }
        let archive = try await source.download(base.appendingPathComponent(Self.archiveName))
        defer { try? fm.removeItem(at: archive) }
        guard try Self.sha256(of: archive) == expected else { throw UpdateError.checksumMismatch }

        let staging = root.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: staging) }
        try await Self.unzip(archive, into: staging)
        let reported = try await Self.reportedVersion(of: staging.appendingPathComponent(Self.executableName))
        guard reported == tag else { throw UpdateError.validationFailed(reported) }

        let destination = root.appendingPathComponent(tag, isDirectory: true)
        if fm.fileExists(atPath: destination.path) { try fm.removeItem(at: destination) }
        try fm.moveItem(at: staging, to: destination)
        let previous = installedVersion
        try activate(tag)
        prune(keeping: Set([tag, previous].compactMap { $0 }))
        return .installed(tag)
    }

    /// Atomically repoints `current` (rename(2) replaces the old link in one step).
    private func activate(_ tag: String) throws {
        let temporary = root.appendingPathComponent(".current-\(UUID().uuidString)")
        try FileManager.default.createSymbolicLink(atPath: temporary.path, withDestinationPath: tag)
        guard rename(temporary.path, currentLink.path) == 0 else {
            let code = errno
            try? FileManager.default.removeItem(at: temporary)
            throw UpdateError.activationFailed(code)
        }
    }

    /// Keeps the new and the previous version (a download started before the swap may
    /// still be using it); removes older versions and staging leftovers from crashes.
    private func prune(keeping keep: Set<String>) {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: root.path) else { return }
        for name in names where (Self.isValidTag(name) && !keep.contains(name)) || name.hasPrefix(".staging-") {
            try? fm.removeItem(at: root.appendingPathComponent(name))
        }
    }

    // MARK: - Helpers

    static func isValidTag(_ tag: String) -> Bool {
        tag.range(of: #"^\d{4}\.\d{2}\.\d{2}(\.\d+)?$"#, options: .regularExpression) != nil
    }

    /// Finds `<sha256>  <fileName>` in a sha256sum-style list.
    static func checksum(in text: String, for fileName: String) -> String? {
        for line in text.split(whereSeparator: \.isNewline) {
            let parts = line.split(whereSeparator: \.isWhitespace)
            guard parts.count == 2, parts[0].count == 64,
                  parts[1].trimmingPrefix("*") == fileName else { continue }
            return parts[0].lowercased()
        }
        return nil
    }

    static func sha256(of file: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: file)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let chunk = try handle.read(upToCount: 1 << 20), !chunk.isEmpty { hasher.update(data: chunk) }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private static func unzip(_ archive: URL, into directory: URL) async throws {
        let process = Process(), out = Pipe(), err = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        process.arguments = ["-x", "-k", archive.path, directory.path]
        process.standardOutput = out; process.standardError = err
        process.standardInput = FileHandle.nullDevice
        let result = try await ProcessCapture.run(process, stdout: out, stderr: err)
        guard result.status == 0,
              FileManager.default.isExecutableFile(atPath: directory.appendingPathComponent(executableName).path)
        else { throw UpdateError.extractionFailed }
    }

    private static func reportedVersion(of executable: URL) async throws -> String {
        let process = Process(), out = Pipe(), err = Pipe()
        process.executableURL = executable
        process.arguments = ["--version"]
        process.standardOutput = out; process.standardError = err
        process.standardInput = FileHandle.nullDevice
        let result = try await ProcessCapture.run(process, stdout: out, stderr: err, launch: {
            try process.run()
            DispatchQueue.global().asyncAfter(deadline: .now() + validationTimeout) {
                if process.isRunning { process.terminate() }
            }
        })
        let text = String(decoding: result.status == 0 ? result.stdout : result.stderr, as: UTF8.self)
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard result.status == 0 else { throw UpdateError.validationFailed("exit \(result.status): \(text.prefix(200))") }
        return text
    }
}

// MARK: - GitHub source

extension YtDlpUpdater.Source {
    /// Generic product User-Agent only — no user or machine identifiers.
    private static let userAgent = "VideoDownloader-macOS"

    public static let github = YtDlpUpdater.Source(
        latestTag: {
            let session = URLSession(configuration: .ephemeral)
            defer { session.finishTasksAndInvalidate() }
            var request = URLRequest(url: YtDlpUpdater.latestReleaseAPI, timeoutInterval: 15)
            request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
            request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
            let (data, response) = try await session.data(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? -1
            guard status == 200 else { throw YtDlpUpdater.UpdateError.httpStatus(status) }
            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let tag = json["tag_name"] as? String else { throw YtDlpUpdater.UpdateError.invalidTag("<missing>") }
            return tag
        },
        download: { url in
            let configuration = URLSessionConfiguration.ephemeral
            configuration.timeoutIntervalForRequest = 30
            configuration.timeoutIntervalForResource = 600
            let session = URLSession(configuration: configuration)
            defer { session.finishTasksAndInvalidate() }
            var request = URLRequest(url: url)
            request.setValue(userAgent, forHTTPHeaderField: "User-Agent")
            let (temporary, response) = try await session.download(for: request)
            let status = (response as? HTTPURLResponse)?.statusCode ?? -1
            guard status == 200 else {
                try? FileManager.default.removeItem(at: temporary)
                throw YtDlpUpdater.UpdateError.httpStatus(status)
            }
            // URLSession may reclaim its temp file; move it somewhere the caller owns.
            let owned = FileManager.default.temporaryDirectory.appendingPathComponent("anydl-\(UUID().uuidString)")
            try FileManager.default.moveItem(at: temporary, to: owned)
            return owned
        }
    )
}

// MARK: - Once-per-launch entry point

/// Runs the updater once per process; every caller awaits the same work.
public enum ManagedYtDlp {
    private static let logger = Logger(subsystem: "com.anydownloader.app", category: "yt-dlp-updater")

    private static let run = Task<YtDlpUpdater.Outcome?, Never> {
        do {
            let outcome = try await YtDlpUpdater().update()
            logger.info("yt-dlp: \(String(describing: outcome), privacy: .public)")
            return outcome
        } catch {
            // Offline or GitHub hiccup: keep using the last working managed or Homebrew copy.
            logger.error("yt-dlp update failed: \(error.localizedDescription, privacy: .public)")
            return nil
        }
    }

    @discardableResult
    public static func ensureUpdated() async -> YtDlpUpdater.Outcome? { await run.value }
}
