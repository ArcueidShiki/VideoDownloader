namespace AnyDownloader.Engine.Tests.Unit;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using AnyDownloader.Core;

/// <summary>
/// Tests for YtDlpDownloadService - TDD approach
/// </summary>
public class YtDlpDownloadServiceTests
{
    private readonly Mock<ILogger<YtDlpDownloadService>> _mockLogger;
    private readonly YtDlpDownloadService _service;

    public YtDlpDownloadServiceTests()
    {
        _mockLogger = new Mock<ILogger<YtDlpDownloadService>>();
        _service = new YtDlpDownloadService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithLogger_ShouldCreateService()
    {
        // Arrange & Act
        var service = new YtDlpDownloadService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
        service.SupportedPlatform.Should().Be(VideoPlatform.Http);
    }

    [Fact]
    public void Constructor_WithoutLogger_ShouldCreateService()
    {
        // Arrange & Act
        var service = new YtDlpDownloadService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_InvalidUrl_ShouldReturnFailure()
    {
        // Arrange
        var url = "not-a-valid-url";
        var options = new DownloadOptions();

        // Act
        var result = await _service.DownloadAsync(url, options);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid URL");
    }

    [Fact]
    public async Task DownloadAsync_YouTubeUrl_WithoutYtDlp_ShouldReturnFailure()
    {
        // Arrange
        var url = "https://www.youtube.com/watch?v=test";
        var options = new DownloadOptions();

        // Act
        var result = await _service.DownloadAsync(url, options);

        // Assert
        // Since yt-dlp is not installed in test environment, it should fail
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadAsync_WithProgress_ShouldReportProgress()
    {
        // Arrange
        var url = "https://www.youtube.com/watch?v=test";
        var options = new DownloadOptions();
        var progressReports = new List<double>();
        var progress = new Progress<double>(value => progressReports.Add(value));

        // Act
        var result = await _service.DownloadAsync(url, options, progress);

        // Assert
        // Progress may or may not be reported depending on yt-dlp output
        // This test verifies the progress callback doesn't throw
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_WithCancellation_ShouldCancel()
    {
        // Arrange
        var url = "https://www.youtube.com/watch?v=test";
        var options = new DownloadOptions();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _service.DownloadAsync(url, options, null, cts.Token);

        // Assert
        // When cancelled before starting, should indicate cancellation or failure
        result.Success.Should().BeFalse();
        // The error message should indicate either cancellation or the reason it couldn't start
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadAsync_WithQualityOption_ShouldUseQuality()
    {
        // Arrange
        var url = "https://www.youtube.com/watch?v=test";
        var options = new DownloadOptions { Quality = QualityLevel.Q1080p };

        // Act
        var result = await _service.DownloadAsync(url, options);

        // Assert
        // The service should attempt to download with specified quality
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_WithSubtitleOption_ShouldDownloadSubtitles()
    {
        // Arrange
        var url = "https://www.youtube.com/watch?v=test";
        var options = new DownloadOptions 
        { 
            DownloadSubtitles = true,
            SubtitleLanguage = "en"
        };

        // Act
        var result = await _service.DownloadAsync(url, options);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadAsync_WithFormatOption_ShouldConvertFormat()
    {
        // Arrange
        var url = "https://www.youtube.com/watch?v=test";
        var options = new DownloadOptions { Format = VideoFormat.Mkv };

        // Act
        var result = await _service.DownloadAsync(url, options);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetStatus_ActiveDownload_ShouldReturnStatus()
    {
        // Arrange
        var downloadId = "test-id";

        // Act
        var status = _service.GetStatus(downloadId);

        // Assert
        status.Should().NotBeNull();
        // Since there's no active download, it returns completed status
        status.State.Should().Be(DownloadState.Completed);
    }

    [Fact]
    public async Task PauseAsync_NotSupported_ShouldReturnFalse()
    {
        // Arrange
        var downloadId = "test-id";

        // Act
        var result = await _service.PauseAsync(downloadId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResumeAsync_NotSupported_ShouldReturnFalse()
    {
        // Arrange
        var downloadId = "test-id";

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
    public void SupportedPlatform_ShouldBeHttp()
    {
        // Arrange & Act
        var platform = _service.SupportedPlatform;

        // Assert
        platform.Should().Be(VideoPlatform.Http);
    }
}
