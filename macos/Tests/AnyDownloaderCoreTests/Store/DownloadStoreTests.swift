import Testing
import Foundation
@testable import AnyDownloaderCore

@Suite("DownloadStore")
struct DownloadStoreTests {
    @MainActor
    func makeStore() throws -> DownloadStore {
        try DownloadStore(persistencePath: ":memory:", maxConcurrent: 3)
    }

    @Test("adding item appears in items array")
    @MainActor
    func addItem() async throws {
        let store = try makeStore()
        try await store.add(url: "https://example.com/a.zip", options: .default)
        #expect(store.items.count == 1)
        #expect(store.items[0].url == "https://example.com/a.zip")
    }

    @Test("rejects invalid URL")
    @MainActor
    func rejectsInvalidURL() async throws {
        let store = try makeStore()
        do {
            try await store.add(url: "javascript:alert(1)", options: .default)
            Issue.record("Should have thrown")
        } catch {
            #expect(store.items.isEmpty)
        }
    }

    @Test("filter returns correct items by state")
    @MainActor
    func filterItems() async throws {
        let store = try makeStore()
        try await store.add(url: "https://example.com/a.zip", options: .default)
        let paused = store.filtered(by: .paused)
        #expect(paused.isEmpty)
    }

    @Test("respects max concurrent limit")
    @MainActor
    func maxConcurrent() throws {
        let store = try makeStore()
        #expect(store.maxConcurrentTasks == 3)
    }

    @Test("activeCount reflects active items")
    @MainActor
    func activeCount() throws {
        let store = try makeStore()
        #expect(store.activeCount == 0)
    }
}
