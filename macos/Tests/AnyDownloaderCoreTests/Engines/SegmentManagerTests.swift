// macos/Tests/AnyDownloaderCoreTests/Engines/SegmentManagerTests.swift
import Testing
@testable import AnyDownloaderCore

@Suite("SegmentManager")
struct SegmentManagerTests {
    @Test("splits file into N equal segments")
    func splitEqually() {
        let mgr = SegmentManager(totalBytes: 1000, threadCount: 4)
        let segments = mgr.segments
        #expect(segments.count == 4)
        #expect(segments[0].range == 0..<250)
        #expect(segments[1].range == 250..<500)
        #expect(segments[3].range == 750..<1000)
    }

    @Test("handles last segment with remainder")
    func remainderSegment() {
        let mgr = SegmentManager(totalBytes: 1001, threadCount: 4)
        #expect(mgr.segments[3].range == 750..<1001)
    }

    @Test("tracks received bytes across segments")
    func trackProgress() {
        var mgr = SegmentManager(totalBytes: 1000, threadCount: 4)
        mgr.markReceived(segmentIndex: 0, bytes: 100)
        mgr.markReceived(segmentIndex: 1, bytes: 50)
        #expect(mgr.totalReceivedBytes == 150)
        #expect(mgr.progress == 0.15)
    }

    @Test("all segments complete means done")
    func allComplete() {
        var mgr = SegmentManager(totalBytes: 100, threadCount: 2)
        mgr.markComplete(segmentIndex: 0)
        #expect(!mgr.isComplete)
        mgr.markComplete(segmentIndex: 1)
        #expect(mgr.isComplete)
    }
}
