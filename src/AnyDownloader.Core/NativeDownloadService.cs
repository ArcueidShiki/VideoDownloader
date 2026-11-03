namespace AnyDownloader.Core;

using System;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core.ResourceLocator;
using AnyDownloader.Core.Downloader;

/// <summary>
/// Unified download service that locates and downloads videos
/// </summary>
public class NativeDownloadService : IDownloadService
{
    private readonly VideoResourceLocator _resourceLocator;
    private readonly HttpDownloader _httpDownloader;

    public VideoPlatform SupportedPlatform => VideoPlatform.Http;

    public NativeDownloadService()
    {
        _resourceLocator = new VideoResourceLocator();
        _httpDownloader = new HttpDownloader();
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Locate the actual video resource
            progress?.Report(5); // 5% - Locating
            
            var resourceInfo = await _resourceLocator.LocateAsync(url, cancellationToken);
            
            if (resourceInfo == null || string.IsNullOrEmpty(resourceInfo.VideoUrl))
            {
                return DownloadResult.FailureResult("Could not locate video resource. The site may not be supported or the video is protected.");
            }
            
            progress?.Report(15); // 15% - Resource found
            
            // Step 2: Download the video
            var downloadOptions = new DownloadOptions
            {
                Format = options.Format,
                OutputPath = options.OutputPath,
                SpeedLimitBytesPerSecond = options.SpeedLimitBytesPerSecond
            };
            
            // Create progress wrapper to scale from 15-100%
            var downloadProgress = new Progress<double>(p => 
                progress?.Report(15 + (p * 0.85))); // Scale to 15-100%
            
            var result = await _httpDownloader.DownloadAsync(
                resourceInfo.VideoUrl,
                downloadOptions,
                downloadProgress,
                cancellationToken);
            
            if (result.Success && result.Metadata != null)
            {
                result.Metadata.Title = resourceInfo.Title;
                result.Metadata.ThumbnailUrl = resourceInfo.ThumbnailUrl;
                result.Metadata.Duration = resourceInfo.Duration;
            }
            
            return result;
        }
        catch (OperationCanceledException)
        {
            return DownloadResult.FailureResult("Download cancelled");
        }
        catch (Exception ex)
        {
            return DownloadResult.FailureResult($"Download failed: {ex.Message}");
        }
    }

    public DownloadStatus GetStatus(string downloadId)
    {
        return new DownloadStatus { State = DownloadState.Completed };
    }

    public Task<bool> PauseAsync(string downloadId)
    {
        return Task.FromResult(false);
    }

    public Task<bool> ResumeAsync(string downloadId)
    {
        return Task.FromResult(false);
    }

    public Task<bool> CancelAsync(string downloadId)
    {
        return Task.FromResult(false);
    }
}
