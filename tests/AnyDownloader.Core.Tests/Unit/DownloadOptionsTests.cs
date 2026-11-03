namespace AnyDownloader.Core.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for DownloadOptions model - TDD approach
/// </summary>
public class DownloadOptionsTests
{
    [Fact]
    public void DownloadOptions_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Arrange & Act
        var options = new DownloadOptions();
        
        // Assert
        options.Quality.Should().Be(QualityLevel.Auto);
        options.Format.Should().Be(VideoFormat.Mp4);
        options.DownloadSubtitles.Should().BeFalse();
        options.EncryptDownload.Should().BeFalse();
    }

    [Fact]
    public void DownloadOptions_WithQuality_ShouldSetQuality()
    {
        // Arrange & Act
        var options = new DownloadOptions { Quality = QualityLevel.Q1080p };
        
        // Assert
        options.Quality.Should().Be(QualityLevel.Q1080p);
    }

    [Fact]
    public void DownloadOptions_WithFormat_ShouldSetFormat()
    {
        // Arrange & Act
        var options = new DownloadOptions { Format = VideoFormat.Mkv };
        
        // Assert
        options.Format.Should().Be(VideoFormat.Mkv);
    }

    [Fact]
    public void DownloadOptions_WithSubtitles_ShouldEnableSubtitles()
    {
        // Arrange & Act
        var options = new DownloadOptions { DownloadSubtitles = true };
        
        // Assert
        options.DownloadSubtitles.Should().BeTrue();
    }

    [Fact]
    public void DownloadOptions_WithEncryption_ShouldEnableEncryption()
    {
        // Arrange & Act
        var options = new DownloadOptions { EncryptDownload = true };
        
        // Assert
        options.EncryptDownload.Should().BeTrue();
    }

    [Fact]
    public void DownloadOptions_WithSubtitleLanguage_ShouldSetLanguage()
    {
        // Arrange & Act
        var options = new DownloadOptions { SubtitleLanguage = "en" };
        
        // Assert
        options.SubtitleLanguage.Should().Be("en");
    }

    [Fact]
    public void DownloadOptions_WithMaxConcurrent_ShouldSetLimit()
    {
        // Arrange & Act
        var options = new DownloadOptions { MaxConcurrentDownloads = 5 };
        
        // Assert
        options.MaxConcurrentDownloads.Should().Be(5);
    }

    [Fact]
    public void DownloadOptions_WithSpeedLimit_ShouldSetLimit()
    {
        // Arrange & Act
        var options = new DownloadOptions { SpeedLimitBytesPerSecond = 1024 * 1024 };
        
        // Assert
        options.SpeedLimitBytesPerSecond.Should().Be(1024 * 1024);
    }

    [Fact]
    public void DownloadOptions_Clone_ShouldCreateIdenticalCopy()
    {
        // Arrange
        var original = new DownloadOptions
        {
            Quality = QualityLevel.Q4K,
            Format = VideoFormat.WebM,
            DownloadSubtitles = true,
            SubtitleLanguage = "es"
        };
        
        // Act
        var clone = original.Clone();
        
        // Assert
        clone.Should().NotBeSameAs(original);
        clone.Quality.Should().Be(original.Quality);
        clone.Format.Should().Be(original.Format);
        clone.DownloadSubtitles.Should().Be(original.DownloadSubtitles);
        clone.SubtitleLanguage.Should().Be(original.SubtitleLanguage);
    }
}
