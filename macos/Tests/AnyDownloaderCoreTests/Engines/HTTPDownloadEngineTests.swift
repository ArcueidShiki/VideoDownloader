// macos/Tests/AnyDownloaderCoreTests/Engines/HTTPDownloadEngineTests.swift
import Testing
import Foundation
@testable import AnyDownloaderCore

@Suite("HTTPDownloadEngine")
struct HTTPDownloadEngineTests {
    @Test("engine creates task with correct itemID")
    func createsTask() {
        let engine = HTTPDownloadEngine()
        let item = DownloadItem(url: "https://example.com/file.zip", type: .http)
        let task = engine.createTask(for: item, options: .default) { _ in }
        #expect(task.itemID == item.id)
    }

    @Test("parseContentLength returns value from headers")
    func headRequestPopulatesBytes() {
        let engine = HTTPDownloadEngine()
        let result = engine.parseContentLength(from: ["Content-Length": "1000000", "Accept-Ranges": "bytes"])
        #expect(result == 1_000_000)
    }

    @Test("builds correct Range header for segment")
    func rangeHeader() {
        let engine = HTTPDownloadEngine()
        let segment = Segment(index: 0, range: 0..<250)
        let header = engine.rangeHeader(for: segment)
        #expect(header == "bytes=0-249")
    }

    @Test("returns false when no Accept-Ranges header")
    func noAcceptRanges() {
        let engine = HTTPDownloadEngine()
        #expect(!engine.supportsRangeRequests(headers: [:]))
    }

    @Test("returns true when Accept-Ranges: bytes")
    func withAcceptRanges() {
        let engine = HTTPDownloadEngine()
        #expect(engine.supportsRangeRequests(headers: ["Accept-Ranges": "bytes"]))
    }
}
