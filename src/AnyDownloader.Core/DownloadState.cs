namespace AnyDownloader.Core;

/// <summary>
/// Download state enumeration
/// </summary>
public enum DownloadState
{
    /// <summary>
    /// Download is pending in queue
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Download is in progress
    /// </summary>
    Downloading = 1,

    /// <summary>
    /// Download is paused
    /// </summary>
    Paused = 2,

    /// <summary>
    /// Download completed successfully
    /// </summary>
    Completed = 3,

    /// <summary>
    /// Download failed
    /// </summary>
    Failed = 4,

    /// <summary>
    /// Download was cancelled
    /// </summary>
    Cancelled = 5
}
