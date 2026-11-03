import Testing
import Foundation
@testable import AnyDownloaderCore

@Suite("PersistenceManager")
struct PersistenceManagerTests {
    func makeInMemoryManager() throws -> PersistenceManager {
        try PersistenceManager(path: ":memory:")
    }

    @Test("saves and loads DownloadItem")
    func saveAndLoad() throws {
        let mgr = try makeInMemoryManager()
        let item = DownloadItem(url: "https://example.com/a.zip", type: .http)
        try mgr.save(item)
        let loaded = try mgr.loadAll()
        #expect(loaded.count == 1)
        #expect(loaded[0].id == item.id)
        #expect(loaded[0].url == item.url)
    }

    @Test("updates existing item")
    func updateItem() throws {
        let mgr = try makeInMemoryManager()
        let item = DownloadItem(url: "https://example.com/a.zip", type: .http)
        try mgr.save(item)
        let updated = item.with(state: .completed, progress: 1.0)
        try mgr.save(updated)
        let loaded = try mgr.loadAll()
        #expect(loaded.count == 1)
        #expect(loaded[0].state == .completed)
    }

    @Test("deletes item by id")
    func deleteItem() throws {
        let mgr = try makeInMemoryManager()
        let item = DownloadItem(url: "https://example.com/a.zip", type: .http)
        try mgr.save(item)
        try mgr.delete(id: item.id)
        #expect(try mgr.loadAll().isEmpty)
    }

    @Test("loadUnfinished skips completed items")
    func skipCompleted() throws {
        let mgr = try makeInMemoryManager()
        let active = DownloadItem(url: "https://example.com/a.zip", type: .http)
        let completed = DownloadItem(url: "https://example.com/b.zip", type: .http).with(state: .completed)
        try mgr.save(active)
        try mgr.save(completed)
        let unfinished = try mgr.loadUnfinished()
        #expect(unfinished.count == 1)
        #expect(unfinished[0].id == active.id)
    }

    // MARK: - v2 rich metadata persistence

    @Test("saves and restores torrentFiles across persistence round-trip")
    func torrentFilesPersisted() throws {
        let mgr = try makeInMemoryManager()
        let files: [TorrentFileEntry] = [
            TorrentFileEntry(index: 1, path: "movie/video.mkv", length: 1_000_000_000),
            TorrentFileEntry(index: 2, path: "movie/subs.srt",  length: 50_000, isSelected: false),
        ]
        let item = DownloadItem(url: "magnet:?xt=urn:btih:abc", type: .torrent)
            .with(state: .selectingFiles, torrentFiles: files)
        try mgr.save(item)
        let loaded = try mgr.loadAll()
        #expect(loaded.count == 1)
        let restoredFiles = loaded[0].torrentFiles
        #expect(restoredFiles?.count == 2)
        #expect(restoredFiles?[0].path == "movie/video.mkv")
        #expect(restoredFiles?[1].isSelected == false)
    }

    @Test("saves and restores selectedFormatId and containerFormat")
    func formatSelectionPersisted() throws {
        let mgr = try makeInMemoryManager()
        let item = DownloadItem(url: "https://youtu.be/abc", type: .ytdlp)
            .with(state: .queued, selectedFormatId: "137+140", selectedContainerFormat: "mp4")
        try mgr.save(item)
        let loaded = try mgr.loadAll()
        #expect(loaded[0].selectedFormatId == "137+140")
        #expect(loaded[0].selectedContainerFormat == "mp4")
    }

    @Test("saves and restores itemOutputDirectory and customFilename")
    func outputMetaPersisted() throws {
        let mgr = try makeInMemoryManager()
        let dir = URL(fileURLWithPath: "/Users/test/Movies")
        let item = DownloadItem(url: "https://example.com/v.mp4", type: .http)
            .with(state: .queued, itemOutputDirectory: dir, customFilename: "my_video")
        try mgr.save(item)
        let loaded = try mgr.loadAll()
        #expect(loaded[0].itemOutputDirectory?.path == dir.path)
        #expect(loaded[0].customFilename == "my_video")
    }

    @Test("loadAll returns both completed and in-progress items")
    func loadAllIncludesCompleted() throws {
        let mgr = try makeInMemoryManager()
        let active    = DownloadItem(url: "https://example.com/a.zip", type: .http)
        let completed = DownloadItem(url: "https://example.com/b.zip", type: .http).with(state: .completed)
        let failed    = DownloadItem(url: "https://example.com/c.zip", type: .http).with(state: .failed)
        try mgr.save(active); try mgr.save(completed); try mgr.save(failed)
        let all = try mgr.loadAll()
        #expect(all.count == 3)
    }
}
