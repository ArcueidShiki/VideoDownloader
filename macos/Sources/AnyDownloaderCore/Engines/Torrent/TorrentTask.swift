import Foundation

public struct TorrentProgressUpdate: Sendable {
    public let progress: Double
    public let speedBytesPerSec: Int64
    public let etaSeconds: Int?
}

public final class TorrentTask: DownloadTaskProtocol, @unchecked Sendable {
    public let itemID: UUID
    private let item: DownloadItem
    private let options: DownloadOptions
    private let onUpdate: @Sendable (DownloadItem) -> Void
    private var process: Process?
    private let lock = NSLock()
    /// Latest progress so pause() preserves it.
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
        let aria2Path = try Self.findAria2c()
        let args = TorrentEngine.buildArguments(url: item.url, options: options)
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: aria2Path)
        proc.arguments = args

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
        let cb = onUpdate
        let progressBox = _ProgressBox(initial: latestProgress)

        await withCheckedContinuation { continuation in
            handle.readabilityHandler = { [weak self] fh in
                let data = fh.availableData
                guard !data.isEmpty else { return }
                let output = String(data: data, encoding: .utf8) ?? ""
                for line in output.components(separatedBy: "\n") {
                    if let update = TorrentTask.parseProgress(line) {
                        // Monotonic clamp: never allow progress to go backward.
                        // Also capture wasInterrupted under the same lock to avoid a
                        // race where we call cb(state: .active) after pause() has
                        // already set wasInterrupted = true and sent .paused state.
                        let (monotonic, interrupted) = self?.lock.withLock { () -> (Double, Bool) in
                            let clamped = max(self?.latestProgress ?? 0, update.progress)
                            self?.latestProgress = clamped
                            return (clamped, self?.wasInterrupted ?? false)
                        } ?? (update.progress, false)
                        guard !interrupted else { continue }
                        progressBox.value = monotonic
                        cb(updateItem.with(
                            state: .active,
                            progress: monotonic,
                            speedBytesPerSec: update.speedBytesPerSec,
                            etaSeconds: update.etaSeconds
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
        if interrupted { return }

        let code = proc.terminationStatus
        if code == 0 {
            onUpdate(item.with(state: .completed, progress: 1.0,
                               filePath: options.outputDirectory.path))
        } else if code == 7 {
            // Exit 7 = "unfinished downloads" — treated as a network/connection failure,
            // not a permanent error. Let the user retry.
            onUpdate(item.with(state: .failed,
                               progress: latestProgress,
                               errorMessage: "No peers responded — check network or try again"))
        } else {
            onUpdate(item.with(state: .failed,
                               progress: latestProgress,
                               errorMessage: "aria2c error \(code)"))
        }
    }

    public func pause() async {
        lock.withLock {
            wasInterrupted = true
            process?.interrupt()
        }
        onUpdate(item.with(state: .paused, progress: latestProgress, speedBytesPerSec: 0))
    }

    public func resume() async throws {
        // Wait for the previous process (sent SIGINT during pause) to fully exit before
        // spawning a new one. Two concurrent aria2c instances writing to the same torrent
        // state files cause data corruption and download reset.
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
    // aria2c summary line: [#hash size/total(N%) CN:X DL:Y.YMiB ETA:1m30s]

    // Compiled once at class-load time — never re-compile per progress line.
    private static let _progressRegex = try! NSRegularExpression(
        pattern: #"\((\d+)%\).*?DL:([\d.]+)(KiB|MiB|GiB).*?ETA:([\w]+)"#
    )
    private static let _etaRegex = try! NSRegularExpression(pattern: #"(\d+)(h|m|s)"#)

    public static func parseProgress(_ line: String) -> TorrentProgressUpdate? {
        guard let match = _progressRegex.firstMatch(in: line,
                                                     range: NSRange(line.startIndex..., in: line)) else { return nil }
        func cap(_ i: Int) -> String? {
            guard let r = Range(match.range(at: i), in: line) else { return nil }
            return String(line[r])
        }
        guard let pct = cap(1).flatMap(Double.init),
              let sp  = cap(2).flatMap(Double.init),
              let unit = cap(3),
              let etaStr = cap(4) else { return nil }
        let mult: Double
        switch unit {
        case "KiB": mult = 1024
        case "MiB": mult = 1_048_576
        default:    mult = 1_073_741_824
        }
        return TorrentProgressUpdate(
            progress: pct / 100,
            speedBytesPerSec: Int64(sp * mult),
            etaSeconds: parseETA(etaStr)
        )
    }

    public static func parseETA(_ eta: String) -> Int? {
        var total = 0
        for m in _etaRegex.matches(in: eta, range: NSRange(eta.startIndex..., in: eta)) {
            guard let nr = Range(m.range(at: 1), in: eta),
                  let ur = Range(m.range(at: 2), in: eta),
                  let n = Int(eta[nr]) else { continue }
            switch eta[ur] {
            case "h": total += n * 3600
            case "m": total += n * 60
            case "s": total += n
            default: break
            }
        }
        return total > 0 ? total : nil
    }

    // MARK: - Path resolution

    static func findAria2c() throws -> String {
        if let p = Bundle.main.resourceURL?.appendingPathComponent("aria2c").path,
           FileManager.default.fileExists(atPath: p) { return p }
        let candidates = ["/opt/homebrew/bin/aria2c", "/usr/local/bin/aria2c", "/usr/bin/aria2c"]
        guard let path = candidates.first(where: { FileManager.default.fileExists(atPath: $0) }) else {
            throw NSError(domain: "Aria2", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "aria2c not found. Install with: brew install aria2"])
        }
        return path
    }
}
