import Foundation

// MARK: - FileWriter actor
// Serialises seek+write operations on a single FileHandle across concurrent tasks.
// An actor is the Swift 6-safe replacement for NSLock around non-Sendable types.
private actor FileWriter {
    private let handle: FileHandle

    init(_ handle: FileHandle) { self.handle = handle }

    func write(_ data: Data, at offset: UInt64) throws {
        try handle.seek(toOffset: offset)
        handle.write(data)
    }

    func close() { try? handle.close() }
}

// MARK: - HTTPDownloadTask

public final class HTTPDownloadTask: DownloadTaskProtocol, @unchecked Sendable {
    public let itemID: UUID
    private let item: DownloadItem
    private let options: DownloadOptions
    private let onUpdate: @Sendable (DownloadItem) -> Void
    private let lock = NSLock()

    // Persisted across pause/resume — cleared only on cancel
    private var completedSegmentSet = Set<Int>()
    private var savedContentLength: Int64 = 0
    private var savedAcceptsRanges = false
    private var savedThreadCount = 0
    private var savedOutputURL: URL?
    private var latestProgress: Double = 0

    // Per-run state
    private var currentSession: URLSession?
    private var isStopped = false   // true while paused or cancelled

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
        guard let url = URL(string: item.url) else { throw URLError(.badURL) }
        lock.withLock { isStopped = false }

        // ── First run only: HEAD request + file setup ──────────────────────────
        if lock.withLock({ savedContentLength == 0 }) {
            let probe = URLSession(configuration: .default)
            defer { probe.invalidateAndCancel() }
            var headReq = URLRequest(url: url)
            headReq.httpMethod = "HEAD"
            let (_, headResponse) = try await probe.data(for: headReq)
            let headers = (headResponse as? HTTPURLResponse)?
                .allHeaderFields as? [String: String] ?? [:]
            let contentLength = Int64(headers["Content-Length"] ?? "") ?? 0
            let acceptsRanges = headers["Accept-Ranges"] == "bytes"
            let threadCount = (contentLength > 0 && acceptsRanges) ? options.threadCount : 1
            let outputURL = options.outputDirectory
                .appendingPathComponent(url.lastPathComponent)

            FileManager.default.createFile(atPath: outputURL.path, contents: nil)
            if contentLength > 0 {
                let fh = try FileHandle(forWritingTo: outputURL)
                try fh.truncate(atOffset: UInt64(contentLength))
                try fh.close()
            }
            lock.withLock {
                savedContentLength = max(contentLength, 1)
                savedAcceptsRanges = acceptsRanges
                savedThreadCount = threadCount
                savedOutputURL = outputURL
            }
        }

        let (contentLength, acceptsRanges, threadCount, outputURL): (Int64, Bool, Int, URL) =
            lock.withLock { (savedContentLength, savedAcceptsRanges, savedThreadCount, savedOutputURL!) }

        // Re-create the file if it was deleted between sessions
        if !FileManager.default.fileExists(atPath: outputURL.path) {
            FileManager.default.createFile(atPath: outputURL.path, contents: nil)
            if contentLength > 1 {
                let fh = try FileHandle(forWritingTo: outputURL)
                try fh.truncate(atOffset: UInt64(contentLength))
                try fh.close()
            }
            lock.withLock { completedSegmentSet.removeAll() }
        }

        let fileHandle = try FileHandle(forWritingTo: outputURL)
        let writer = FileWriter(fileHandle)
        let sess = URLSession(configuration: .default)
        lock.withLock { currentSession = sess }

        // ── Rebuild segment manager; pre-credit already-completed segments ──────
        let mgr = MutableSegmentManager(totalBytes: contentLength, threadCount: threadCount)
        let alreadyDone = lock.withLock { completedSegmentSet }
        for idx in alreadyDone where idx < mgr.segmentCount {
            let seg = mgr.segment(at: idx)
            mgr.markReceived(segmentIndex: idx, bytes: Int64(seg.range.count))
            mgr.markComplete(segmentIndex: idx)
        }

