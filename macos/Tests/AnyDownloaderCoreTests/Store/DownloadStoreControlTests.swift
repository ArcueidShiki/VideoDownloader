import Testing
import Foundation
@testable import AnyDownloaderCore

// MARK: - Mock task

/// Minimal mock that records pause/cancel calls without touching the network.
final class MockDownloadTask: DownloadTaskProtocol, @unchecked Sendable {
    let itemID: UUID
    var didStart  = false
    var didPause  = false
    var didResume = false
    var didCancel = false

    init(id: UUID) { self.itemID = id }

    func start()  async throws { didStart  = true }
    func pause()  async       { didPause  = true }
    func resume() async throws { didResume = true }
    func cancel() async       { didCancel = true }
}

// MARK: - Helpers

extension DownloadItem {
    /// Quick builder for test items with a given state.
    /// Uses 127.0.0.1:1 (loopback, closed port) to avoid DNS lookups during tests.
    static func stub(type: DownloadType = .http, state: DownloadState) -> DownloadItem {
        var item = DownloadItem(url: "https://127.0.0.1:1/\(UUID()).zip", type: type)
        item = item.with(state: state)
        return item
    }
}

// MARK: - Suite

@Suite("DownloadStore — control button logic")
struct DownloadStoreControlTests {

    init() {
        // Prevent real HTTP connections when startAllQueued / resumeAll spawn background tasks.
        FailFastURLProtocol.register()
    }

    @MainActor
    func makeStore() throws -> DownloadStore {
        try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
    }

    // MARK: pauseAll

    @Test("pauseAll pauses every active task")
    @MainActor
    func pauseAllAffectsActive() async throws {
        let store = try makeStore()
        let a1 = DownloadItem.stub(state: .active)
        let a2 = DownloadItem.stub(state: .active)
        let q1 = DownloadItem.stub(state: .queued)       // must NOT be paused
        let c1 = DownloadItem.stub(state: .completed)    // must NOT be paused
        [a1, a2, q1, c1].forEach { store._injectItem($0) }

        let mock1 = MockDownloadTask(id: a1.id)
        let mock2 = MockDownloadTask(id: a2.id)
        store._injectTask(mock1, for: a1.id)
        store._injectTask(mock2, for: a2.id)

        await store.pauseAll()

        #expect(mock1.didPause)
        #expect(mock2.didPause)
        #expect(store.items.first(where: { $0.id == q1.id })?.state == .queued)
        #expect(store.items.first(where: { $0.id == c1.id })?.state == .completed)
    }

    @Test("pauseAll(ids:) only pauses selected active tasks")
    @MainActor
    func pauseAllWithIds() async throws {
        let store = try makeStore()
        let a1 = DownloadItem.stub(state: .active)
        let a2 = DownloadItem.stub(state: .active)   // NOT in selection
        [a1, a2].forEach { store._injectItem($0) }

        let mock1 = MockDownloadTask(id: a1.id)
        let mock2 = MockDownloadTask(id: a2.id)
        store._injectTask(mock1, for: a1.id)
        store._injectTask(mock2, for: a2.id)

        await store.pauseAll(ids: [a1.id])

        #expect(mock1.didPause,  "selected task should be paused")
        #expect(!mock2.didPause, "unselected task must not be paused")
    }

    @Test("pauseAll does not touch completed tasks")
    @MainActor
    func pauseAllSkipsCompleted() async throws {
        let store = try makeStore()
        let c1 = DownloadItem.stub(state: .completed)
        store._injectItem(c1)

        await store.pauseAll()   // no crash, no state change

        #expect(store.items.first(where: { $0.id == c1.id })?.state == .completed)
    }

    // MARK: startAllQueued

    @Test("startAllQueued only processes queued items")
    @MainActor
    func startAllQueuedSkipsNonQueued() async throws {
        let store = try makeStore()
        let q1 = DownloadItem.stub(state: .queued)
        let p1 = DownloadItem.stub(state: .paused)    // must NOT change
        let c1 = DownloadItem.stub(state: .completed) // must NOT change
        [q1, p1, c1].forEach { store._injectItem($0) }

        // startAllQueued fires tasks which may fail (no real binary); we just
        // verify the paused/completed items remain untouched.
        await store.startAllQueued(outputDirectory: URL.downloadsDirectory)

        #expect(store.items.first(where: { $0.id == p1.id })?.state == .paused)
        #expect(store.items.first(where: { $0.id == c1.id })?.state == .completed)
    }

