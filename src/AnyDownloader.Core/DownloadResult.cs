namespace AnyDownloader.Core;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Result of a download operation
/// </summary>
public class DownloadResult
{
    /// <summary>
    /// Whether the download was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Path to the downloaded file (if successful)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Error message (if failed)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Download metadata
    /// </summary>
    public DownloadMetadata? Metadata { get; set; }

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static DownloadResult SuccessResult(string filePath, DownloadMetadata? metadata = null)
    {
        return new DownloadResult
        {
            Success = true,
            FilePath = filePath,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static DownloadResult FailureResult(string errorMessage)
    {
        return new DownloadResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Metadata about a downloaded video
/// </summary>
public class DownloadMetadata
{
    /// <summary>
    /// Video title
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Video duration
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Video resolution (e.g., "1920x1080")
    /// </summary>
    public string? Resolution { get; set; }

    /// <summary>
    /// Video format (e.g., "mp4")
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// Thumbnail URL
    /// </summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Author/channel name
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Upload date
    /// </summary>
    public DateTime? UploadDate { get; set; }

    /// <summary>
    /// Subtitle files downloaded
    /// </summary>
    public List<string>? SubtitleFiles { get; set; }
}
