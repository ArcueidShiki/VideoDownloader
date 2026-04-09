namespace AnyDownloader.Core.Downloader;

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Native HTTP/HTTPS downloader with progress tracking
/// </summary>
public class HttpDownloader : IDownloadService
{
    private readonly HttpClient _httpClient;
    
    public VideoPlatform SupportedPlatform => VideoPlatform.Http;

    public HttpDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        DownloadOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get file size first
            var fileSize = await GetFileSizeAsync(url, cancellationToken);
            
            // Create download request
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            
            // Add headers
            if (options.SpeedLimitBytesPerSecond.HasValue)
            {
                // Will be handled during download
            }
            
            // Add custom headers from options if needed
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            // Ensure output directory exists
            var outputPath = options.OutputPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "AnyDownloader");
            Directory.CreateDirectory(outputPath);
            
            // Generate filename
            var fileName = GenerateFileName(url, options.Format);
            var filePath = Path.Combine(outputPath, fileName);
            
            // Download with progress
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            
            var buffer = new byte[81920];
            long totalRead = 0;
            var speedLimit = options.SpeedLimitBytesPerSecond ?? 0;
            var lastProgressTime = DateTime.UtcNow;
            
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;
                
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                
                // Report progress
                var currentProgress = fileSize > 0 ? (totalRead * 100.0 / fileSize) : 0;
                progress?.Report(currentProgress);
                
                // Speed limiting
                if (speedLimit > 0)
                {
                    var elapsed = (DateTime.UtcNow - lastProgressTime).TotalSeconds;
                    var expectedBytes = elapsed * speedLimit;
                    if (totalRead > expectedBytes)
                    {
                        var delay = (totalRead - expectedBytes) / speedLimit;
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                    }
                }
            }
            
            return DownloadResult.SuccessResult(filePath, new DownloadMetadata
            {
                FileSize = totalRead,
                Format = options.Format.ToString().ToLower()
            });
        }
        catch (OperationCanceledException)
        {
            return DownloadResult.FailureResult("Download cancelled");
        }
        catch (HttpRequestException ex)
        {
            return DownloadResult.FailureResult($"HTTP error: {ex.Message}");
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

    private async Task<long> GetFileSizeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _httpClient.SendAsync(request, ct);
            if (response.Content.Headers.ContentLength.HasValue)
                return response.Content.Headers.ContentLength.Value;
        }
        catch { }
        
        return 0;
    }

    private static string GenerateFileName(string url, VideoFormat format)
    {
        var ext = format switch
        {
            VideoFormat.Mkv => ".mkv",
            VideoFormat.WebM => ".webm",
            VideoFormat.Mp3 => ".mp3",
            VideoFormat.M4a => ".m4a",
            VideoFormat.Wav => ".wav",
            _ => ".mp4"
        };
        
        return $"download_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
    }
}