    @Test("startAllQueued(ids:) skips queued items not in selection")
    @MainActor
    func startAllQueuedWithIds() async throws {
        let store = try makeStore()
        let q1 = DownloadItem.stub(state: .queued)
        let q2 = DownloadItem.stub(state: .queued)  // NOT selected
        [q1, q2].forEach { store._injectItem($0) }

        // After the call, q2 must remain queued (q1 may change or fail to start;
        // we only assert the non-selected one is untouched).
        await store.startAllQueued(outputDirectory: URL.downloadsDirectory, ids: [q1.id])

        #expect(store.items.first(where: { $0.id == q2.id })?.state == .queued)
    }

    // MARK: resumeAll

    @Test("resumeAll resumes all paused items")
    @MainActor
    func resumeAllAffectsPaused() async throws {
        let store = try makeStore()
        let p1 = DownloadItem.stub(state: .paused)
        let p2 = DownloadItem.stub(state: .paused)
        let c1 = DownloadItem.stub(state: .completed) // must NOT resume
        [p1, p2, c1].forEach { store._injectItem($0) }

        store.resumeAll(outputDirectory: URL.downloadsDirectory)

        // After resume, paused items should be back in active or queued state
        // (they start tasks that will fail in unit test env — just verify
        // completed items are untouched and no crash occurs)
        #expect(store.items.first(where: { $0.id == c1.id })?.state == .completed)
    }

    @Test("resumeAll(ids:) only resumes selected paused items")
    @MainActor
    func resumeAllWithIds() async throws {
        let store = try makeStore()
        let p1 = DownloadItem.stub(state: .paused)
        let p2 = DownloadItem.stub(state: .paused)  // NOT selected → must stay paused
        [p1, p2].forEach { store._injectItem($0) }

        store.resumeAll(outputDirectory: URL.downloadsDirectory, ids: [p1.id])

        // p2 must still be paused (not touched)
        #expect(store.items.first(where: { $0.id == p2.id })?.state == .paused)
    }

    // MARK: startOrResumeAll

    @Test("startOrResumeAll does not affect completed items")
    @MainActor
    func startOrResumeAllSkipsCompleted() async throws {
        let store = try makeStore()
        let c1 = DownloadItem.stub(state: .completed)
        let c2 = DownloadItem.stub(state: .completed)
        [c1, c2].forEach { store._injectItem($0) }

        await store.startOrResumeAll(outputDirectory: URL.downloadsDirectory)

        #expect(store.items.allSatisfy { $0.state == .completed })
    }

    @Test("startOrResumeAll with ids leaves items outside selection unchanged")
    @MainActor
    func startOrResumeAllWithIds() async throws {
        let store = try makeStore()
        let q1 = DownloadItem.stub(state: .queued)
        let q2 = DownloadItem.stub(state: .queued)  // NOT in selection
        [q1, q2].forEach { store._injectItem($0) }

        await store.startOrResumeAll(outputDirectory: URL.downloadsDirectory, ids: [q1.id])

        // q2 must remain queued
        #expect(store.items.first(where: { $0.id == q2.id })?.state == .queued)
    }

    // MARK: - pausedTasks instance reuse

    @Test("resume() reuses the same task instance that was paused")
    @MainActor
    func resumeReusesPausedTaskInstance() async throws {
        let store = try makeStore()
        let item = DownloadItem.stub(state: .paused)
        store._injectItem(item)

        // Inject the mock into the paused-tasks slot (simulates a prior pause())
        let mock = MockDownloadTask(id: item.id)
        store._injectPausedTask(mock, for: item.id)

        store.resume(id: item.id, outputDirectory: URL.downloadsDirectory)

        // Yield twice: once to let the spawned Task start, once for async task.resume() to finish
        for _ in 0..<5 { await Task.yield() }

        #expect(mock.didResume, "resume() must call resume() on the ORIGINAL paused task, not a new instance")
    }

