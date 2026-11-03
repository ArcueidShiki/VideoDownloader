namespace AnyDownloader.Core;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interface for download services
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Gets the supported platform for this download service
    /// </summary>
    VideoPlatform SupportedPlatform { get; }

    /// <summary>
    /// Downloads a video from the specified URL
    /// </summary>
    /// <param name="url">The URL to download from</param>
    /// <param name="options">Download options</param>
    /// <param name="progress">Progress reporter (0-100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Download result</returns>
    Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets download status
    /// </summary>
    /// <param name="downloadId">Download identifier</param>
    /// <returns>Current download status</returns>
    DownloadStatus GetStatus(string downloadId);

    /// <summary>
    /// Pauses an active download
    /// </summary>
    /// <param name="downloadId">Download identifier</param>
    /// <returns>True if paused successfully</returns>
    Task<bool> PauseAsync(string downloadId);

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    /// <param name="downloadId">Download identifier</param>
    /// <returns>True if resumed successfully</returns>
    Task<bool> ResumeAsync(string downloadId);

    /// <summary>
    /// Cancels an active or paused download
    /// </summary>
    /// <param name="downloadId">Download identifier</param>
    /// <returns>True if cancelled successfully</returns>
    Task<bool> CancelAsync(string downloadId);
}
