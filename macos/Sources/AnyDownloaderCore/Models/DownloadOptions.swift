import Foundation

public struct DownloadOptions: Sendable {
    public var outputDirectory: URL
    public var threadCount: Int
    public var speedLimitBytesPerSec: Int64
    public var quality: String
    public var format: String
    public var downloadSubtitles: Bool
    /// aria2c 1-based file indices to download. `nil` means download all.
    public var selectedFileIndices: [Int]?
    /// Override output filename (without directory path). `nil` = yt-dlp %(title)s.%(ext)s.
    public var customFilename: String?

    public init(
        outputDirectory: URL = URL.downloadsDirectory,
        threadCount: Int = 8,
        speedLimitBytesPerSec: Int64 = 0,
        quality: String = "bestvideo+bestaudio/best",
        format: String = "mp4",
        downloadSubtitles: Bool = false,
        selectedFileIndices: [Int]? = nil,
        customFilename: String? = nil
    ) {
        self.outputDirectory = outputDirectory
        self.threadCount = max(1, min(16, threadCount))
        self.speedLimitBytesPerSec = speedLimitBytesPerSec
        self.quality = quality
        self.format = format
        self.downloadSubtitles = downloadSubtitles
        self.selectedFileIndices = selectedFileIndices
        // Sanitize: strip path separators and parent-directory traversal ("..") so a
        // crafted filename like "../../etc/cron" cannot escape the output directory.
        if let name = customFilename {
            let safe = URL(fileURLWithPath: name).lastPathComponent
            self.customFilename = safe.isEmpty ? nil : safe
        } else {
            self.customFilename = nil
        }
    }

    public static let `default` = DownloadOptions()
}
