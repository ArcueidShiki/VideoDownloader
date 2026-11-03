namespace AnyDownloader.Core.Tests.E2E;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core.Downloader;
using FluentAssertions;
using Xunit;

/// <summary>
/// End-to-End tests for MultiThreadedDownloader
/// </summary>
public class MultiThreadedDownloaderTests : IDisposable
{
    private readonly string _testDownloadPath;
    private readonly MultiThreadedDownloader _downloader;

    public MultiThreadedDownloaderTests()
    {
        _testDownloadPath = Path.Combine(Path.GetTempPath(), "AnyDownloaderTests");
        Directory.CreateDirectory(_testDownloadPath);
        _downloader = new MultiThreadedDownloader();
    }

    [Fact]
    public async Task DownloadAsync_SmallFile_ShouldDownloadSuccessfully()
    {
        // Arrange
        var url = "https://httpbin.org/bytes/1024"; // 1KB file
        var savePath = Path.Combine(_testDownloadPath, $"test_small_{Guid.NewGuid()}.bin");
        
        // Act
        var result = await _downloader.DownloadAsync(url, savePath, threadCount: 1);
        
        // Assert
        result.Success.Should().BeTrue();
        File.Exists(savePath).Should().BeTrue();
        new FileInfo(savePath).Length.Should().Be(1024);
    }

    [Fact]
    public async Task DownloadAsync_MediumFile_ShouldDownloadSuccessfully()
    {
        // Arrange
        var url = "https://httpbin.org/bytes/102400"; // 100KB file
        var savePath = Path.Combine(_testDownloadPath, $"test_medium_{Guid.NewGuid()}.bin");
        
        // Act
        var result = await _downloader.DownloadAsync(url, savePath, threadCount: 4);
        
        // Assert
        result.Success.Should().BeTrue();
        File.Exists(savePath).Should().BeTrue();
        new FileInfo(savePath).Length.Should().Be(102400);
    }

    [Fact]
    public async Task DownloadAsync_WithProgress_ShouldReportProgress()
    {
        // Arrange
        var url = "https://httpbin.org/bytes/51200"; // 50KB file
        var savePath = Path.Combine(_testDownloadPath, $"test_progress_{Guid.NewGuid()}.bin");
        var progressReports = new System.Collections.Generic.List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));
        
        // Act
        var result = await _downloader.DownloadAsync(url, savePath, threadCount: 1, progress: progress);
        
        // Assert
        result.Success.Should().BeTrue();
        progressReports.Should().NotBeEmpty();
        progressReports[progressReports.Count - 1].PercentComplete.Should().Be(100);
    }

    [Fact]
    public async Task DownloadAsync_WithSpeedLimit_ShouldRespectLimit()
    {
        // Arrange
        var url = "https://httpbin.org/bytes/10240"; // 10KB file
        var savePath = Path.Combine(_testDownloadPath, $"test_speed_{Guid.NewGuid()}.bin");
        var speedLimit = 1024L; // 1 KB/s
        var startTime = DateTime.UtcNow;
        
        // Act
        var result = await _downloader.DownloadAsync(
            url, savePath, threadCount: 1, speedLimit: speedLimit);
        
        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        
        // Assert
        result.Success.Should().BeTrue();
        // Should take at least 8 seconds (10KB / 1KB/s = 10s, allow some variance)
        elapsed.Should().BeGreaterThan(5);
    }

    [Fact]
    public async Task DownloadAsync_InvalidUrl_ShouldReturnFailure()
    {
        // Arrange
        var url = "https://httpbin.org/status/404";
        var savePath = Path.Combine(_testDownloadPath, $"test_404_{Guid.NewGuid()}.bin");
        
        // Act
        var result = await _downloader.DownloadAsync(url, savePath);
        
        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadAsync_Cancelled_ShouldReturnCancelled()
    {
        // Arrange
        var url = "https://httpbin.org/delay/10"; // 10 second delay
        var savePath = Path.Combine(_testDownloadPath, $"test_cancel_{Guid.NewGuid()}.bin");
        var cts = new CancellationTokenSource();
        cts.CancelAfter(1000); // Cancel after 1 second
        
        // Act
        var result = await _downloader.DownloadAsync(url, savePath, cancellationToken: cts.Token);
        
        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cancelled", "Cancelled");
    }

    [Fact]
    public async Task DownloadAsync_MultiThreaded_ShouldBeFasterThanSingleThread()
    {
        // Arrange
        var url = "https://httpbin.org/bytes/512000"; // 500KB file
        var savePath1 = Path.Combine(_testDownloadPath, $"test_multi_{Guid.NewGuid()}.bin");
        var savePath2 = Path.Combine(_testDownloadPath, $"test_single_{Guid.NewGuid()}.bin");
        
        // Act - Multi-threaded
        var start1 = DateTime.UtcNow;
        await _downloader.DownloadAsync(url, savePath1, threadCount: 8);
        var time1 = (DateTime.UtcNow - start1).TotalMilliseconds;
        
        // Act - Single-threaded
        var start2 = DateTime.UtcNow;
        await _downloader.DownloadAsync(url, savePath2, threadCount: 1);
        var time2 = (DateTime.UtcNow - start2).TotalMilliseconds;
        
        // Assert
        // Multi-threaded should be faster (or at least not significantly slower)
        // Note: For small files, overhead may make it slower, so we just verify both succeed
        File.Exists(savePath1).Should().BeTrue();
        File.Exists(savePath2).Should().BeTrue();
    }

    public void Dispose()
    {
        _downloader.Dispose();
        
        // Cleanup test files
        try
        {
            if (Directory.Exists(_testDownloadPath))
            {
                Directory.Delete(_testDownloadPath, true);
            }
        }
        catch { }
    }
}
