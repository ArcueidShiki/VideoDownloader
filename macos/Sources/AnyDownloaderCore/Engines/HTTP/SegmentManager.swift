import Foundation

public struct Segment: Sendable {
    public let index: Int
    public let range: Range<Int64>
    public var receivedBytes: Int64 = 0
    public var isComplete: Bool = false

    public init(index: Int, range: Range<Int64>) {
        self.index = index
        self.range = range
    }
}

public struct SegmentManager: Sendable {
    public let totalBytes: Int64
    public let threadCount: Int
    public private(set) var segments: [Segment]

    public init(totalBytes: Int64, threadCount: Int) {
        self.totalBytes = totalBytes
        self.threadCount = threadCount
        let size = totalBytes / Int64(threadCount)
        self.segments = (0..<threadCount).map { i in
            let start = Int64(i) * size
            let end = i == threadCount - 1 ? totalBytes : start + size
            return Segment(index: i, range: start..<end)
        }
    }

    public var totalReceivedBytes: Int64 {
        segments.reduce(0) { $0 + $1.receivedBytes }
    }

    public var progress: Double {
        guard totalBytes > 0 else { return 0 }
        return Double(totalReceivedBytes) / Double(totalBytes)
    }

    public var isComplete: Bool {
        segments.allSatisfy(\.isComplete)
    }

    public mutating func markReceived(segmentIndex: Int, bytes: Int64) {
        segments[segmentIndex].receivedBytes += bytes
    }

    public mutating func markComplete(segmentIndex: Int) {
        segments[segmentIndex].isComplete = true
    }
}
