import Foundation

public struct TorrentFileEntry: Identifiable, Sendable, Codable, Equatable {
    /// 1-based index matching aria2c's `--select-file` convention.
    public let index: Int
    public let path: String
    public let length: Int64
    public var isSelected: Bool

    public var id: Int { index }

    public init(index: Int, path: String, length: Int64, isSelected: Bool = true) {
        self.index = index
        self.path = path
        self.length = length
        self.isSelected = isSelected
    }
}
