import Foundation
import Testing
@testable import AnyDownloaderCore

@Suite("Store recovery regressions")
struct RecoveryRegressionTests {
    final class FailingTask: DownloadTaskProtocol, Sendable {
        let itemID: UUID
        init(id: UUID) { itemID = id }
        func start() async throws { throw URLError(.cannotOpenFile) }
        func resume() async throws { throw URLError(.cannotOpenFile) }
        func pause() async {}
        func cancel() async {}
    }

    @Test("resume launch error becomes a persisted failure")
    @MainActor
    func resumeError() async throws {
        let store = try DownloadStore(persistencePath: ":memory:")
        let item = DownloadItem.stub(state: .paused)
        store._injectItem(item)
        store._injectPausedTask(FailingTask(id: item.id), for: item.id)
        store.resume(id: item.id, outputDirectory: .temporaryDirectory)
        let deadline = Date().addingTimeInterval(3)
        while store.items[0].state == .active && Date() < deadline {
            try await Task.sleep(for: .milliseconds(10))
        }
        #expect(store.items[0].state == .failed)
        #expect(store.items[0].errorMessage != nil)
    }

    @Test("cancel invalidates a resume before its worker starts")
    @MainActor
    func cancelBeforeLaunch() async throws {
        let store = try DownloadStore(persistencePath: ":memory:")
        let item = DownloadItem.stub(state: .paused)
        let task = MockDownloadTask(id: item.id)
        store._injectItem(item)
        store._injectPausedTask(task, for: item.id)
        store.resume(id: item.id, outputDirectory: .temporaryDirectory)
        await store.cancel(id: item.id)
        try await Task.sleep(for: .milliseconds(50))
        #expect(!task.didResume)
        #expect(store.items[0].state == .failed)
        #expect(store.items[0].errorMessage == "Cancelled")
    }
}
