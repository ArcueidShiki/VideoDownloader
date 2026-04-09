namespace AnyDownloader.Windows.Models;

// ─────────────────────────────────────────────────────
// Enums shared between Models, Services, and ViewModels
// No WinUI3 / CommunityToolkit dependencies — fully unit-testable.
// ─────────────────────────────────────────────────────

/// <summary>Lifecycle states for a download task.</summary>
public enum DownloadState
{
    /// <summary>Created but not yet started.</summary>
    Pending,
    /// <summary>Fetching video/torrent metadata (yt-dlp --dump-json or magnet resolve).</summary>
    FetchingInfo,
    /// <summary>Metadata fetched; waiting for user to pick a format (yt-dlp only).</summary>
    SelectingFormat,
    /// <summary>Metadata fetched; waiting for user to select torrent files.</summary>
    SelectingFiles,
    /// <summary>Format/files confirmed; waiting in queue to start.</summary>
    Queued,
    /// <summary>Actively downloading.</summary>
    Active,
    /// <summary>User-paused; partial file preserved on disk.</summary>
    Paused,
    /// <summary>Download finished successfully.</summary>
    Completed,
    /// <summary>Terminated due to error or user cancellation.</summary>
    Failed,
}

/// <summary>Backend engine used to perform a download.</summary>
public enum DownloadType
{
    /// <summary>Direct HTTP/HTTPS download via aria2c.</summary>
    Http,
    /// <summary>Video/audio download via yt-dlp.</summary>
    YtDlp,
    /// <summary>Torrent/magnet download via aria2c.</summary>
    Torrent,
}

/// <summary>Tab filter selected in the main window toolbar.</summary>
public enum FilterTab { All, Active, Paused, Done, Torrents }

/// <summary>User's choice when adding a URL that already exists in the queue.</summary>
public enum DuplicateChoice
{
    /// <summary>Cancel the existing entry and start a fresh download.</summary>
    DownloadAgain,
    /// <summary>Add a new download alongside the existing one (useful for a different format).</summary>
    KeepBoth,
    /// <summary>Do nothing — user cancelled.</summary>
    Cancel,
}

/// <summary>User's choice in the delete-confirmation dialog.</summary>
public enum DeleteChoice
{
    /// <summary>Remove from the queue and database; leave the file on disk.</summary>
    TaskOnly,
    /// <summary>Remove from the queue and database AND delete the file from disk.</summary>
    TaskAndFile,
    /// <summary>Do nothing — user cancelled.</summary>
    Cancel,
}
