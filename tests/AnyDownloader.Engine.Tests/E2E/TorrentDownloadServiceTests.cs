namespace AnyDownloader.Core.Tests.E2E;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Engine;
using FluentAssertions;
using Xunit;

/// <summary>
/// End-to-End tests for TorrentDownloadService
/// </summary>
public class TorrentDownloadServiceTests : IDisposable
{
    private readonly string _testDownloadPath;
    private readonly TorrentDownloadService _service;

    public TorrentDownloadServiceTests()
    {
        _testDownloadPath = Path.Combine(Path.GetTempPath(), "AnyDownloaderTorrentTests");
        Directory.CreateDirectory(_testDownloadPath);
        _service = new TorrentDownloadService(null, _testDownloadPath);
    }

    [Fact]
    public void Constructor_ShouldInitializeEngine()
    {
        // Arrange & Act
        var service = new TorrentDownloadService(null, _testDownloadPath);
        
        // Assert
        service.Should().NotBeNull();
        service.SupportedPlatform.Should().Be(VideoPlatform.Magnet);
    }

    [Fact]
    public async Task DownloadAsync_InvalidMagnetLink_ShouldReturnFailure()
    {
        // Arrange
        var invalidMagnet = "magnet:?xt=urn:btih:INVALIDINVALIDINVALIDINVALIDINVALID";
        var options = new DownloadOptions();
        
        // Act
        var result = await _service.DownloadAsync(invalidMagnet, options);
        
        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadAsync_InvalidUrl_ShouldReturnFailure()
    {
        // Arrange
        var invalidUrl = "not-a-torrent-url";
        var options = new DownloadOptions();
        
        // Act
        var result = await _service.DownloadAsync(invalidUrl, options);
        
        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid torrent source");
    }

    [Fact]
    public async Task DownloadAsync_NonExistentTorrentFile_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentFile = "C:\\nonexistent\\file.torrent";
        var options = new DownloadOptions();
        
        // Act
        var result = await _service.DownloadAsync(nonExistentFile, options);
        
        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_WithCancellation_ShouldCancel()
    {
        // Arrange
        var invalidMagnet = "magnet:?xt=urn:btih:INVALIDINVALIDINVALIDINVALIDINVALID";
        var options = new DownloadOptions();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        // Act
        var result = await _service.DownloadAsync(invalidMagnet, options, null, cts.Token);
        
        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void GetStatus_NonExistentDownload_ShouldReturnCompleted()
    {
        // Arrange
        var downloadId = "non-existent-id";
        
        // Act
        var status = _service.GetStatus(downloadId);
        
        // Assert
        status.Should().NotBeNull();
        status.State.Should().Be(DownloadState.Completed);
    }

    [Fact]
    public async Task PauseAsync_NonExistentDownload_ShouldReturnFalse()
    {
        // Arrange
        var downloadId = "non-existent-id";
        
        // Act
        var result = await _service.PauseAsync(downloadId);
        
        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeAsync_NonExistentDownload_ShouldReturnFalse()
    {
        // Arrange
        var downloadId = "non-existent-id";
        
        // Act
        var result = await _service.ResumeAsync(downloadId);
        
        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CancelAsync_NonExistentDownload_ShouldReturnFalse()
    {
        // Arrange
        var downloadId = "non-existent-id";
        
        // Act
        var result = await _service.CancelAsync(downloadId);
        
        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetTorrentInfoAsync_InvalidMagnet_ShouldReturnNull()
    {
        // Arrange
        var invalidMagnet = "magnet:?xt=urn:btih:INVALIDINVALIDINVALIDINVALIDINVALID";
        
        // Act
        var info = await _service.GetTorrentInfoAsync(invalidMagnet);
        
        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public async Task GetTorrentInfoAsync_NonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var nonExistentFile = "C:\\nonexistent\\file.torrent";
        
        // Act
        var info = await _service.GetTorrentInfoAsync(nonExistentFile);
        
        // Assert
        info.Should().BeNull();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new TorrentDownloadService(null, _testDownloadPath);
        
        // Act & Assert
        service.Dispose();
        // Should not throw
    }

    public void Dispose()
    {
        _service.Dispose();
        
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
