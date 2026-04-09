import Foundation

public struct DownloadItem: Identifiable, Sendable, Codable {
    public let id: UUID
    public let url: String
    public let type: DownloadType
    public var state: DownloadState
    public var progress: Double {
        didSet { progress = max(0, min(1, progress)) }
    }
    public var speedBytesPerSec: Int64
    public var etaSeconds: Int?
    public var filePath: String?
    public var errorMessage: String?
    public var totalBytes: Int64?
    public var receivedBytes: Int64
    public var title: String?
    public var torrentFiles: [TorrentFileEntry]?
    public var videoInfo: VideoInfo?
    public var selectedFormatId: String?        // yt-dlp format id chosen by user
    public var selectedContainerFormat: String? // nil/"mp4" = merge to mp4, "native" = no forced merge
    public var itemOutputDirectory: URL?        // per-task override; nil = use global
    public var customFilename: String?          // per-task filename override for yt-dlp output
    public let createdAt: Date

    public init(url: String, type: DownloadType) {
        self.id = UUID()
        self.url = url
        self.type = type
        self.state = .pending
        self.progress = 0
        self.speedBytesPerSec = 0
        self.receivedBytes = 0
        self.createdAt = Date()
    }

    /// Internal initializer for reconstructing items from persistent storage.
    init(id: UUID, url: String, type: DownloadType, createdAt: Date) {
        self.id = id
        self.url = url
        self.type = type
        self.state = .pending
        self.progress = 0
        self.speedBytesPerSec = 0
        self.receivedBytes = 0
        self.createdAt = createdAt
    }

    /// Returns a fresh copy of this item with all download state cleared, preserving
    /// the same UUID, URL, type, createdAt, output directory, filename override,
    /// and any previously fetched video info / format selection so the user isn't
    /// forced back through the format-picker on a restart.
    func resetForRestart() -> DownloadItem {
        var fresh = DownloadItem(id: id, url: url, type: type, createdAt: createdAt)
        fresh.itemOutputDirectory = itemOutputDirectory
        fresh.customFilename = customFilename
        fresh.title = title
        fresh.videoInfo = videoInfo
        fresh.selectedFormatId = selectedFormatId
        fresh.selectedContainerFormat = selectedContainerFormat
        fresh.torrentFiles = torrentFiles
        return fresh
    }

    public func with(
        state: DownloadState? = nil,
        progress: Double? = nil,
        speedBytesPerSec: Int64? = nil,
        etaSeconds: Int? = nil,
        filePath: String? = nil,
        errorMessage: String? = nil,
        totalBytes: Int64? = nil,
        receivedBytes: Int64? = nil,
        title: String? = nil,
        torrentFiles: [TorrentFileEntry]? = nil,
        videoInfo: VideoInfo? = nil,
        selectedFormatId: String? = nil,
        selectedContainerFormat: String? = nil,
        itemOutputDirectory: URL? = nil,
        customFilename: String? = nil
    ) -> DownloadItem {
        var copy = self
        if let s = state { copy.state = s }
        if let p = progress { copy.progress = max(0, min(1, p)) }
        if let sp = speedBytesPerSec { copy.speedBytesPerSec = sp }
        copy.etaSeconds = etaSeconds ?? self.etaSeconds
        copy.filePath = filePath ?? self.filePath
        copy.errorMessage = errorMessage ?? self.errorMessage
        copy.totalBytes = totalBytes ?? self.totalBytes
        if let r = receivedBytes { copy.receivedBytes = r }
        copy.title = title ?? self.title
        copy.torrentFiles = torrentFiles ?? self.torrentFiles
        copy.videoInfo = videoInfo ?? self.videoInfo
        copy.selectedFormatId = selectedFormatId ?? self.selectedFormatId
        copy.selectedContainerFormat = selectedContainerFormat ?? self.selectedContainerFormat
        copy.itemOutputDirectory = itemOutputDirectory ?? self.itemOutputDirectory
        copy.customFilename = customFilename ?? self.customFilename
        return copy
    }
}
