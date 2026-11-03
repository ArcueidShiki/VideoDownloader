public enum DownloadState: String, Codable, Sendable {
    case pending
    /// Fetching video info (title, formats, thumbnail) via yt-dlp --dump-json.
    case fetchingInfo
    /// Info ready — waiting for the user to pick format/resolution.
    case selectingFormat
    /// Fetching file list from the torrent/magnet metadata.
    case fetchingMetadata
    /// Metadata ready — waiting for the user to pick which files to download.
    case selectingFiles
    /// Added to queue, waiting for user to press Start.
    case queued
    case active
    case paused
    case completed
    case failed
}
