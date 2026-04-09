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
    private let lock = NSLock()
    /// Tracks the latest progress so pause() preserves it instead of resetting to 0.
    private var latestProgress: Double = 0
    private var latestSpeed: Int64 = 0
    private var wasInterrupted = false

    public init(
        item: DownloadItem,
        options: DownloadOptions,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) {
        self.itemID = item.id
        self.item = item
        self.options = options
        self.onUpdate = onUpdate
        self.latestProgress = item.progress
    }

    public func start() async throws {
        let ytdlpPath = try Self.findYtDlp()
        let args = YtDlpEngine.buildArguments(url: item.url, options: options)
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: ytdlpPath)
        proc.arguments = args
        proc.currentDirectoryURL = options.outputDirectory
        proc.environment = YtDlpTask.enrichedEnvironment()

        let pipe = Pipe()
        proc.standardOutput = pipe
        proc.standardError = pipe
        lock.withLock {
            process = proc
            wasInterrupted = false
        }

        onUpdate(item.with(state: .active, progress: latestProgress))
        try proc.run()

        let handle = pipe.fileHandleForReading
        let updateItem = item
        let updateCallback = onUpdate
        let titleBox    = _TitleBox()
        let filePathBox = _TitleBox()   // tracks the final output file path

        // Pre-seed filePath from what we already know in memory:
        // customFilename is exact; videoInfo title gives an approximate path.
        // yt-dlp output lines (Destination / already-downloaded / Merger) override this if present.
        if let customFilename = options.customFilename {
            filePathBox.value = options.outputDirectory
                .appendingPathComponent(customFilename).path
        } else if let title = item.videoInfo?.title ?? item.title {
            let ext = (options.format == "native" || options.format.isEmpty) ? "mp4" : options.format
            let sanitized = YtDlpTask.sanitizeFilename(title)
            filePathBox.value = options.outputDirectory
                .appendingPathComponent("\(sanitized).\(ext)").path
        }
        let progressBox = _ProgressBox(initial: latestProgress)

        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            handle.readabilityHandler = { [weak self] fh in
                let data = fh.availableData
                guard !data.isEmpty else { return }
                let output = String(data: data, encoding: .utf8) ?? ""
                for line in output.components(separatedBy: "\n") {
                    // "[download] Destination: /path/to/file.mp4"  — initial destination
                    if line.contains("[download] Destination:"),
                       let range = line.range(of: "[download] Destination: ") {
                        let fullPath = String(line[range.upperBound...])
                            .trimmingCharacters(in: .whitespacesAndNewlines)
                        filePathBox.value = fullPath
                        if titleBox.value == nil {
                            let title = URL(fileURLWithPath: fullPath)
                                .deletingPathExtension().lastPathComponent
                            titleBox.value = title
                            updateCallback(updateItem.with(state: .active, progress: progressBox.value,
                                                           title: title))
                        }
                    }
                    // "[download] /path/to/file.mp4 has already been downloaded"
                    if let path = YtDlpTask.parseAlreadyDownloadedPath(line) {
                        filePathBox.value = path
                        if titleBox.value == nil {
                            let title = URL(fileURLWithPath: path)
                                .deletingPathExtension().lastPathComponent
                            titleBox.value = title
                            updateCallback(updateItem.with(state: .active, progress: 1.0, title: title))
                        }
                    }
                    // "[Merger] Merging formats into "final.mp4"" — overrides destination
                    if line.contains("[Merger] Merging formats into"),
                       let start = line.range(of: "\""),
                       let end   = line.range(of: "\"", options: .backwards) {
                        let merged = String(line[start.upperBound..<end.lowerBound])
                            .trimmingCharacters(in: .whitespacesAndNewlines)
                        if !merged.isEmpty { filePathBox.value = merged }
                    }
                    if let update = YtDlpTask.parseProgress(line) {
                        progressBox.value = update.progress
                        self?.lock.withLock { self?.latestProgress = update.progress }
                        updateCallback(updateItem.with(
                            state: .active,
                            progress: update.progress,
                            speedBytesPerSec: update.speedBytesPerSec,
                            etaSeconds: update.etaSeconds,
                            title: titleBox.value
                        ))
                    }
                }
            }
            proc.terminationHandler = { _ in
                handle.readabilityHandler = nil
                continuation.resume()
            }
        }

        let interrupted = lock.withLock { wasInterrupted }
        if interrupted { return } // pause/cancel already published state

        if proc.terminationStatus == 0 {
            // Build the absolute file path from what yt-dlp reported.
            // filePathBox.value is normally already absolute (--output uses the full dir path),
            // but guard against a bare filename just in case.
            let finalPath: String? = filePathBox.value.map { captured in
                captured.hasPrefix("/")
                    ? captured
                    : options.outputDirectory.appendingPathComponent(captured).path
            }
            onUpdate(item.with(state: .completed, progress: 1.0, filePath: finalPath))
        } else {
            onUpdate(item.with(state: .failed,
                               errorMessage: "yt-dlp exited with code \(proc.terminationStatus)"))
        }
    }

    public func pause() async {
        lock.withLock {
            wasInterrupted = true
            process?.interrupt()
        }
        onUpdate(item.with(state: .paused, progress: latestProgress,
                           speedBytesPerSec: 0))
    }

    public func resume() async throws {
        // Wait for the previous process (sent SIGINT during pause) to fully exit before
        // spawning a new one. Without this, two yt-dlp instances can write to the same
        // .part files simultaneously, producing a corrupted merged output.
        if let old = lock.withLock({ process }), old.isRunning {
            await withCheckedContinuation { (cont: CheckedContinuation<Void, Never>) in
                DispatchQueue.global().async { old.waitUntilExit(); cont.resume() }
            }
        }
        try await start()
    }

    public func cancel() async {
        lock.withLock {
            wasInterrupted = true
            process?.terminate()
        }
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
        let current = env["PATH"] ?? "/usr/bin:/bin:/usr/sbin:/sbin"
        if !current.contains(brewBin) {
            env["PATH"] = "\(brewBin):/usr/local/bin:\(current)"
        }
        return env
    }

    static func findYtDlp() throws -> String {
        if let p = Bundle.main.resourceURL?.appendingPathComponent("yt-dlp").path,
           FileManager.default.fileExists(atPath: p) { return p }
        let candidates = ["/opt/homebrew/bin/yt-dlp", "/usr/local/bin/yt-dlp", "/usr/bin/yt-dlp"]
        guard let path = candidates.first(where: { FileManager.default.fileExists(atPath: $0) }) else {
            throw NSError(domain: "YtDlp", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "yt-dlp not found. Install with: brew install yt-dlp"])
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
