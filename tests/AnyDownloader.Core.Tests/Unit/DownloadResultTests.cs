namespace AnyDownloader.Core.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for DownloadResult model - TDD approach
/// </summary>
public class DownloadResultTests
{
    [Fact]
    public void DownloadResult_SuccessResult_ShouldCreateSuccessfulResult()
    {
        // Arrange
        var filePath = "/path/to/video.mp4";

        // Act
        var result = DownloadResult.SuccessResult(filePath);

        // Assert
        result.Success.Should().BeTrue();
        result.FilePath.Should().Be(filePath);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void DownloadResult_SuccessResult_WithMetadata_ShouldIncludeMetadata()
    {
        // Arrange
        var filePath = "/path/to/video.mp4";
        var metadata = new DownloadMetadata { Title = "Test Video" };

        // Act
        var result = DownloadResult.SuccessResult(filePath, metadata);

        // Assert
        result.Success.Should().BeTrue();
        result.FilePath.Should().Be(filePath);
        result.Metadata.Should().NotBeNull();
        result.Metadata.Title.Should().Be("Test Video");
    }

    [Fact]
    public void DownloadResult_FailureResult_ShouldCreateFailedResult()
    {
        // Arrange
        var errorMessage = "Download failed: Network error";

        // Act
        var result = DownloadResult.FailureResult(errorMessage);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(errorMessage);
        result.FilePath.Should().BeNull();
    }

    [Fact]
    public void DownloadMetadata_DefaultConstructor_ShouldSetDefaults()
    {
        // Act
        var metadata = new DownloadMetadata();

        // Assert
        metadata.Title.Should().BeNull();
        metadata.Duration.Should().BeNull();
        metadata.Resolution.Should().BeNull();
        metadata.Format.Should().BeNull();
        metadata.FileSize.Should().BeNull();
        metadata.ThumbnailUrl.Should().BeNull();
        metadata.Author.Should().BeNull();
        metadata.UploadDate.Should().BeNull();
        metadata.SubtitleFiles.Should().BeNull();
    }

    [Fact]
    public void DownloadMetadata_WithProperties_ShouldSetValues()
    {
        // Arrange & Act
        var metadata = new DownloadMetadata
        {
            Title = "Test Video",
            Duration = TimeSpan.FromMinutes(5),
            Resolution = "1920x1080",
            Format = "mp4",
            FileSize = 1024 * 1024 * 100, // 100 MB
            ThumbnailUrl = "https://example.com/thumb.jpg",
            Author = "Test Channel",
            UploadDate = DateTime.UtcNow,
            SubtitleFiles = new List<string> { "sub_en.srt", "sub_es.srt" }
        };

        // Assert
        metadata.Title.Should().Be("Test Video");
        metadata.Duration.Should().Be(TimeSpan.FromMinutes(5));
        metadata.Resolution.Should().Be("1920x1080");
        metadata.Format.Should().Be("mp4");
        metadata.FileSize.Should().Be(1024 * 1024 * 100);
        metadata.ThumbnailUrl.Should().Be("https://example.com/thumb.jpg");
        metadata.Author.Should().Be("Test Channel");
        metadata.UploadDate.Should().HaveValue();
        metadata.SubtitleFiles.Should().HaveCount(2);
    }
}
