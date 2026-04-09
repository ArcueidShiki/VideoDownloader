namespace AnyDownloader.Core;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Interface for download queue management
/// </summary>
public interface IDownloadQueueManager
{
    /// <summary>
    /// Gets the current queue length
    /// </summary>
    int QueueLength { get; }

    /// <summary>
    /// Gets the number of active downloads
    /// </summary>
    int ActiveDownloads { get; }

    /// <summary>
    /// Gets the maximum concurrent downloads
    /// </summary>
    int MaxConcurrentDownloads { get; }

    /// <summary>
    /// Adds a download to the queue
    /// </summary>
    /// <param name="item">Download item to add</param>
    /// <returns>Queue position</returns>
    Task<int> EnqueueAsync(DownloadQueueItem item);

    /// <summary>
    /// Removes a download from the queue
    /// </summary>
    /// <param name="itemId">Download item ID</param>
    /// <returns>True if removed successfully</returns>
    Task<bool> DequeueAsync(string itemId);

    /// <summary>
    /// Gets all queued items
    /// </summary>
    IEnumerable<DownloadQueueItem> GetQueuedItems();

    /// <summary>
    /// Gets all active downloads
    /// </summary>
    IEnumerable<DownloadQueueItem> GetActiveDownloads();

    /// <summary>
    /// Gets a specific queue item
    /// </summary>
    DownloadQueueItem? GetItem(string itemId);

    /// <summary>
    /// Starts processing the queue
    /// </summary>
    Task StartProcessingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops processing the queue
    /// </summary>
    Task StopProcessingAsync();

    /// <summary>
    /// Pauses a specific download
    /// </summary>
    Task<bool> PauseDownloadAsync(string itemId);

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    Task<bool> ResumeDownloadAsync(string itemId);

    /// <summary>
    /// Cancels a download
    /// </summary>
    Task<bool> CancelDownloadAsync(string itemId);

    /// <summary>
    /// Updates queue configuration
    /// </summary>
    void UpdateConfiguration(QueueConfiguration configuration);

    /// <summary>
    /// Gets queue statistics
    /// </summary>
    QueueStatistics GetStatistics();
}

/// <summary>
/// Queue configuration
/// </summary>
public class QueueConfiguration
{
    /// <summary>
    /// Maximum concurrent downloads for free users
    /// </summary>
    public int MaxConcurrentDownloadsFree { get; set; } = 2;

    /// <summary>
    /// Maximum concurrent downloads for premium users
    /// </summary>
    public int MaxConcurrentDownloadsPremium { get; set; } = 5;

    /// <summary>
    /// Maximum concurrent downloads for VIP users
    /// </summary>
    public int MaxConcurrentDownloadsVip { get; set; } = 10;

    /// <summary>
    /// Global maximum concurrent downloads
    /// </summary>
    public int GlobalMaxConcurrentDownloads { get; set; } = 20;

    /// <summary>
    /// Rate limit for free users (bytes per second, 0 = unlimited)
    /// </summary>
    public long RateLimitFree { get; set; } = 0;

    /// <summary>
    /// Rate limit for premium users
    /// </summary>
    public long RateLimitPremium { get; set; } = 0;

    /// <summary>
    /// Rate limit for VIP users
    /// </summary>
    public long RateLimitVip { get; set; } = 0;

    /// <summary>
    /// Enable queue processing
    /// </summary>
    public bool EnableQueueProcessing { get; set; } = true;
}

/// <summary>
/// Queue statistics
/// </summary>
public class QueueStatistics
{
    /// <summary>
    /// Total items processed
    /// </summary>
    public long TotalProcessed { get; set; }

    /// <summary>
    /// Successful downloads
    /// </summary>
    public long SuccessfulDownloads { get; set; }

    /// <summary>
    /// Failed downloads
    /// </summary>
    public long FailedDownloads { get; set; }

    /// <summary>
    /// Cancelled downloads
    /// </summary>
    public long CancelledDownloads { get; set; }

    /// <summary>
    /// Average wait time in seconds
    /// </summary>
    public double AverageWaitTimeSeconds { get; set; }

    /// <summary>
    /// Average download time in seconds
    /// </summary>
    public double AverageDownloadTimeSeconds { get; set; }

    /// <summary>
    /// Total data downloaded in bytes
    /// </summary>
    public long TotalDataDownloaded { get; set; }

    /// <summary>
    /// Queue throughput (downloads per hour)
    /// </summary>
    public double ThroughputPerHour { get; set; }
}
