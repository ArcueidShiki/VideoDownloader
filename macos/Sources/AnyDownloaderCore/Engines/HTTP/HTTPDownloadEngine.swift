import Foundation

public struct HTTPDownloadEngine: DownloadEngineProtocol {
    public init() {}

    public func createTask(
        for item: DownloadItem,
        options: DownloadOptions,
        onUpdate: @Sendable @escaping (DownloadItem) -> Void
    ) -> any DownloadTaskProtocol {
        HTTPDownloadTask(item: item, options: options, onUpdate: onUpdate)
    }

    public func parseContentLength(from headers: [String: String]) -> Int64? {
        headers["Content-Length"].flatMap { Int64($0) }
    }

    public func rangeHeader(for segment: Segment) -> String {
        "bytes=\(segment.range.lowerBound)-\(segment.range.upperBound - 1)"
    }

    public func supportsRangeRequests(headers: [String: String]) -> Bool {
        headers["Accept-Ranges"] == "bytes"
    }
}
