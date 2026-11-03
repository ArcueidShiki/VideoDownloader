namespace AnyDownloader.Core;

using System;

/// <summary>
/// Represents the status of a download operation
/// </summary>
public class DownloadStatus
{
    private DownloadState _state = DownloadState.Pending;
    private double _progress;
    private long _downloadedBytes;
    private long _totalBytes;
    private long _speed;
    private string? _errorMessage;
    private DateTime? _startTime;
    private DateTime? _endTime;

    /// <summary>
    /// Current state of the download
    /// </summary>
    public DownloadState State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// Download progress as percentage (0-100)
    /// </summary>
    public double Progress
    {
        get => _progress;
        set => _progress = Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// Number of bytes downloaded so far
    /// </summary>
    public long DownloadedBytes
    {
        get => _downloadedBytes;
        set => _downloadedBytes = value;
    }

    /// <summary>
    /// Total number of bytes to download
    /// </summary>
    public long TotalBytes
    {
        get => _totalBytes;
        set => _totalBytes = value;
    }

    /// <summary>
    /// Current download speed in bytes per second
    /// </summary>
    public long Speed
    {
        get => _speed;
        set => _speed = value;
    }

    /// <summary>
    /// Error message if download failed
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => _errorMessage = value;
    }

    /// <summary>
    /// Time when download started
    /// </summary>
    public DateTime? StartTime
    {
        get => _startTime;
        set => _startTime = value;
    }

    /// <summary>
    /// Time when download completed/failed
    /// </summary>
    public DateTime? EndTime
    {
        get => _endTime;
        set => _endTime = value;
    }

    /// <summary>
    /// Calculates the progress percentage based on downloaded and total bytes
    /// </summary>
    public void CalculateProgress()
    {
        if (_totalBytes <= 0)
        {
            _progress = 0;
            return;
        }

        _progress = Math.Clamp((_downloadedBytes * 100.0) / _totalBytes, 0, 100);
    }

    /// <summary>
    /// Calculates the estimated time remaining
    /// </summary>
    /// <returns>TimeSpan representing ETA, or null if speed is 0</returns>
    public TimeSpan? CalculateETA()
    {
        if (_speed <= 0)
            return null;

        long remainingBytes = _totalBytes - _downloadedBytes;
        if (remainingBytes <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds(remainingBytes / (double)_speed);
    }

    /// <summary>
    /// Gets the formatted speed string (e.g., "1.5 MB/s")
    /// </summary>
    public string GetFormattedSpeed()
    {
        if (Speed <= 0)
            return "Connecting...";

        return Speed switch
        {
            < 1024 => $"{Speed} B/s",
            < 1024 * 1024 => $"{Speed / 1024.0:F1} KB/s",
            < 1024 * 1024 * 1024 => $"{Speed / (1024.0 * 1024):F1} MB/s",
            _ => $"{Speed / (1024.0 * 1024 * 1024):F2} GB/s"
        };
    }

    /// <summary>
    /// Gets the formatted size string (e.g., "150.5 MB")
    /// </summary>
    public string GetFormattedSize()
    {
        return FormatBytes(_totalBytes);
    }

    /// <summary>
    /// Gets the formatted downloaded size string
    /// </summary>
    public string GetFormattedDownloaded()
    {
        return $"{FormatBytes(_downloadedBytes)} / {FormatBytes(_totalBytes)}";
    }

    /// <summary>
    /// Checks if the download is currently active (downloading)
    /// </summary>
    public bool IsActive()
    {
        return _state == DownloadState.Downloading;
    }

    /// <summary>
    /// Checks if the download is in a terminal state (completed, failed, or cancelled)
    /// </summary>
    public bool IsTerminal()
    {
        return _state is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled;
    }

    /// <summary>
    /// Gets a human-readable duration string
    /// </summary>
    public string? GetDuration()
    {
        if (!_startTime.HasValue || !_endTime.HasValue)
            return null;

        var duration = _endTime.Value - _startTime.Value;
        return duration.ToString(@"hh\:mm\:ss");
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            < 1024L * 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
            _ => $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB"
        };
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"DownloadStatus [{State}] - {Progress:F1}% - {GetFormattedDownloaded()}";
    }
}
