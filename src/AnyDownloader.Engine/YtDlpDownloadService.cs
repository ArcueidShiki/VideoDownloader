namespace AnyDownloader.Engine;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core;
using Microsoft.Extensions.Logging;

/// <summary>
/// Download service implementation using yt-dlp
/// </summary>
public class YtDlpDownloadService : IDownloadService
{
    private readonly ILogger<YtDlpDownloadService>? _logger;
    private readonly string _ytDlpPath;
    private readonly string? _ffmpegPath;
    private readonly ConcurrentDictionary<string, DownloadProcessInfo> _activeDownloads;

    /// <summary>
    /// Gets the supported platform
    /// </summary>
    public VideoPlatform SupportedPlatform => VideoPlatform.Http;

    /// <summary>
    /// Initializes a new instance of the YtDlpDownloadService
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="ytDlpPath">Path to yt-dlp executable (default: search PATH)</param>
    /// <param name="ffmpegPath">Path to ffmpeg executable (default: search PATH)</param>
    public YtDlpDownloadService(
        ILogger<YtDlpDownloadService>? logger = null,
        string? ytDlpPath = null,
        string? ffmpegPath = null)
    {
        _logger = logger;
        // Try multiple locations for yt-dlp
        _ytDlpPath = ytDlpPath ?? 
                     FindYtDlpPath() ??
                     "yt-dlp";
        _ffmpegPath = ffmpegPath;
        _activeDownloads = new ConcurrentDictionary<string, DownloadProcessInfo>();
        
        _logger?.LogInformation("yt-dlp path: {Path}", _ytDlpPath);
    }

    private static string? FindYtDlpPath()
    {
        // Check common locations
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                        "Programs", "Python", "Python313", "Scripts", "yt-dlp.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                        "Programs", "Python", "Python312", "Scripts", "yt-dlp.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                        "Programs", "Python", "Python311", "Scripts", "yt-dlp.exe"),
            "yt-dlp.exe",
            "yt-dlp"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var downloadId = Guid.NewGuid().ToString();

        try
        {
            _logger?.LogInformation("Starting download: {Url}", url);

            // Validate URL
            var validator = new UrlValidator();
            if (!validator.IsValid(url))
            {
                return DownloadResult.FailureResult($"Invalid URL: {url}");
            }

            // Build yt-dlp arguments
            var arguments = BuildYtDlpArguments(url, options, out var outputPath);

            _logger?.LogDebug("yt-dlp arguments: {Arguments}", arguments);

            // Start process
            var processInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = new Process { StartInfo = processInfo };
            
            var processOutput = new List<string>();
            var downloadProgress = 0.0;

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    processOutput.Add(e.Data);
                    _logger?.LogDebug("yt-dlp output: {Output}", e.Data);

                    // Parse progress
                    var progressMatch = Regex.Match(e.Data, @"\[download\]\s+(\d+\.?\d*)%");
                    if (progressMatch.Success && double.TryParse(progressMatch.Groups[1].Value, out var newProgress))
                    {
                        downloadProgress = newProgress;
                        progress?.Report(newProgress);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger?.LogWarning("yt-dlp error: {Error}", e.Data);
                    processOutput.Add(e.Data);
                }
            };

            // Store active download
            var downloadInfo = new DownloadProcessInfo
            {
                Process = process,
                Options = options,
                StartTime = DateTime.UtcNow
            };
            _activeDownloads[downloadId] = downloadInfo;

