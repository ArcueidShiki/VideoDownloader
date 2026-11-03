import Foundation
import Testing
@testable import AnyDownloaderCore

@Suite("Reviewed recovery boundaries")
struct ReviewRecoveryTests {
    actor Gate {
        var continuation: CheckedContinuation<Void, any Error>?
        var calls = 0
        func run() async throws {
            calls += 1
            if calls == 1 { try await withCheckedThrowingContinuation { continuation = $0 } }
        }
        func fail() { continuation?.resume(throwing: URLError(.cannotConnectToHost)); continuation = nil }
        var ready: Bool { continuation != nil }
    }
    final class DelayedTask: DownloadTaskProtocol, Sendable {
        let itemID: UUID
        let gate: Gate
        init(id: UUID, gate: Gate) { itemID = id; self.gate = gate }
        func start() async throws { try await gate.run() }
        func resume() async throws { try await gate.run() }
        func pause() async {}
        func cancel() async {}
    }

    @Test("late errors cannot overwrite pause or the next resume", arguments: [false, true])
    @MainActor
    func lateError(resumeAgain: Bool) async throws {
        let store = try DownloadStore(persistencePath: ":memory:")
        let item = DownloadItem.stub(state: .paused)
        let gate = Gate()
        store._injectItem(item)
        store._injectPausedTask(DelayedTask(id: item.id, gate: gate), for: item.id)
        store.resume(id: item.id, outputDirectory: .temporaryDirectory)
        let deadline = Date().addingTimeInterval(2)
        while !(await gate.ready) && Date() < deadline { await Task.yield() }
        try #require(await gate.ready)
        await store.pause(id: item.id)
        #expect(store.items[0].state == .paused)
        if resumeAgain { store.resume(id: item.id, outputDirectory: .temporaryDirectory) }
        await gate.fail()
        for _ in 0..<100 { await Task.yield() }
        #expect(store.items[0].state == (resumeAgain ? .active : .paused))
        #expect(store.items[0].errorMessage == nil)
    }

    @Test("metadata error, recovery and cached selection survive reopening")
    @MainActor
    func metadataPersistence() async throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }
        let database = root.appendingPathComponent("state.sqlite").path
        let store = try DownloadStore(persistencePath: database)
        store.videoInfoFetcher = { _ in throw URLError(.cannotConnectToHost) }
        try await store.add(url: "https://www.youtube.com/watch?v=fixture", options: .default)
        let id = try #require(store.items.first?.id)
        let deadline = Date().addingTimeInterval(2)
        while store.items[0].state != .failed && Date() < deadline { await Task.yield() }
        #expect(store.items[0].state == .failed)
        let reopened = try DownloadStore(persistencePath: database)
        #expect(reopened.items[0].state == .failed)
        #expect(reopened.items[0].errorMessage == store.items[0].errorMessage)
        store.videoInfoFetcher = { _ in try VideoInfo.parse(from: ["id": "fixture", "title": "Recovered metadata"]) }
        await store.restart(id: id, outputDirectory: root)
        let retryDeadline = Date().addingTimeInterval(2)
        while store.items[0].state != .selectingFormat && Date() < retryDeadline { await Task.yield() }
        #expect(store.items[0].state == .selectingFormat)
        #expect(try DownloadStore(persistencePath: database).items[0].videoInfo?.title == "Recovered metadata")
        store.videoInfoFetcher = { _ in throw URLError(.cannotConnectToHost) }
        try await store.add(url: "https://www.youtube.com/watch?v=fixture", options: .default)
        let cacheDeadline = Date().addingTimeInterval(2)
        while store.items.last?.state != .selectingFormat && Date() < cacheDeadline { await Task.yield() }
        let finalStore = try DownloadStore(persistencePath: database)
        #expect(finalStore.items.count == 2)
        #expect(finalStore.items.allSatisfy { $0.state == .selectingFormat && $0.videoInfo?.title == "Recovered metadata" })
    }
}
