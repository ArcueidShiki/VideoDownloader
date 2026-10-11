import Foundation

public struct YtDlpProgressUpdate: Sendable {
    public let progress: Double
    public let speedBytesPerSec: Int64
    public let etaSeconds: Int?
}

public final class YtDlpTask: DownloadTaskProtocol, @unchecked Sendable {
    public let itemID: UUID
    private let item: DownloadItem
    private let options: DownloadOptions
    private let onUpdate: @Sendable (DownloadItem) -> Void
    private var process: Process?
    private var processGroup: pid_t?
    private let lock = NSLock()
    /// Tracks the latest progress so pause() preserves it instead of resetting to 0.
    private var latestProgress: Double = 0
    private var latestSpeed: Int64 = 0
    private var wasInterrupted = false

    public init(
        item: DownloadItem,
        options: DownloadOptions,
        executablePath: String? = nil,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) {
        self.executableOverride = executablePath
        self.itemID = item.id
        self.item = item
        self.options = options
        self.onUpdate = onUpdate
        self.latestProgress = item.progress
    }

    public func start() async throws {
        guard lock.withLock({ () -> Bool in
            guard !running else { return false }
            running = true
            wasInterrupted = false
            return true
        }) else {
            throw NSError(domain: "YtDlp", code: 3, userInfo: [NSLocalizedDescriptionKey:
                "The previous download is still stopping. Wait for it to finish before retrying."])
        }
        defer { lock.withLock { running = false; process = nil; processGroup = nil } }
        do {
            let ytdlpPath = try executableOverride ?? Self.findYtDlp()
            try FileManager.default.createDirectory(at: options.outputDirectory, withIntermediateDirectories: true)
            let proc = Process()
            proc.executableURL = URL(fileURLWithPath: ytdlpPath)
            proc.arguments = YtDlpEngine.buildArguments(url: item.url, options: options)
            proc.currentDirectoryURL = options.outputDirectory
            proc.environment = Self.enrichedEnvironment()
            proc.standardInput = FileHandle.nullDevice
            let stdout = Pipe(), stderr = Pipe()
            proc.standardOutput = stdout
            proc.standardError = stderr
            let finalPath = _TitleBox()
            let output = try await ProcessCapture.run(proc, stdout: stdout, stderr: stderr, launch: { [self] in
                try lock.withLock {
                    guard !wasInterrupted else { throw CancellationError() }
                    process = proc
                    try proc.run()
                    // Foundation creates a separate process group on macOS. Verify
                    // ownership before ever signalling a negative PID.
                    let pid = proc.processIdentifier
                    guard getpgid(pid) == pid, pid != getpgrp() else {
                        proc.terminate()
                        throw NSError(domain: "YtDlp", code: 4, userInfo: [NSLocalizedDescriptionKey:
                            "Could not isolate the download process group."])
                    }
                    processGroup = pid
                    onUpdate(item.with(state: .active, progress: latestProgress))
                }
            }) { [self] line in
                lock.withLock {
                    guard !wasInterrupted else { return }
                    let marker = "__ANYDOWNLOADER_FILE__"
                    if line.hasPrefix(marker),
                       let data = String(line.dropFirst(marker.count)).data(using: .utf8),
                       let path = try? JSONDecoder().decode(String.self, from: data) {
                        finalPath.value = path
                    }
                    if let update = Self.parseProgress(line) {
                        latestProgress = update.progress
                        onUpdate(item.with(state: .active, progress: update.progress,
                                           speedBytesPerSec: update.speedBytesPerSec, etaSeconds: update.etaSeconds))
                    }
                }
            }
            guard !lock.withLock({ wasInterrupted }) else { return }
            guard output.status == 0 else {
                let message = String(decoding: output.stderr, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
                throw NSError(domain: "YtDlp", code: Int(output.status), userInfo: [NSLocalizedDescriptionKey:
                    message.isEmpty ? "yt-dlp exited with code \(output.status)" : message])
            }
            guard let path = finalPath.value,
                  let attributes = try? FileManager.default.attributesOfItem(atPath: path),
                  attributes[.type] as? FileAttributeType == .typeRegular,
                  (attributes[.size] as? NSNumber)?.int64Value ?? 0 > 0 else {
                throw NSError(domain: "YtDlp", code: 2, userInfo: [NSLocalizedDescriptionKey:
                    "yt-dlp did not produce a non-empty output file. Retry or choose another format."])
            }
            onUpdate(item.with(state: .completed, progress: 1, filePath: path))
        } catch {
            if !lock.withLock({ wasInterrupted }) {
                onUpdate(item.with(state: .failed, errorMessage: error.localizedDescription))
            }
        }
    }

    private var running = false
    private let executableOverride: String?

    private func stop() async {
        let group = lock.withLock { () -> pid_t? in
            wasInterrupted = true
            if let processGroup { kill(-processGroup, SIGINT) }
            return processGroup
        }
        // A descendant (for example FFmpeg) may keep writing and retain both
        // pipes after the extractor exits. Signal the verified owned group even
        // when Process.isRunning is false, and never treat a deadline as EOF.
        var tick = 0
        while lock.withLock({ running }) {
            if let group {
                if tick == 30 { kill(-group, SIGTERM) }
                if tick >= 60 && tick % 30 == 0 { kill(-group, SIGKILL) }
            }
            tick += 1
            try? await Task.sleep(for: .milliseconds(100))
        }
    }

    public func pause() async {
        await stop()
        onUpdate(item.with(state: .paused, progress: lock.withLock { latestProgress }, speedBytesPerSec: 0))
    }

    public func resume() async throws {
        await stop()
        try await start()
    }

    public func cancel() async {
        await stop()
        onUpdate(item.with(state: .failed, errorMessage: "Cancelled"))
    }

    // MARK: - Progress parsing

    // Compiled once at class-load time — never re-compile per progress line.
    private static let _progressRegex = try! NSRegularExpression(
        pattern: #"\[download\]\s+([\d.]+)%.*?at\s+([\d.]+)(KiB|MiB|GiB)/s.*?ETA\s+([\d:]+)"#
    )

    public static func parseProgress(_ line: String) -> YtDlpProgressUpdate? {
        guard line.contains("[download]"), line.contains("%") else { return nil }
        guard let match = _progressRegex.firstMatch(in: line,
                                                     range: NSRange(line.startIndex..., in: line)) else { return nil }
        func capture(_ i: Int) -> String? {
            guard let r = Range(match.range(at: i), in: line) else { return nil }
            return String(line[r])
        }
        guard let pct = capture(1).flatMap(Double.init),
              let speed = capture(2).flatMap(Double.init),
              let unit = capture(3),
              let eta = capture(4) else { return nil }
        let mult: Double
        switch unit {
        case "KiB": mult = 1024
        case "MiB": mult = 1_048_576
        default:    mult = 1_073_741_824
        }
        return YtDlpProgressUpdate(
            progress: pct / 100,
            speedBytesPerSec: Int64(speed * mult),
            etaSeconds: parseETA(eta)
        )
    }

    /// Mirrors yt-dlp's filename sanitization for macOS:
    /// replaces characters forbidden in HFS+ paths with `_`.
    public static func sanitizeFilename(_ title: String) -> String {
        let forbidden: Set<Character> = ["/", ":", "*", "?", "\"", "<", ">", "|", "\0"]
        return title.map { forbidden.contains($0) ? "_" : String($0) }.joined()
    }

    /// Parses "[download] /path/to/file.mp4 has already been downloaded"
    /// Returns the absolute file path, or nil if the line doesn't match.
    public static func parseAlreadyDownloadedPath(_ line: String) -> String? {
        guard line.contains("[download]"),
              line.contains("has already been downloaded"),
              let downloadRange = line.range(of: "[download] "),
              let alreadyRange  = line.range(of: " has already been downloaded")
        else { return nil }
        let path = String(line[downloadRange.upperBound..<alreadyRange.lowerBound])
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return path.isEmpty ? nil : path
    }

    public static func parseETA(_ eta: String) -> Int? {
        let parts = eta.components(separatedBy: ":").compactMap(Int.init)
        switch parts.count {
        case 2: return parts[0] * 60 + parts[1]
        case 3: return parts[0] * 3600 + parts[1] * 60 + parts[2]
        default: return nil
        }
    }

    // MARK: - Path resolution

    /// Returns the current process environment with Homebrew paths prepended so that
    /// yt-dlp can invoke ffmpeg (needed for stream merging) even when the app is
    /// launched from Finder/Dock, where the default PATH is /usr/bin:/bin only.
    static func enrichedEnvironment() -> [String: String] {
        var env = ProcessInfo.processInfo.environment
        let brewBin = FileManager.default.fileExists(atPath: "/opt/homebrew/bin")
            ? "/opt/homebrew/bin" : "/usr/local/bin"
        let resources = Bundle.main.resourceURL?.path ?? ""
        let current = env["PATH"] ?? "/usr/bin:/bin:/usr/sbin:/sbin"
        if !current.contains(brewBin) {
            env["PATH"] = "\(brewBin):/usr/local/bin:\(current)"
        }
        env["PATH"] = "\(resources):\(env["PATH"] ?? current)"
        return env
    }

    static func findYtDlp() throws -> String {
        guard let path = DependencyStatus.find("yt-dlp") else {
            throw NSError(domain: "YtDlp", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "yt-dlp not found. Install with: brew install yt-dlp deno ffmpeg"])
        }
        return path
    }
}

// MARK: - Thread-safe boxes

final class _TitleBox: @unchecked Sendable {
    private let lock = NSLock()
    private var _value: String?
    var value: String? {
        get { lock.withLock { _value } }
        set { lock.withLock { _value = newValue } }
    }
}

final class _ProgressBox: @unchecked Sendable {
    private let lock = NSLock()
    private var _value: Double
    init(initial: Double) { _value = initial }
    var value: Double {
        get { lock.withLock { _value } }
        set { lock.withLock { _value = newValue } }
    }
}