            // Start process
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for completion
            await Task.Run(() =>
            {
                process.WaitForExit();
            }, cancellationToken);

            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Download cancelled: {Url}", url);
                return DownloadResult.FailureResult("Download cancelled");
            }

            // Remove from active downloads
            _activeDownloads.TryRemove(downloadId, out _);

            // Check exit code
            if (process.ExitCode != 0)
            {
                var errorMessage = string.Join("\n", processOutput.Where(l => l.Contains("ERROR")));
                _logger?.LogError("Download failed with exit code {ExitCode}: {Error}", process.ExitCode, errorMessage);
                return DownloadResult.FailureResult($"Download failed: {errorMessage ?? "Unknown error"}");
            }

            // Find downloaded file
            var downloadedFile = FindDownloadedFile(outputPath, options);

            _logger?.LogInformation("Download completed: {File}", downloadedFile);

            // Create metadata
            var metadata = await ExtractMetadataAsync(url, cancellationToken);

            return DownloadResult.SuccessResult(downloadedFile, metadata);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Download cancelled: {Url}", url);
            return DownloadResult.FailureResult("Download cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Download failed: {Url}", url);
            return DownloadResult.FailureResult($"Download failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public DownloadStatus GetStatus(string downloadId)
    {
        if (_activeDownloads.TryGetValue(downloadId, out var downloadInfo))
        {
            return downloadInfo.Status;
        }

        return new DownloadStatus { State = DownloadState.Completed };
    }

    /// <inheritdoc/>
    public Task<bool> PauseAsync(string downloadId)
    {
        // yt-dlp doesn't support pausing, we can only cancel
        // For now, return false to indicate pause is not supported
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<bool> ResumeAsync(string downloadId)
    {
        // yt-dlp doesn't support resume directly
        // Would need to restart with --download-archive
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public async Task<bool> CancelAsync(string downloadId)
    {
        if (_activeDownloads.TryGetValue(downloadId, out var downloadInfo))
        {
            try
            {
                downloadInfo.Process.Kill();
                await downloadInfo.Process.WaitForExitAsync();
                _logger?.LogInformation("Download cancelled: {DownloadId}", downloadId);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to cancel download: {DownloadId}", downloadId);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds yt-dlp command line arguments
    /// </summary>
    private string BuildYtDlpArguments(string url, DownloadOptions options, out string outputPath)
    {
        var args = new List<string>
        {
            "--no-warnings",
            "--progress"
        };

        // Output template
        outputPath = options.OutputPath ?? Path.Combine(Directory.GetCurrentDirectory(), "Downloads");
        Directory.CreateDirectory(outputPath);
        args.Add($"-o \"{Path.Combine(outputPath, "%(title)s.%(ext)s")}\"");

        // Format selection
        if (options.Quality != QualityLevel.Auto)
        {
            var qualityValue = (int)options.Quality;
            if (qualityValue > 0)
            {
                args.Add($"-f \"bestvideo[height<={qualityValue}]+bestaudio/best[height<={qualityValue}]/best\"");
            }
        }

        // Format conversion
        if (options.Format != VideoFormat.Auto && options.Format != VideoFormat.Mp4)
        {
            var formatExt = options.Format switch
            {
                VideoFormat.Mkv => "mkv",
                VideoFormat.WebM => "webm",
                VideoFormat.Mp3 => "mp3",
                VideoFormat.M4a => "m4a",
                VideoFormat.Wav => "wav",
                _ => "mp4"
            };
            args.Add($"--merge-output-format {formatExt}");
        }

        // Subtitles
        if (options.DownloadSubtitles)
        {
            args.Add("--write-sub");
            args.Add("--write-auto-sub");
            
            if (!string.IsNullOrEmpty(options.SubtitleLanguage))
            {
                args.Add($"--sub-langs \"{options.SubtitleLanguage}\"");
            }
            else
            {
                args.Add("--sub-langs \"en,.*\"");
            }
        }

        // Speed limit
        if (options.SpeedLimitBytesPerSecond.HasValue)
        {
            var limitKbps = options.SpeedLimitBytesPerSecond.Value / 1024;
            args.Add($"-r {limitKbps}K");
        }

        // Cookie file
        if (!string.IsNullOrEmpty(options.CookieFile))
        {
            args.Add($"--cookies \"{options.CookieFile}\"");
        }

        // Add URL
        args.Add($"\"{url}\"");

        return string.Join(" ", args);
    }

    /// <summary>
    /// Finds the downloaded file
    /// </summary>
    private string FindDownloadedFile(string outputPath, DownloadOptions options)
    {
        var directory = new DirectoryInfo(outputPath);
        var files = directory.GetFiles();

        // Get most recent file
        var latestFile = files
            .OrderByDescending(f => f.LastWriteTime)
            .FirstOrDefault();

        return latestFile?.FullName ?? Path.Combine(outputPath, "unknown");
    }

    /// <summary>
    /// Extracts metadata from video URL using yt-dlp
    /// </summary>
    private async Task<DownloadMetadata?> ExtractMetadataAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var arguments = $"--dump-json --no-download \"{url}\"";
            var processInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();

            var jsonOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // Simple JSON parsing (in production, use System.Text.Json)
            var metadata = new DownloadMetadata();

            // Extract title
            var titleMatch = Regex.Match(jsonOutput, @"""title"":\s*""([^""]+)""");
            if (titleMatch.Success)
                metadata.Title = titleMatch.Groups[1].Value;

            // Extract duration
            var durationMatch = Regex.Match(jsonOutput, @"""duration"":\s*(\d+)");
            if (durationMatch.Success && int.TryParse(durationMatch.Groups[1].Value, out var duration))
                metadata.Duration = TimeSpan.FromSeconds(duration);

            // Extract thumbnail
            var thumbMatch = Regex.Match(jsonOutput, @"""thumbnail"":\s*""([^""]+)""");
            if (thumbMatch.Success)
                metadata.ThumbnailUrl = thumbMatch.Groups[1].Value;

            // Extract uploader
            var uploaderMatch = Regex.Match(jsonOutput, @"""uploader"":\s*""([^""]+)""");
            if (uploaderMatch.Success)
                metadata.Author = uploaderMatch.Groups[1].Value;

            return metadata;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to extract metadata");
            return null;
        }
    }

    /// <summary>
    /// Information about an active download process
    /// </summary>
    private class DownloadProcessInfo
    {
        public Process Process { get; set; } = null!;
        public DownloadOptions Options { get; set; } = null!;
        public DateTime StartTime { get; set; }
        public DownloadStatus Status { get; set; } = new DownloadStatus { State = DownloadState.Downloading };
    }
}
