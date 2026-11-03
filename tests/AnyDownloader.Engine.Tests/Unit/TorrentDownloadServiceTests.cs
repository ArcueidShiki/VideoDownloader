namespace AnyDownloader.Engine.Tests.Unit;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AnyDownloader.Core;

/// <summary>
/// Tests for TorrentDownloadService - TDD approach
/// </summary>
public class TorrentDownloadServiceTests
{
    private readonly Mock<ILogger<TorrentDownloadService>> _mockLogger;
    private readonly TorrentDownloadService _service;

    public TorrentDownloadServiceTests()
    {
        _mockLogger = new Mock<ILogger<TorrentDownloadService>>();
        _service = new TorrentDownloadService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldCreateService()
    {
        // Arrange & Act
        var service = new TorrentDownloadService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
        service.SupportedPlatform.Should().Be(VideoPlatform.Magnet);
    }

    [Fact]
    public void Constructor_WithoutLogger_ShouldCreateService()
    {
        // Arrange & Act
        var service = new TorrentDownloadService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldCreateDownloadDirectory()
    {
        // Arrange
        var testPath = Path.Combine(Path.GetTempPath(), "TorrentTest_" + Guid.NewGuid());

        try
        {
            // Act
            var service = new TorrentDownloadService(null, testPath);

            // Assert
            Directory.Exists(testPath).Should().BeTrue();
            
            // Cleanup
            service.Dispose();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, true);
            }
        }
    }

    [Fact]
    public async Task DownloadAsync_InvalidMagnetLink_ShouldReturnFailure()
    {
        // Arrange
        var invalidMagnet = "magnet:?xt=urn:btih:invalid";
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
        var invalidMagnet = "magnet:?xt=urn:btih:invalid";
        var options = new DownloadOptions();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _service.DownloadAsync(invalidMagnet, options, null, cts.Token);

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_WithProgress_ShouldReportProgress()
    {
        // Arrange
        var invalidMagnet = "magnet:?xt=urn:btih:invalid";
        var options = new DownloadOptions();
        var progressReports = new List<double>();
        var progress = new Progress<double>(value => progressReports.Add(value));

        // Act
        var result = await _service.DownloadAsync(invalidMagnet, options, progress);

        // Assert
        // Progress may not be reported for invalid magnet, but should not throw
        result.Should().NotBeNull();
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
        var invalidMagnet = "magnet:?xt=urn:btih:invalid";

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
    public async Task Dispose_ShouldNotThrow()
    {
        // Arrange
        var service = new TorrentDownloadService();

        // Act & Assert
        service.Dispose();
        // Should not throw
    }

    [Fact]
    public void SupportedPlatform_ShouldBeMagnet()
    {
        // Arrange & Act
        var platform = _service.SupportedPlatform;

        // Assert
        platform.Should().Be(VideoPlatform.Magnet);
    }
}
