import Testing
import Foundation
@testable import AnyDownloaderCore

@Suite("BencodeParser")
struct BencodeParserTests {

    @Test("parses integer")
    func parsesInteger() throws {
        var p = BencodeParser("i42e".data(using: .utf8)!)
        guard case .integer(let n) = try p.parse() else {
            Issue.record("Expected integer"); return
        }
        #expect(n == 42)
    }

    @Test("parses negative integer")
    func parsesNegativeInteger() throws {
        var p = BencodeParser("i-7e".data(using: .utf8)!)
        guard case .integer(let n) = try p.parse() else {
            Issue.record("Expected integer"); return
        }
        #expect(n == -7)
    }

    @Test("parses byte string")
    func parsesByteString() throws {
        var p = BencodeParser("4:spam".data(using: .utf8)!)
        guard case .bytes(let d) = try p.parse() else {
            Issue.record("Expected bytes"); return
        }
        #expect(String(data: d, encoding: .utf8) == "spam")
    }

    @Test("parses list")
    func parsesList() throws {
        var p = BencodeParser("l4:spami42ee".data(using: .utf8)!)
        guard case .list(let items) = try p.parse() else {
            Issue.record("Expected list"); return
        }
        #expect(items.count == 2)
        #expect(items[0].string == "spam")
        #expect(items[1].integer == 42)
    }

    @Test("parses dict")
    func parsesDict() throws {
        var p = BencodeParser("d3:bar4:spam3:fooi42ee".data(using: .utf8)!)
        let v = try p.parse()
        #expect(v["bar"]?.string == "spam")
        #expect(v["foo"]?.integer == 42)
    }

    @Test("parses multi-file torrent info")
    func parsesMultiFileTorrent() throws {
        // Minimal multi-file torrent bencode. "01.mp3" / "02.mp3" = 6 chars → prefix 6:
        let torrent = "d4:infod4:name8:my-album5:filesld4:pathl4:disc6:01.mp3e6:lengthi3000000eed4:pathl4:disc6:02.mp3e6:lengthi2500000eeeee"
        let data = torrent.data(using: .utf8)!
        let metadata = try TorrentMetadata.parse(data: data)
        #expect(metadata.name == "my-album")
        #expect(metadata.files.count == 2)
        #expect(metadata.files[0].path == "disc/01.mp3")
        #expect(metadata.files[0].length == 3_000_000)
        #expect(metadata.files[1].path == "disc/02.mp3")
        #expect(metadata.files[1].length == 2_500_000)
        // Indices are 1-based (aria2c convention)
        #expect(metadata.files[0].index == 1)
        #expect(metadata.files[1].index == 2)
    }

    @Test("parses single-file torrent info")
    func parsesSingleFileTorrent() throws {
        // "movie.mp4" = 9 chars → prefix 9:
        let torrent = "d4:infod4:name9:movie.mp46:lengthi1073741824eee"
        let data = torrent.data(using: .utf8)!
        let metadata = try TorrentMetadata.parse(data: data)
        #expect(metadata.name == "movie.mp4")
        #expect(metadata.files.count == 1)
        #expect(metadata.files[0].path == "movie.mp4")
        #expect(metadata.files[0].length == 1_073_741_824)
    }
}

@Suite("E2E: Torrent file selection flow")
struct TorrentSelectionFlowTests {

    @Test("E2E: adding magnet does NOT start as active (goes through metadata fetch path)")
    @MainActor
    func e2eMagnetNotImmediatelyActive() async throws {
        let store = try DownloadStore(persistencePath: ":memory:", maxConcurrent: 5)
        try await store.add(url: "magnet:?xt=urn:btih:da39a3ee5e6b4b0d3255bfef95601890afd80709&dn=test", options: .default)
        // Torrents must never go straight to .active — they need file selection first.
        let item = store.items[0]
        #expect(item.type == .torrent)
        #expect(item.state != .active)
    }

    @Test("E2E: TorrentFileEntry isSelected defaults true")
    func torrentFileEntryDefaults() {
        let entry = TorrentFileEntry(index: 1, path: "file.mp4", length: 1024)
        #expect(entry.isSelected == true)
        #expect(entry.id == 1)
    }

    @Test("E2E: DownloadOptions carries selectedFileIndices")
    func downloadOptionsFileIndices() {
        let opts = DownloadOptions(selectedFileIndices: [1, 3, 5])
        #expect(opts.selectedFileIndices == [1, 3, 5])
    }

    @Test("E2E: TorrentEngine buildArguments includes --select-file")
    func buildArgumentsWithSelection() {
        let opts = DownloadOptions(selectedFileIndices: [2, 4])
        let args = TorrentEngine.buildArguments(url: "magnet:?xt=urn:btih:test", options: opts)
        #expect(args.contains("--select-file=2,4"))
    }

    @Test("E2E: TorrentEngine buildArguments omits --select-file when nil")
    func buildArgumentsNoSelection() {
        let opts = DownloadOptions()
        let args = TorrentEngine.buildArguments(url: "magnet:?xt=urn:btih:test", options: opts)
        #expect(!args.contains { $0.hasPrefix("--select-file") })
    }

    @Test("E2E: DownloadItem carries torrentFiles via with()")
    func downloadItemWithTorrentFiles() {
        let item = DownloadItem(url: "magnet:?xt=test", type: .torrent)
        let files = [TorrentFileEntry(index: 1, path: "a.mp4", length: 500)]
        let updated = item.with(state: .selectingFiles, torrentFiles: files)
        #expect(updated.state == .selectingFiles)
        #expect(updated.torrentFiles?.count == 1)
        #expect(updated.torrentFiles?[0].path == "a.mp4")
    }
}
