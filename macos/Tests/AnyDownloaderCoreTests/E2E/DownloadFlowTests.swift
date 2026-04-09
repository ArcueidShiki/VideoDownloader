import Testing
import Foundation
@testable import AnyDownloaderCore

@Suite("E2E Download Flows")
struct DownloadFlowTests {

    init() {
        FailFastURLProtocol.register()
    }

    @Test("E2E: user adds HTTP URL → item appears in list")
    @MainActor
    func e2eHTTPAddAndStart() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        try await store.add(url: "https://example.com/file.zip", options: .default)
        #expect(store.items.count == 1)
        let item = store.items[0]
        #expect(item.url == "https://example.com/file.zip")
        #expect(item.type == .http)
    }

    @Test("E2E: user pauses active download → state transitions to paused")
    @MainActor
    func e2eHTTPPause() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        // pause() only affects active items (items with a running task).
        // Inject an active HTTP item and a mock task to simulate a live download.
        let item = DownloadItem.stub(type: .http, state: .active)
        store._injectItem(item)
        let mock = MockDownloadTask(id: item.id)
        store._injectTask(mock, for: item.id)

        await store.pause(id: item.id)

        #expect(mock.didPause)
        #expect(store.items.first(where: { $0.id == item.id })?.state == .paused)
    }

    @Test("E2E: user cancels download → state becomes failed")
    @MainActor
    func e2eHTTPCancel() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        try await store.add(url: "https://example.com/file.zip", options: .default)
        let id = store.items[0].id
        await store.cancel(id: id)
        #expect(store.items[0].state == .failed)
    }

    @Test("E2E: YouTube URL classified as ytdlp")
    @MainActor
    func e2eYtDlpClassification() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        try await store.add(url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ", options: .default)
        #expect(store.items[0].type == .ytdlp)
    }

    @Test("E2E: magnet link classified as torrent")
    @MainActor
    func e2eMagnetClassification() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        try await store.add(url: "magnet:?xt=urn:btih:da39a3ee5e6b4b0d3255bfef95601890afd80709&dn=test", options: .default)
        #expect(store.items[0].type == .torrent)
    }

    @Test("E2E: SSRF attack rejected, list stays empty")
    @MainActor
    func e2eSSRFRejection() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        do {
            try await store.add(url: "https://192.168.1.1/malicious", options: .default)
            Issue.record("Should have thrown")
        } catch {
            #expect(store.items.isEmpty)
        }
    }

    @Test("E2E: max concurrent limit respected")
    @MainActor
    func e2eMaxConcurrent() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 2)
        for i in 1...5 {
            try await store.add(url: "https://example.com/file\(i).zip", options: .default)
        }
        #expect(store.items.count == 5)
        let active = store.filtered(by: .active)
        #expect(active.count <= 2)
    }

    @Test("E2E: items persist across store restarts")
    @MainActor
    func e2ePersistenceAcrossRestart() async throws {
        let tmpPath = FileManager.default.temporaryDirectory
            .appendingPathComponent("test-\(UUID()).sqlite").path
        defer { try? FileManager.default.removeItem(atPath: tmpPath) }

        let store1 = try DownloadStore(persistencePath: tmpPath, maxConcurrent: 5)
        try await store1.add(url: "https://example.com/file.zip", options: .default)
        let id = store1.items[0].id

        let store2 = try DownloadStore(persistencePath: tmpPath, maxConcurrent: 5)
        #expect(store2.items.contains { $0.id == id })
    }

    // --- YtDlp Flow (2nd E2E) ---

    @Test("E2E: YtDlp download can be paused then resumed")
    @MainActor
    func e2eYtDlpPauseResume() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        // Inject an active ytdlp item and a mock task to simulate a live download.
        // (pause() requires an active task; using mock avoids real yt-dlp/network calls.)
        let item = DownloadItem.stub(type: .ytdlp, state: .active)
        store._injectItem(item)
        let mock = MockDownloadTask(id: item.id)
        store._injectTask(mock, for: item.id)

        // Pause
        await store.pause(id: item.id)
        #expect(mock.didPause)
        #expect(store.items.first(where: { $0.id == item.id })?.state == .paused)

        // Resume — reuses the same mock task stored during pause
        store.resume(id: item.id, outputDirectory: URL.downloadsDirectory)
        for _ in 0..<5 { await Task.yield() }

        #expect(mock.didResume)
    }

    // --- Torrent Flow (2nd E2E) ---

    @Test("E2E: Torrent download can be cancelled → state is failed")
    @MainActor
    func e2eTorrentCancel() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        try await store.add(url: "magnet:?xt=urn:btih:da39a3ee5e6b4b0d3255bfef95601890afd80709&dn=test", options: .default)
        #expect(store.items[0].type == .torrent)
        let id = store.items[0].id
        await store.cancel(id: id)
        #expect(store.items[0].state == .failed)
    }
}
