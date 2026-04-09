namespace AnyDownloader.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core;
using Microsoft.Extensions.Logging;

/// <summary>
/// Download queue manager with priority support and rate limiting
/// </summary>
public class DownloadQueueManager : IDownloadQueueManager, IDisposable
{
    private readonly ILogger<DownloadQueueManager>? _logger;
    private readonly IEnumerable<IDownloadService> _downloadServices;
    private readonly ConcurrentDictionary<string, DownloadQueueItem> _queuedItems;
    private readonly ConcurrentDictionary<string, DownloadQueueItem> _activeDownloads;
    private readonly QueueConfiguration _configuration;
    private readonly QueueStatistics _statistics;
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private readonly SemaphoreSlim _queueSemaphore;
    private bool _isProcessing;

    /// <inheritdoc/>
    public int QueueLength => _queuedItems.Count;

    /// <inheritdoc/>
    public int ActiveDownloads => _activeDownloads.Count;

    /// <inheritdoc/>
    public int MaxConcurrentDownloads => _configuration.GlobalMaxConcurrentDownloads;

    /// <summary>
    /// Initializes a new instance of the DownloadQueueManager
    /// </summary>
    public DownloadQueueManager(
        IEnumerable<IDownloadService> downloadServices,
        ILogger<DownloadQueueManager>? logger = null,
        QueueConfiguration? configuration = null)
    {
        _downloadServices = downloadServices ?? throw new ArgumentNullException(nameof(downloadServices));
        _logger = logger;
        _configuration = configuration ?? new QueueConfiguration();
        _queuedItems = new ConcurrentDictionary<string, DownloadQueueItem>();
        _activeDownloads = new ConcurrentDictionary<string, DownloadQueueItem>();
        _statistics = new QueueStatistics();
        _queueSemaphore = new SemaphoreSlim(_configuration.GlobalMaxConcurrentDownloads);
    }

    /// <inheritdoc/>
    public Task<int> EnqueueAsync(DownloadQueueItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        // Calculate priority based on user tier
        item.CalculatePriority();

        // Add to queue
        _queuedItems[item.Id] = item;

        // Update queue positions
        UpdateQueuePositions();

        // Estimate wait time
        item.EstimatedWaitSeconds = EstimateWaitTime(item);

        _logger?.LogInformation("Download queued: {Id}, Position: {Position}, Priority: {Priority}",
            item.Id, item.QueuePosition, item.Priority);

        return Task.FromResult(item.QueuePosition);
    }