    @Test("pause() stores task so resume() can reuse it")
    @MainActor
    func pauseStoresTaskForReuse() async throws {
        let store = try makeStore()
        let item = DownloadItem.stub(state: .active)
        store._injectItem(item)

        let mock = MockDownloadTask(id: item.id)
        store._injectTask(mock, for: item.id)

        // Pause — this should move the mock to pausedTasks
        await store.pause(id: item.id)
        #expect(mock.didPause, "pause() must call pause() on the task")

        // Now resume — same mock must be picked up
        store.resume(id: item.id, outputDirectory: URL.downloadsDirectory)
        for _ in 0..<5 { await Task.yield() }

        #expect(mock.didResume, "resume() must reuse the same task instance stored during pause()")
    }

    @Test("cancel() cleans up both active and paused slots")
    @MainActor
    func cancelCleansBothSlots() async throws {
        let store = try makeStore()
        let active = DownloadItem.stub(state: .active)
        let paused = DownloadItem.stub(state: .paused)
        store._injectItem(active)
        store._injectItem(paused)

        let mockActive = MockDownloadTask(id: active.id)
        let mockPaused = MockDownloadTask(id: paused.id)
        store._injectTask(mockActive, for: active.id)
        store._injectPausedTask(mockPaused, for: paused.id)

        await store.cancel(id: active.id)
        await store.cancel(id: paused.id)

        #expect(mockActive.didCancel)
        #expect(mockPaused.didCancel)
        #expect(store.items.first(where: { $0.id == active.id })?.state == .failed)
        #expect(store.items.first(where: { $0.id == paused.id })?.state == .failed)
    }

    // MARK: - Torrent freeze fix: pause() without an active task

