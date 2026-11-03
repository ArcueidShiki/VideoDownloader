namespace AnyDownloader.Engine;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using MonoTorrent.Client;

/// <summary>
/// Download service implementation for torrents using MonoTorrent
/// </summary>
public class TorrentDownloadService : IDownloadService, IDisposable
{
    private readonly ILogger<TorrentDownloadService>? _logger;
    private readonly string _downloadPath;
    private readonly ClientEngine? _engine;
    private readonly ConcurrentDictionary<string, TorrentManager> _activeTorrents;

    /// <summary>
    /// Gets the supported platform
    /// </summary>
    public VideoPlatform SupportedPlatform => VideoPlatform.Magnet;

    /// <summary>
    /// Initializes a new instance of the TorrentDownloadService
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="downloadPath">Path to save downloaded files</param>
    public TorrentDownloadService(
        ILogger<TorrentDownloadService>? logger = null,
        string? downloadPath = null)
    {
        _logger = logger;
        _downloadPath = downloadPath ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads", "Torrents");
        Directory.CreateDirectory(_downloadPath);

        try
        {
            // Initialize the client engine with minimal settings
            _engine = new ClientEngine();
            _logger?.LogInformation("Torrent engine initialized");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize torrent engine");
        }

        _activeTorrents = new ConcurrentDictionary<string, TorrentManager>();
    }

    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_engine == null)
        {
            return DownloadResult.FailureResult("Torrent engine not initialized");
        }

        var downloadId = Guid.NewGuid().ToString();
        TorrentManager? manager = null;

        try
        {
            _logger?.LogInformation("Starting torrent download: {Url}", url);

            // Load torrent from magnet link or torrent file
            MagnetLink? magnetLink = null;
            ITorrentInfo? torrentInfo = null;

            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Loading from magnet link");
                magnetLink = MagnetLink.Parse(url);
            }
            else if (File.Exists(url))
            {
                _logger?.LogDebug("Loading from torrent file: {Path}", url);
                torrentInfo = await Torrent.LoadAsync(url);
            }
            else
            {
                return DownloadResult.FailureResult($"Invalid torrent source: {url}");
            }

            // Create torrent manager
            if (magnetLink != null)
            {
                manager = await _engine.AddAsync(magnetLink, _downloadPath);
            }
            else if (torrentInfo != null)
            {
                manager = await _engine.AddAsync(magnetLink ?? throw new ArgumentException("Either magnetLink or torrentInfo must be provided"), _downloadPath);
            }

            if (manager == null)
            {
                return DownloadResult.FailureResult("Failed to create torrent manager");
            }

            _activeTorrents[downloadId] = manager;

            _logger?.LogInformation("Torrent added: {Name}", manager.Torrent?.Name ?? "Unknown");

            // Start download
            await manager.StartAsync();

            // Monitor progress
            var lastProgress = 0.0;
            while (manager.State != TorrentState.Seeding && 
                   manager.State != TorrentState.Stopped && 
                   manager.State != TorrentState.Error &&
                   !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken);

                var currentProgress = manager.Progress;
                if (Math.Abs(currentProgress - lastProgress) > 0.5) // Report every 0.5%
                {
                    progress?.Report(currentProgress);
                    lastProgress = currentProgress;
                }

                _logger?.LogDebug("Torrent progress: {Progress:F2}%", currentProgress);
            }

            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Torrent download cancelled: {Name}", manager.Torrent?.Name);
                await manager.StopAsync();
                _activeTorrents.TryRemove(downloadId, out _);
                return DownloadResult.FailureResult("Download cancelled");
            }

            // Stop torrent
            await manager.StopAsync();
            _activeTorrents.TryRemove(downloadId, out _);

            _logger?.LogInformation("Torrent download completed: {Name}", manager.Torrent?.Name);

            // Find downloaded files
            var downloadedFiles = manager.Files
                .Where(f => f.BitField.TrueCount == f.BitField.Length)
                .Select(f => Path.Combine(_downloadPath, f.Path))
                .ToList();

            var metadata = new DownloadMetadata
            {
                Title = manager.Torrent?.Name,
                FileSize = manager.Torrent?.Size,
                SubtitleFiles = downloadedFiles.Where(f => 
                    f.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)).ToList()
            };

            return DownloadResult.SuccessResult(downloadedFiles.FirstOrDefault() ?? _downloadPath, metadata);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Torrent download cancelled: {Url}", url);
            if (manager != null)
            {
                await manager.StopAsync();
                _activeTorrents.TryRemove(downloadId, out _);
            }
            return DownloadResult.FailureResult("Download cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Torrent download failed: {Url}", url);
            if (manager != null)
            {
                _activeTorrents.TryRemove(downloadId, out _);
            }
            return DownloadResult.FailureResult($"Download failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public DownloadStatus GetStatus(string downloadId)
    {
        if (_activeTorrents.TryGetValue(downloadId, out var manager))
        {
            var state = manager.State switch
            {
                TorrentState.Downloading => DownloadState.Downloading,
                TorrentState.Paused => DownloadState.Paused,
                TorrentState.Seeding => DownloadState.Completed,
                TorrentState.Stopped => DownloadState.Paused,
                TorrentState.Hashing => DownloadState.Downloading,
                TorrentState.Metadata => DownloadState.Downloading,
                _ => DownloadState.Pending
            };

            return new DownloadStatus
            {
                State = state,
                Progress = manager.Progress,
                DownloadedBytes = (long)manager.Monitor.DataBytesDownloaded,
                TotalBytes = manager.Torrent?.Size ?? 0,
                Speed = (long)manager.Monitor.DownloadRate
            };
        }

        return new DownloadStatus { State = DownloadState.Completed };
    }

    /// <inheritdoc/>
    public async Task<bool> PauseAsync(string downloadId)
    {
        if (_activeTorrents.TryGetValue(downloadId, out var manager))
        {
            try
            {
                await manager.StopAsync();
                _logger?.LogInformation("Torrent paused: {Name}", manager.Torrent?.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to pause torrent: {Name}", manager.Torrent?.Name);
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> ResumeAsync(string downloadId)
    {
        if (_activeTorrents.TryGetValue(downloadId, out var manager))
        {
            try
            {
                await manager.StartAsync();
                _logger?.LogInformation("Torrent resumed: {Name}", manager.Torrent?.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to resume torrent: {Name}", manager.Torrent?.Name);
                return false;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> CancelAsync(string downloadId)
    {
        if (_activeTorrents.TryGetValue(downloadId, out var manager))
        {
            try
            {
                await manager.StopAsync();
                _activeTorrents.TryRemove(downloadId, out _);
                _logger?.LogInformation("Torrent cancelled: {Name}", manager.Torrent?.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to cancel torrent: {Name}", manager.Torrent?.Name);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets torrent info from a magnet link or file without downloading
    /// </summary>
    public async Task<DownloadMetadata?> GetTorrentInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            ITorrentInfo? torrentInfo = null;

            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                // For magnet links, parse and wait for metadata
                var magnetLink = MagnetLink.Parse(url);
                
                // Create temporary engine to fetch metadata
                using var tempEngine = new ClientEngine();
                var tempManager = await tempEngine.AddAsync(magnetLink, _downloadPath);
                
                // Wait for metadata with timeout
                var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
                
                while (tempManager.Torrent == null && !timeoutCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(500, timeoutCts.Token);
                }
                
                await tempEngine.StopAllAsync();
                
                if (tempManager.Torrent == null)
                {
                    _logger?.LogWarning("Failed to get metadata for magnet link");
                    return null;
                }
                
                torrentInfo = tempManager.Torrent;
            }
            else if (File.Exists(url))
            {
                torrentInfo = await Torrent.LoadAsync(url);
            }
            else
            {
                return null;
            }

            return new DownloadMetadata
            {
                Title = torrentInfo.Name,
                FileSize = torrentInfo.Size,
                Format = Path.GetExtension(torrentInfo.Name).TrimStart('.')
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get torrent info");
            return null;
        }
    }

    /// <summary>
    /// Disposes the torrent engine
    /// </summary>
    public void Dispose()
    {
        if (_engine != null)
        {
            _engine.Dispose();
            _logger?.LogInformation("Torrent engine disposed");
        }
    }
}