    /// <inheritdoc/>
    public Task<bool> DequeueAsync(string itemId)
    {
        if (_queuedItems.TryRemove(itemId, out var item))
        {
            _logger?.LogInformation("Download removed from queue: {Id}", itemId);
            UpdateQueuePositions();
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public IEnumerable<DownloadQueueItem> GetQueuedItems()
    {
        return _queuedItems.Values
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.QueuedAt);
    }

    /// <inheritdoc/>
    public IEnumerable<DownloadQueueItem> GetActiveDownloads()
    {
        return _activeDownloads.Values;
    }

    /// <inheritdoc/>
    public DownloadQueueItem? GetItem(string itemId)
    {
        return _queuedItems.TryGetValue(itemId, out var queued) ? queued :
               _activeDownloads.TryGetValue(itemId, out var active) ? active : null;
    }

    /// <inheritdoc/>
    public Task StartProcessingAsync(CancellationToken cancellationToken = default)
    {
        if (_isProcessing)
        {
            _logger?.LogWarning("Queue processing already started");
            return Task.CompletedTask;
        }

        _isProcessing = true;
        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessQueueAsync(_processingCts.Token);

        _logger?.LogInformation("Queue processing started");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopProcessingAsync()
    {
        if (!_isProcessing)
        {
            return;
        }

        _isProcessing = false;
        _processingCts?.Cancel();

        if (_processingTask != null)
        {
            await _processingTask.ConfigureAwait(false);
        }

        _logger?.LogInformation("Queue processing stopped");
    }

    /// <inheritdoc/>
    public async Task<bool> PauseDownloadAsync(string itemId)
    {
        if (_activeDownloads.TryGetValue(itemId, out var item))
        {
            var service = GetDownloadService(item.Url);
            if (service != null)
            {
                var result = await service.PauseAsync(itemId);
                if (result)
                {
                    item.Status.State = DownloadState.Paused;
                    _logger?.LogInformation("Download paused: {Id}", itemId);
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> ResumeDownloadAsync(string itemId)
    {
        if (_activeDownloads.TryGetValue(itemId, out var item))
        {
            var service = GetDownloadService(item.Url);
            if (service != null)
            {
                var result = await service.ResumeAsync(itemId);
                if (result)
                {
                    item.Status.State = DownloadState.Downloading;
                    _logger?.LogInformation("Download resumed: {Id}", itemId);
                    return true;
                }
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> CancelDownloadAsync(string itemId)
    {
        // Try to cancel from active downloads
        if (_activeDownloads.TryRemove(itemId, out var activeItem))
        {
            var service = GetDownloadService(activeItem.Url);
            if (service != null)
            {
                await service.CancelAsync(itemId);
                activeItem.Status.State = DownloadState.Cancelled;
                _statistics.CancelledDownloads++;
                _logger?.LogInformation("Download cancelled: {Id}", itemId);
                return true;
            }
        }

        // Try to cancel from queue
        return await DequeueAsync(itemId);
    }

    /// <inheritdoc/>
    public void UpdateConfiguration(QueueConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        _configuration.MaxConcurrentDownloadsFree = configuration.MaxConcurrentDownloadsFree;
        _configuration.MaxConcurrentDownloadsPremium = configuration.MaxConcurrentDownloadsPremium;
        _configuration.MaxConcurrentDownloadsVip = configuration.MaxConcurrentDownloadsVip;
        _configuration.GlobalMaxConcurrentDownloads = configuration.GlobalMaxConcurrentDownloads;
        _configuration.RateLimitFree = configuration.RateLimitFree;
        _configuration.RateLimitPremium = configuration.RateLimitPremium;
        _configuration.RateLimitVip = configuration.RateLimitVip;
        _configuration.EnableQueueProcessing = configuration.EnableQueueProcessing;

        _logger?.LogInformation("Queue configuration updated");
    }

    /// <inheritdoc/>
    public QueueStatistics GetStatistics()
    {
        return new QueueStatistics
        {
            TotalProcessed = _statistics.TotalProcessed,
            SuccessfulDownloads = _statistics.SuccessfulDownloads,
            FailedDownloads = _statistics.FailedDownloads,
            CancelledDownloads = _statistics.CancelledDownloads,
            AverageWaitTimeSeconds = _statistics.AverageWaitTimeSeconds,
            AverageDownloadTimeSeconds = _statistics.AverageDownloadTimeSeconds,
            TotalDataDownloaded = _statistics.TotalDataDownloaded,
            ThroughputPerHour = _statistics.ThroughputPerHour
        };
    }

    /// <summary>
    /// Processes the download queue
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _configuration.EnableQueueProcessing)
            {
                // Check if we can start more downloads
                if (ActiveDownloads < MaxConcurrentDownloads)
                {
                    // Get next item from queue (highest priority, oldest first)
                    var nextItem = GetQueuedItems()
                        .FirstOrDefault(x => CanStartDownload(x));

                    if (nextItem != null)
                    {
                        await StartDownloadAsync(nextItem, cancellationToken);
                    }
                }

                // Update progress for active downloads
                await UpdateActiveDownloadsProgressAsync();

                // Wait before next iteration
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Queue processing cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Queue processing error");
        }
    }

    /// <summary>
    /// Checks if a download can be started
    /// </summary>
    private bool CanStartDownload(DownloadQueueItem item)
    {
        // Check global limit
        if (ActiveDownloads >= MaxConcurrentDownloads)
            return false;

        // Check user tier limits
        var userDownloads = _activeDownloads.Values.Count(x => x.UserId == item.UserId);
        var maxForTier = item.UserTier switch
        {
            UserTier.VIP => _configuration.MaxConcurrentDownloadsVip,
            UserTier.Premium => _configuration.MaxConcurrentDownloadsPremium,
            UserTier.Free => _configuration.MaxConcurrentDownloadsFree,
            _ => 3
        };

        return userDownloads < maxForTier;
    }

    /// <summary>
    /// Starts a download
    /// </summary>
    private async Task StartDownloadAsync(DownloadQueueItem item, CancellationToken cancellationToken)
    {
        try
        {
            // Remove from queue
            if (!_queuedItems.TryRemove(item.Id, out _))
                return;

            // Add to active downloads
            _activeDownloads[item.Id] = item;
            item.MarkStarted();

            _logger?.LogInformation("Starting download: {Id}, URL: {Url}", item.Id, item.Url);

            // Get appropriate download service
            var service = GetDownloadService(item.Url);
            if (service == null)
            {
                item.MarkCompleted(DownloadResult.FailureResult("No download service available for this URL"));
                _activeDownloads.TryRemove(item.Id, out _);
                _statistics.FailedDownloads++;
                return;
            }

            // Apply rate limit
            if (item.Options.SpeedLimitBytesPerSecond == null)
            {
                item.Options.SpeedLimitBytesPerSecond = GetRateLimitForTier(item.UserTier);
            }

            // Create progress reporter
            var progress = new Progress<double>(progress =>
            {
                item.Status.Progress = progress;
            });

            // Start download
            var result = await service.DownloadAsync(item.Url, item.Options, progress, cancellationToken);

            // Mark completed
            item.MarkCompleted(result);
            _activeDownloads.TryRemove(item.Id, out _);

            // Update statistics
            _statistics.TotalProcessed++;
            if (result.Success)
            {
                _statistics.SuccessfulDownloads++;
                if (result.Metadata?.FileSize.HasValue == true)
                {
                    _statistics.TotalDataDownloaded += result.Metadata.FileSize.Value;
                }
            }
            else
            {
                _statistics.FailedDownloads++;
            }

            _logger?.LogInformation("Download completed: {Id}, Success: {Success}", item.Id, result.Success);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Download failed: {Id}", item.Id);
            item.MarkCompleted(DownloadResult.FailureResult(ex.Message));
            _activeDownloads.TryRemove(item.Id, out _);
            _statistics.FailedDownloads++;
        }
    }

    /// <summary>
    /// Gets the appropriate download service for a URL
    /// </summary>
    private IDownloadService? GetDownloadService(string url)
    {
        var validator = new UrlValidator();
        var platform = validator.GetPlatform(url);

        foreach (var service in _downloadServices)
        {
            if (service.SupportedPlatform == platform)
                return service;
        }

        // Fallback to HTTP service if available
        return _downloadServices.FirstOrDefault(s => s.SupportedPlatform == VideoPlatform.Http);
    }

    /// <summary>
    /// Gets rate limit for user tier
    /// </summary>
    private long GetRateLimitForTier(UserTier tier)
    {
        return tier switch
        {
            UserTier.VIP => _configuration.RateLimitVip,
            UserTier.Premium => _configuration.RateLimitPremium,
            UserTier.Free => _configuration.RateLimitFree,
            _ => 0
        };
    }

    /// <summary>
    /// Updates queue positions
    /// </summary>
    private void UpdateQueuePositions()
    {
        var orderedItems = GetQueuedItems().ToList();
        for (int i = 0; i < orderedItems.Count; i++)
        {
            orderedItems[i].QueuePosition = i + 1;
        }
    }

    /// <summary>
    /// Estimates wait time for a new item
    /// </summary>
    private int EstimateWaitTime(DownloadQueueItem item)
    {
        var itemsAhead = GetQueuedItems()
            .Count(x => x.Priority >= item.Priority && x.QueuedAt < item.QueuedAt);

        // Estimate 30 seconds per item ahead (rough average)
        return itemsAhead * 30;
    }

    /// <summary>
    /// Updates progress for active downloads
    /// </summary>
    private async Task UpdateActiveDownloadsProgressAsync()
    {
        foreach (var item in _activeDownloads.Values)
        {
            var service = GetDownloadService(item.Url);
            if (service != null)
            {
                var status = service.GetStatus(item.Id);
                item.Status.Progress = status.Progress;
                item.Status.Speed = status.Speed;
                item.Status.DownloadedBytes = status.DownloadedBytes;
                item.Status.TotalBytes = status.TotalBytes;
                item.Status.State = status.State;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _processingCts?.Cancel();
        _processingCts?.Dispose();
        _queueSemaphore.Dispose();
    }
}
