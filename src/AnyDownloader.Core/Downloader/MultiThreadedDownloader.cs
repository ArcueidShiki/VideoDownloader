namespace AnyDownloader.Core.Downloader;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Multi-threaded HTTP/HTTPS downloader (IDM-style)
/// </summary>
public class MultiThreadedDownloader : IDisposable
{
    private readonly HttpClient _httpClient;
    private DownloadTask? _currentTask;
    private bool _disposed;

    public MultiThreadedDownloader()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromHours(1)
        };
        
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    /// <summary>
    /// Downloads a file with multi-threading support
    /// </summary>
    public async Task<DownloadResult> DownloadAsync(
        string url,
        string savePath,
        int threadCount = 4,
        long? speedLimit = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get file info
            var fileInfo = await GetFileInfoAsync(url, cancellationToken);
            
            if (!fileInfo.AcceptsRanges && threadCount > 1)
            {
                // Server doesn't support range requests, use single thread
                threadCount = 1;
            }
            
            // Create download task
            _currentTask = new DownloadTask
            {
                Url = url,
                SavePath = savePath,
                TotalSize = fileInfo.ContentLength,
                ThreadCount = threadCount,
                SpeedLimit = speedLimit
            };
            
            // Create temp file
            var tempPath = savePath + ".tmp";
            _currentTask.TempPath = tempPath;
            
            // Pre-allocate file
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (fileInfo.ContentLength > 0)
                {
                    fs.SetLength(fileInfo.ContentLength);
                }
            }
            
            // Start download threads
            var tasks = new List<Task<DownloadSegment>>();
            var segments = CalculateSegments(fileInfo.ContentLength, threadCount);
            
            foreach (var segment in segments)
            {
                tasks.Add(DownloadSegmentAsync(url, tempPath, segment, speedLimit, cancellationToken));
            }
            
            // Wait for all segments
            var results = await Task.WhenAll(tasks);
            
            // Merge segments (already in order)
            var totalDownloaded = results.Sum(r => r.DownloadedBytes);
            
            // Rename temp file to final
            File.Move(tempPath, savePath, true);
            
            progress?.Report(new DownloadProgress
            {
                TotalBytes = fileInfo.ContentLength,
                DownloadedBytes = totalDownloaded,
                PercentComplete = 100,
                SpeedBytesPerSecond = 0,
                State = DownloadState.Completed
            });
            
            return DownloadResult.SuccessResult(savePath, new DownloadMetadata
            {
                FileSize = totalDownloaded,
                Format = Path.GetExtension(savePath).TrimStart('.')
            });
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

    /// <summary>
    /// Pauses the current download
    /// </summary>
    public async Task<bool> PauseAsync()
    {
        if (_currentTask == null) return false;
        
        _currentTask.IsPaused = true;
        await _currentTask.CancelSegments();
        return true;
    }

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    public async Task<bool> ResumeAsync()
    {
        if (_currentTask == null) return false;
        
        _currentTask.IsPaused = false;
        // Resume logic would go here
        return true;
    }

    /// <summary>
    /// Cancels the current download
    /// </summary>
    public async Task<bool> CancelAsync()
    {
        if (_currentTask == null) return false;
        
        await _currentTask.CancelSegments();
        
        // Clean up temp file
        if (!string.IsNullOrEmpty(_currentTask.TempPath) && File.Exists(_currentTask.TempPath))
        {
            try { File.Delete(_currentTask.TempPath); } catch { }
        }
        
        return true;
    }

    private async Task<FileInfo> GetFileInfoAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        
        return new FileInfo
        {
            ContentLength = response.Content.Headers.ContentLength ?? 0,
            AcceptsRanges = response.Headers.AcceptRanges.Contains("bytes")
        };
    }

    private List<Segment> CalculateSegments(long totalSize, int threadCount)
    {
        var segments = new List<Segment>();
        
        if (totalSize <= 0)
        {
            // Unknown size, single segment
            segments.Add(new Segment { Start = 0, End = -1 });
            return segments;
        }
        
        var segmentSize = totalSize / threadCount;
        
        for (int i = 0; i < threadCount; i++)
        {
            var start = i * segmentSize;
            var end = (i == threadCount - 1) ? totalSize - 1 : start + segmentSize - 1;
            
            segments.Add(new Segment { Start = start, End = end });
        }
        
        return segments;
    }

    private async Task<DownloadSegment> DownloadSegmentAsync(
        string url,
        string tempPath,
        Segment segment,
        long? speedLimit,
        CancellationToken ct)
    {
        var downloadedBytes = 0L;
        var startTime = DateTime.UtcNow;
        var lastProgressTime = startTime;
        
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        
        if (segment.End > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(segment.Start, segment.End);
        }
        
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.None);
        fileStream.Position = segment.Start;
        
        var buffer = new byte[81920];
        
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            
            // Speed limiting
            if (speedLimit.HasValue && speedLimit.Value > 0)
            {
                var elapsed = (DateTime.UtcNow - lastProgressTime).TotalSeconds;
                var expectedBytes = elapsed * speedLimit.Value;
                if (downloadedBytes > expectedBytes)
                {
                    var delay = (downloadedBytes - expectedBytes) / speedLimit.Value;
                    await Task.Delay(TimeSpan.FromMilliseconds(delay * 1000), ct);
                }
            }
            
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0) break;
            
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            downloadedBytes += read;
        }
        
        return new DownloadSegment
        {
            DownloadedBytes = downloadedBytes,
            Success = true
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    private class FileInfo
    {
        public long ContentLength { get; set; }
        public bool AcceptsRanges { get; set; }
    }

    private class Segment
    {
        public long Start { get; set; }
        public long End { get; set; }
    }

    private class DownloadSegment
    {
        public long DownloadedBytes { get; set; }
        public bool Success { get; set; }
    }

    private class DownloadTask
    {
        public string Url { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public string TempPath { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public int ThreadCount { get; set; }
        public long? SpeedLimit { get; set; }
        public bool IsPaused { get; set; }
        
        private CancellationTokenSource? _cts;
        
        public async Task CancelSegments()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }
    }
}

/// <summary>
/// Download progress information
/// </summary>
public class DownloadProgress
{
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double PercentComplete { get; set; }
    public long SpeedBytesPerSecond { get; set; }
    public DownloadState State { get; set; }
    
    public string FormattedSpeed => FormatBytes(SpeedBytesPerSecond);
    public string FormattedProgress => $"{PercentComplete:F1}%";
    
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B/s";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB/s";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB/s";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB/s";
    }
}
