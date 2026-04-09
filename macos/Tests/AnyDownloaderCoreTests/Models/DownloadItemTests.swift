import Testing
@testable import AnyDownloaderCore

@Suite("DownloadItem")
struct DownloadItemTests {
    @Test("creates with unique IDs")
    func uniqueIDs() {
        let a = DownloadItem(url: "https://example.com/a.mp4", type: .http)
        let b = DownloadItem(url: "https://example.com/b.mp4", type: .http)
        #expect(a.id != b.id)
    }

    @Test("default state is pending")
    func defaultStatePending() {
        let item = DownloadItem(url: "https://example.com/a.mp4", type: .http)
        #expect(item.state == .pending)
        #expect(item.progress == 0.0)
    }

    @Test("progress clamped 0...1")
    func progressClamped() {
        var item = DownloadItem(url: "https://example.com/a.mp4", type: .http)
        item.progress = 1.5
        #expect(item.progress <= 1.0)
        item.progress = -0.5
        #expect(item.progress >= 0.0)
    }

    @Test("immutable copy on state change")
    func immutableUpdate() {
        let original = DownloadItem(url: "https://example.com/a.mp4", type: .http)
        let updated = original.with(state: .active, progress: 0.5)
        #expect(original.state == .pending)
        #expect(updated.state == .active)
        #expect(updated.progress == 0.5)
        #expect(original.id == updated.id)
    }
}
