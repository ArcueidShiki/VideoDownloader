namespace AnyDownloader.Core;

using System;

/// <summary>
/// Represents a download item in the queue
/// </summary>
public class DownloadQueueItem
{
    private readonly DateTime _queuedAt;
    private DateTime? _startedAt;
    private DateTime? _completedAt;

    /// <summary>
    /// Unique identifier for this download
    /// </summary>
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// URL or magnet link to download
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Download options
    /// </summary>
    public DownloadOptions Options { get; set; } = new();

    /// <summary>
    /// User tier for prioritization
    /// </summary>
    public UserTier UserTier { get; set; } = UserTier.Free;

    /// <summary>
    /// Calculated priority
    /// </summary>
    public DownloadPriority Priority { get; set; } = DownloadPriority.Normal;

    /// <summary>
    /// Current download status
    /// </summary>
    public DownloadStatus Status { get; set; } = new();

    /// <summary>
    /// User ID (for authenticated users)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// When the item was queued
    /// </summary>
    public DateTime QueuedAt => _queuedAt;

    /// <summary>
    /// When the download started
    /// </summary>
    public DateTime? StartedAt => _startedAt;

    /// <summary>
    /// When the download completed
    /// </summary>
    public DateTime? CompletedAt => _completedAt;

    /// <summary>
    /// Estimated wait time in seconds
    /// </summary>
    public int EstimatedWaitSeconds { get; set; }

    /// <summary>
    /// Current queue position
    /// </summary>
    public int QueuePosition { get; set; }

    /// <summary>
    /// Error message if download failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Download result
    /// </summary>
    public DownloadResult? Result { get; set; }

    /// <summary>
    /// Creates a new download queue item
    /// </summary>
    public DownloadQueueItem()
    {
        _queuedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the download as started
    /// </summary>
    public void MarkStarted()
    {
        _startedAt = DateTime.UtcNow;
        Status.State = DownloadState.Downloading;
    }

    /// <summary>
    /// Marks the download as completed
    /// </summary>
    public void MarkCompleted(DownloadResult result)
    {
        _completedAt = DateTime.UtcNow;
        Status.State = result.Success ? DownloadState.Completed : DownloadState.Failed;
        Result = result;
        ErrorMessage = result.ErrorMessage;
    }

    /// <summary>
    /// Calculates priority based on user tier
    /// </summary>
    public void CalculatePriority()
    {
        Priority = UserTier switch
        {
            UserTier.VIP => DownloadPriority.Critical,
            UserTier.Premium => DownloadPriority.High,
            UserTier.Free => DownloadPriority.Low,
            _ => DownloadPriority.Normal
        };
    }

    /// <summary>
    /// Gets a human-readable wait time
    /// </summary>
    public string GetFormattedWaitTime()
    {
        if (EstimatedWaitSeconds < 60)
            return $"{EstimatedWaitSeconds} seconds";
        if (EstimatedWaitSeconds < 3600)
            return $"{EstimatedWaitSeconds / 60} minutes";
        return $"{EstimatedWaitSeconds / 3600:F1} hours";
    }
}