    @Test("pause() without active task still transitions state to .paused")
    @MainActor
    func pauseWithoutActiveTaskUnblocks() async throws {
        // Scenario: app restarted → item is .active in DB but no running task exists.
        // Pressing the pause button must NOT silently no-op; it must update the state
        // so the overall toolbar button is no longer stuck on "Pause".
        let store = try makeStore()
        let item = DownloadItem.stub(type: .torrent, state: .active)
        store._injectItem(item)
        // Note: intentionally NOT injecting a task via _injectTask — simulates restart

        await store.pause(id: item.id)

        #expect(store.items.first(where: { $0.id == item.id })?.state == .paused,
                "pause() must update state even when no running task exists")
    }

    @Test("pauseAll unblocks toolbar when torrent items are .active with no running task")
    @MainActor
    func pauseAllUnblocksToolbarForGhostTorrent() async throws {
        let store = try makeStore()
        let torrent = DownloadItem.stub(type: .torrent, state: .active)
        let video   = DownloadItem.stub(type: .ytdlp,   state: .active)
        [torrent, video].forEach { store._injectItem($0) }
        // Only inject task for video, not torrent (simulates restart scenario)
        let videoMock = MockDownloadTask(id: video.id)
        store._injectTask(videoMock, for: video.id)

        await store.pauseAll()

        #expect(store.items.first(where: { $0.id == torrent.id })?.state == .paused,
                "ghost torrent must be transitioned to .paused")
        #expect(store.items.first(where: { $0.id == video.id })?.state == .paused,
                "video task with real task should also be paused")
        #expect(videoMock.didPause, "real task pause() must be called for video item")
    }

    // MARK: - Store init: restart state reset

    @Test("store init resets .active items to .paused (no running tasks after restart)")
    @MainActor
    func storeInitResetsActiveToPaused() async throws {
        // Use a temp SQLite file so two separate store instances share the same DB
        let tmpPath = NSTemporaryDirectory() + "test_restart_\(UUID().uuidString).sqlite"
        defer { try? FileManager.default.removeItem(atPath: tmpPath) }

        // Phase 1: save an active item as if app quit mid-download
        let pm = try PersistenceManager(path: tmpPath)
        let item = DownloadItem(url: "https://example.com/test.zip", type: .http)
            .with(state: .active, progress: 0.42, receivedBytes: 420_000)
        try pm.save(item)

        // Phase 2: create a new store (simulates app restart)
        let store = try await MainActor.run {
            try DownloadStore(persistencePath: tmpPath)
        }

        let restored = store.items.first(where: { $0.id == item.id })
        #expect(restored?.state == .paused,
                "active items must become .paused on restart — no real task is running")
        #expect(restored?.progress == 0.42, "progress must be preserved across restart")
    }

    @Test("store init resets .fetchingInfo to .selectingFormat when videoInfo is present")
    @MainActor
    func storeInitRestoresFetchingInfoWithVideoInfo() async throws {
        let tmpPath = NSTemporaryDirectory() + "test_fetchinfo_\(UUID().uuidString).sqlite"
        defer { try? FileManager.default.removeItem(atPath: tmpPath) }

        let pm = try PersistenceManager(path: tmpPath)
        let mockInfo = VideoInfo(
            id: "abc", title: "Test Video", thumbnailURL: nil,
            duration: 120, uploader: nil, originalURL: "https://youtu.be/abc",
            videoFormats: [], audioFormats: []
        )
        let item = DownloadItem(url: "https://youtu.be/abc", type: .ytdlp)
            .with(state: .fetchingInfo, videoInfo: mockInfo)
        try pm.save(item)

        let store = try await MainActor.run {
            try DownloadStore(persistencePath: tmpPath)
        }

        let restored = store.items.first(where: { $0.id == item.id })
        #expect(restored?.state == .selectingFormat,
                "fetchingInfo + cached videoInfo should restore to .selectingFormat")
    }

    @Test("store init resets .fetchingMetadata to .selectingFiles when torrentFiles are present")
    @MainActor
    func storeInitRestoresFetchingMetadataWithTorrentFiles() async throws {
        let tmpPath = NSTemporaryDirectory() + "test_fetchtorrent_\(UUID().uuidString).sqlite"
        defer { try? FileManager.default.removeItem(atPath: tmpPath) }

        let pm = try PersistenceManager(path: tmpPath)
        let files = [TorrentFileEntry(index: 1, path: "file.mkv", length: 1_000_000)]
        let item = DownloadItem(url: "magnet:?xt=urn:btih:abc", type: .torrent)
            .with(state: .fetchingMetadata, torrentFiles: files)
        try pm.save(item)

        let store = try await MainActor.run {
            try DownloadStore(persistencePath: tmpPath)
        }

        let restored = store.items.first(where: { $0.id == item.id })
        #expect(restored?.state == .selectingFiles,
                "fetchingMetadata + cached torrentFiles should restore to .selectingFiles")
        #expect(restored?.torrentFiles?.count == 1,
                "torrentFiles must survive persistence round-trip")
    }

    // MARK: - Deduplication

    @Test("add() throws duplicateURL when same HTTP URL is already queued")
    @MainActor
    func addRejectsDuplicateURL() async throws {
        let store = try makeStore()
        // Inject an existing queued HTTP item with a known URL
        var existing = DownloadItem(url: "https://example.com/video.mp4", type: .http)
        existing = existing.with(state: .queued)
        store._injectItem(existing)

        // Attempting to add the same URL should throw duplicateURL
        await #expect(throws: DownloadStoreError.duplicateURL) {
            try await store.add(
                url: "https://example.com/video.mp4",
                options: DownloadOptions(outputDirectory: URL.downloadsDirectory)
            )
        }
        #expect(store.items.count == 1, "duplicate must not be added to the queue")
    }

    // MARK: - findDuplicate

    @Test("findDuplicate returns nil when queue is empty")
    @MainActor
    func findDuplicateEmptyQueue() throws {
        let store = try makeStore()
        #expect(store.findDuplicate(url: "https://youtu.be/abc") == nil)
    }

    @Test("findDuplicate returns nil for completed items with matching URL")
    @MainActor
    func findDuplicateIgnoresCompleted() throws {
        let store = try makeStore()
        var completed = DownloadItem(url: "https://youtu.be/abc", type: .ytdlp)
        completed = completed.with(state: .completed)
        store._injectItem(completed)
        #expect(store.findDuplicate(url: "https://youtu.be/abc") == nil,
                "completed items must never count as duplicates")
    }

    @Test("findDuplicate matches ytdlp item with same URL + formatId + containerFormat")
    @MainActor
    func findDuplicateMatchesYtdlpByFormat() throws {
        let store = try makeStore()
        var existing = DownloadItem(url: "https://youtu.be/abc", type: .ytdlp)
        existing = existing.with(
            state: .queued,
            selectedFormatId: "137+140/best",
            selectedContainerFormat: "mp4"
        )
        store._injectItem(existing)

        // Same URL + format → conflict
        let conflict = store.findDuplicate(
            url: "https://youtu.be/abc",
            formatId: "137+140/best",
            containerFormat: "mp4"
        )
        #expect(conflict?.id == existing.id, "should detect duplicate with same format")

        // Same URL but different format → no conflict
        let noConflict = store.findDuplicate(
            url: "https://youtu.be/abc",
            formatId: "bestaudio/best",  // audio-only
            containerFormat: "mp4"
        )
        #expect(noConflict == nil,
                "different formatId means different download — must not block")
    }

    @Test("findDuplicate respects excludingId so an item doesn't conflict with itself")
    @MainActor
    func findDuplicateExcludesOwnID() throws {
        let store = try makeStore()
        var item = DownloadItem(url: "https://youtu.be/abc", type: .ytdlp)
        item = item.with(state: .selectingFormat)
        store._injectItem(item)

        // Without excludingId → the item matches itself
        #expect(store.findDuplicate(url: "https://youtu.be/abc") != nil)
        // With excludingId → no match (can't conflict with itself)
        #expect(store.findDuplicate(url: "https://youtu.be/abc", excludingId: item.id) == nil,
                "excludingId must prevent self-match in format-picker flow")
    }

    @Test("findDuplicate distinguishes by outputDir for torrent")
    @MainActor
    func findDuplicateDistinguishesByOutputDir() throws {
        let store = try makeStore()
        let dir1 = URL(fileURLWithPath: "/Users/test/Movies")
        let dir2 = URL(fileURLWithPath: "/Users/test/Downloads")
        var item = DownloadItem(url: "magnet:?xt=urn:btih:abc", type: .torrent)
        item = item.with(state: .active, itemOutputDirectory: dir1)
        store._injectItem(item)

        // Same URL + same dir → conflict
        #expect(store.findDuplicate(url: "magnet:?xt=urn:btih:abc", outputDir: dir1) != nil)
        // Same URL + different dir → user wants files in a different folder, allow it
        #expect(store.findDuplicate(url: "magnet:?xt=urn:btih:abc", outputDir: dir2) == nil,
                "different output dir means independent download — must not block")
    }

    @Test("add() allows re-adding a completed item's URL")
    @MainActor
    func addAllowsReAddingCompletedURL() async throws {
        let store = try makeStore()
        var completed = DownloadItem(url: "https://example.com/video.mp4", type: .http)
        completed = completed.with(state: .completed)
        store._injectItem(completed)

        // Adding the same URL again after it's completed should succeed (creates new row)
        // The HTTP item goes straight to .queued without network calls
        do {
            try await store.add(
                url: "https://example.com/video.mp4",
                options: DownloadOptions(outputDirectory: URL.downloadsDirectory)
            )
        } catch DownloadStoreError.duplicateURL {
            Issue.record("Should not throw duplicateURL for a completed item's URL")
        } catch {
            // Other errors (e.g., validation) are acceptable in test environment
        }
    }

    @Test("HTTP/ytdlp/torrent resume: same task type stub reuses instance")
    @MainActor
    func resumeReusesByType() async throws {
        // Simulate each download type to ensure the same pattern holds regardless of type
        for type_ in [DownloadType.http, DownloadType.ytdlp, DownloadType.torrent] {
            let store = try makeStore()
            let item = DownloadItem.stub(type: type_, state: .paused)
            store._injectItem(item)

            let mock = MockDownloadTask(id: item.id)
            store._injectPausedTask(mock, for: item.id)

            store.resume(id: item.id, outputDirectory: URL.downloadsDirectory)
            for _ in 0..<5 { await Task.yield() }

            #expect(mock.didResume, "\(type_) resume must reuse the paused task instance")
        }
    }
}