        let startProgress = mgr.progress
        lock.withLock { latestProgress = startProgress }
        onUpdate(item.with(state: .active, progress: startProgress,
                           totalBytes: contentLength > 1 ? contentLength : nil))

        // ── Download incomplete segments ────────────────────────────────────────
        defer {
            Task { await writer.close() }
            lock.withLock { currentSession = nil }
        }

        try await withThrowingTaskGroup(of: Void.self) { group in
            for i in 0..<mgr.segmentCount {
                if alreadyDone.contains(i) { continue }
                guard !lock.withLock({ isStopped }) else { break }
                let segment = mgr.segment(at: i)
                group.addTask {
                    do {
                        var req = URLRequest(url: url)
                        if acceptsRanges {
                            req.setValue(
                                "bytes=\(segment.range.lowerBound)-\(segment.range.upperBound - 1)",
                                forHTTPHeaderField: "Range"
                            )
                        }
                        let (data, _) = try await sess.data(for: req)

                        // Actor serialises seek+write; prevents interleaving across segments
                        try await writer.write(data, at: UInt64(segment.range.lowerBound))

                        mgr.markReceived(segmentIndex: i, bytes: Int64(data.count))
                        mgr.markComplete(segmentIndex: i)
                        let prog = mgr.progress
                        self.lock.withLock {
                            self.completedSegmentSet.insert(i)
                            self.latestProgress = prog
                        }
                        self.onUpdate(self.item.with(
                            state: .active,
                            progress: prog,
                            totalBytes: contentLength > 1 ? contentLength : nil,
                            receivedBytes: mgr.totalReceivedBytes
                        ))
                    } catch let err as URLError where err.code == .cancelled {
                        // Session was invalidated (pause or cancel) — silently stop
                    }
                }
            }
            try await group.waitForAll()
        }

        let stopped = lock.withLock { isStopped }
        if stopped { return }   // pause()/cancel() already emitted the new state

        onUpdate(item.with(state: .completed, progress: mgr.progress,
                           filePath: outputURL.path, receivedBytes: mgr.totalReceivedBytes))
    }

    public func pause() async {
        let prog = lock.withLock {
            isStopped = true
            currentSession?.invalidateAndCancel()
            currentSession = nil
            return latestProgress
        }
        onUpdate(item.with(state: .paused, progress: prog, speedBytesPerSec: 0))
    }

    public func resume() async throws {
        try await start()
    }

    public func cancel() async {
        lock.withLock {
            isStopped = true
            currentSession?.invalidateAndCancel()
            currentSession = nil
            // Clear all state so a fresh retry starts from scratch
            completedSegmentSet.removeAll()
            savedContentLength = 0
        }
        onUpdate(item.with(state: .failed, errorMessage: "Cancelled"))
    }

    func supportsRangeRequests(headers: [String: String]) -> Bool {
        headers["Accept-Ranges"] == "bytes"
    }
}

/// Thread-safe wrapper around SegmentManager for use in concurrent tasks.
private final class MutableSegmentManager: @unchecked Sendable {
    private let lock = NSLock()
    private var mgr: SegmentManager

    init(totalBytes: Int64, threadCount: Int) {
        self.mgr = SegmentManager(totalBytes: totalBytes, threadCount: threadCount)
    }

    var segmentCount: Int { mgr.segments.count }

    func segment(at index: Int) -> Segment {
        lock.withLock { mgr.segments[index] }
    }

    func markReceived(segmentIndex: Int, bytes: Int64) {
        lock.withLock { mgr.markReceived(segmentIndex: segmentIndex, bytes: bytes) }
    }

    func markComplete(segmentIndex: Int) {
        lock.withLock { mgr.markComplete(segmentIndex: segmentIndex) }
    }

    var progress: Double {
        lock.withLock { mgr.progress }
    }

    var totalReceivedBytes: Int64 {
        lock.withLock { mgr.totalReceivedBytes }
    }
}
