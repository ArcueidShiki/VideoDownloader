import Foundation

// MARK: - FileWriter actor
// Serialises seek+write operations on a single FileHandle across concurrent tasks.
// An actor is the Swift 6-safe replacement for NSLock around non-Sendable types.
private actor FileWriter {
    private let handle: FileHandle

    init(_ handle: FileHandle) { self.handle = handle }

    func write(_ data: Data, at offset: UInt64) throws {
        try handle.seek(toOffset: offset)
        try handle.write(contentsOf: data)
    }

    func truncate(to length: UInt64) throws { try handle.truncate(atOffset: length) }

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
    private var savedRepresentationLength: Int64?
    private var savedContentCoding = ""
    private var savedAcceptsRanges = false
    private var savedThreadCount = 0
    private var savedOutputURL: URL?
    private var latestProgress: Double = 0

    // Per-run state
    private var currentSession: URLSession?
    private var runningTask: (id: UUID, task: Task<Void, Error>)?
    private let sessionConfiguration: URLSessionConfiguration
    private var isStopped = false   // true while paused or cancelled

    public init(
        item: DownloadItem,
        options: DownloadOptions,
        sessionConfiguration: URLSessionConfiguration = .default,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) {
        self.itemID = item.id
        self.item = item
        self.options = options
        self.sessionConfiguration = sessionConfiguration
        self.onUpdate = onUpdate
        self.latestProgress = item.progress
    }

    public func start() async throws {
        let run = lock.withLock { () -> (id: UUID, task: Task<Void, Error>) in
            if let runningTask { return runningTask }
            isStopped = false
            let run = (id: UUID(), task: Task {
                do { try await self.performDownload() }
                catch {
                    if !self.lock.withLock({ self.isStopped }) { throw error }
                }
            })
            runningTask = run
            return run
        }
        defer {
            lock.withLock {
                if runningTask?.id == run.id { runningTask = nil }
            }
        }
        try await run.task.value
    }

    private func performDownload() async throws {
        guard let url = URL(string: item.url) else { throw URLError(.badURL) }
        guard !lock.withLock({ isStopped }) else { return }

        // ── First run only: HEAD request + file setup ──────────────────────────
        if lock.withLock({ savedContentLength == 0 }) {
            let probe = URLSession(configuration: sessionConfiguration)
            defer { probe.invalidateAndCancel() }
            let probing = lock.withLock {
                guard !isStopped else { return false }
                currentSession = probe
                return true
            }
            guard probing else { return }
            var headReq = URLRequest(url: url)
            headReq.httpMethod = "HEAD"
            headReq.setValue("identity", forHTTPHeaderField: "Accept-Encoding")
            let (_, headResponse) = try await probe.data(for: headReq)
            guard !lock.withLock({ isStopped }) else { return }
            guard let response = headResponse as? HTTPURLResponse else { throw URLError(.badServerResponse) }
            guard (200..<300).contains(response.statusCode) else {
                throw Self.responseError(response.statusCode)
            }
            // Content-Length and byte ranges describe the encoded representation
            // (RFC 9110, sections 8.4/14.1.2). URLSession returns decoded Data.
            let contentLength = Self.isIdentity(response)
                ? Int64(response.value(forHTTPHeaderField: "Content-Length") ?? "") ?? 0 : 0
            let acceptsRanges = contentLength > 0 && Self.isIdentity(response)
                && response.value(forHTTPHeaderField: "Accept-Ranges")?.lowercased() == "bytes"
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
                savedRepresentationLength = Int64(response.value(forHTTPHeaderField: "Content-Length") ?? "")
                savedContentCoding = Self.contentCoding(response)
                savedAcceptsRanges = acceptsRanges
                savedThreadCount = threadCount
                savedOutputURL = outputURL
            }
        }

        let (contentLength, acceptsRanges, threadCount, outputURL): (Int64, Bool, Int, URL) =
            lock.withLock { (savedContentLength, savedAcceptsRanges, savedThreadCount, savedOutputURL!) }
        let (representationLength, contentCoding) = lock.withLock { (savedRepresentationLength, savedContentCoding) }

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
        let sess = URLSession(configuration: sessionConfiguration)
        let downloading = lock.withLock {
            guard !isStopped else { return false }
            currentSession = sess
            return true
        }
        guard downloading else { await writer.close(); sess.invalidateAndCancel(); return }

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
            sess.invalidateAndCancel()
            lock.withLock { currentSession = nil }
        }

        do {
            try await withThrowingTaskGroup(of: Void.self) { group in
                for i in 0..<mgr.segmentCount {
                    if alreadyDone.contains(i) { continue }
                    guard !lock.withLock({ isStopped }) else { break }
                    let segment = mgr.segment(at: i)
                    group.addTask {
                        do {
                            var req = URLRequest(url: url)
                            req.setValue("identity", forHTTPHeaderField: "Accept-Encoding")
                            if acceptsRanges {
                                req.setValue(
                                    "bytes=\(segment.range.lowerBound)-\(segment.range.upperBound - 1)",
                                    forHTTPHeaderField: "Range"
                                )
                            }
                            let (data, rawResponse) = try await sess.data(for: req)
                            guard !self.lock.withLock({ self.isStopped }) else { return }
                            guard let response = rawResponse as? HTTPURLResponse else { throw URLError(.badServerResponse) }
                            guard response.statusCode == (acceptsRanges ? 206 : 200) else {
                                throw Self.responseError(response.statusCode)
                            }
                            guard !data.isEmpty else { throw URLError(.zeroByteResource) }
                            let responseLength = Int64(response.value(forHTTPHeaderField: "Content-Length") ?? "")
                            if !acceptsRanges, Self.contentCoding(response) == contentCoding,
                               let representationLength, let responseLength,
                               responseLength != representationLength {
                                throw URLError(.networkConnectionLost)
                            }
                            if acceptsRanges {
                                let expected = "bytes \(segment.range.lowerBound)-\(segment.range.upperBound - 1)/\(contentLength)"
                                guard Self.isIdentity(response),
                                      response.value(forHTTPHeaderField: "Content-Range") == expected,
                                      data.count == segment.range.count else { throw URLError(.badServerResponse) }
                            } else if Self.isIdentity(response) {
                                if let responseLength, data.count != responseLength {
                                    throw URLError(.networkConnectionLost)
                                }
                                if contentLength > 1 && data.count != contentLength {
                                    throw URLError(.networkConnectionLost)
                                }
                            }
                            // For content-coded full responses, URLSession handles
                            // transport framing and decoding. Its decoded Data length must
                            // never be compared with the compressed Content-Length.

                            // Actor serialises seek+write; prevents interleaving across segments
                            try await writer.write(data, at: UInt64(segment.range.lowerBound))
                            if !acceptsRanges { try await writer.truncate(to: UInt64(data.count)) }

                            mgr.markReceived(segmentIndex: i, bytes: Int64(data.count))
                            mgr.markComplete(segmentIndex: i)
                            let prog = acceptsRanges ? mgr.progress : 1
                            self.lock.withLock {
                                self.completedSegmentSet.insert(i)
                                self.latestProgress = prog
                                if !acceptsRanges { self.savedContentLength = Int64(data.count) }
                            }
                            self.onUpdate(self.item.with(
                                state: .active,
                                progress: prog,
                                totalBytes: acceptsRanges ? contentLength : Int64(data.count),
                                receivedBytes: mgr.totalReceivedBytes
                            ))
                        } catch let err as URLError where err.code == .cancelled {
                            // Session was invalidated (pause or cancel) — silently stop
                        }
                    }
                }
                try await group.waitForAll()
            }

        } catch {
            await writer.close()
            throw error
        }
        await writer.close()
        let stopped = lock.withLock { isStopped }
        if stopped { return }   // pause()/cancel() already emitted the new state

        onUpdate(item.with(state: .completed, progress: 1,
                           filePath: outputURL.path, totalBytes: mgr.totalReceivedBytes,
                           receivedBytes: mgr.totalReceivedBytes))
    }

    private func stop() async {
        let run = lock.withLock {
            isStopped = true
            currentSession?.invalidateAndCancel()
            return runningTask
        }
        // Drain the HEAD probe, segment writers and callbacks before resume can
        // reset the stop flag. Otherwise an old run can revive a paused row.
        if let run { _ = await run.task.result }
        lock.withLock {
            if runningTask?.id == run?.id { runningTask = nil }
            currentSession = nil
        }
    }

    public func pause() async {
        await stop()
        let prog = lock.withLock { latestProgress }
        onUpdate(item.with(state: .paused, progress: prog, speedBytesPerSec: 0))
    }

    public func resume() async throws {
        try await start()
    }

    public func cancel() async {
        await stop()
        lock.withLock {
            completedSegmentSet.removeAll()
            savedContentLength = 0
        }
        onUpdate(item.with(state: .failed, errorMessage: "Cancelled"))
    }

    private static func responseError(_ status: Int) -> NSError {
        NSError(domain: "HTTPDownload", code: status, userInfo: [NSLocalizedDescriptionKey:
            "The server returned HTTP \(status) instead of the requested media. Check the URL or retry later."])
    }

    private static func isIdentity(_ response: HTTPURLResponse) -> Bool {
        contentCoding(response).isEmpty
    }

    private static func contentCoding(_ response: HTTPURLResponse) -> String {
        (response.value(forHTTPHeaderField: "Content-Encoding") ?? "")
            .split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
            .filter { $0 != "identity" }
            .joined(separator: ",")
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
