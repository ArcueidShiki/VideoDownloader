namespace AnyDownloader.Core.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for URL validator - TDD approach
/// Following Test-Driven Development: write failing tests first
/// </summary>
public class UrlValidatorTests
{
    private readonly UrlValidator _validator;

    public UrlValidatorTests()
    {
        _validator = new UrlValidator();
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=abc123")]
    [InlineData("https://www.youtube.com/playlist?list=PL123456789")]
    [InlineData("https://youtu.be/abc123")]
    public void IsValid_YouTubeUrl_ShouldReturnTrue(string url)
    {
        // Act
        var result = _validator.IsValid(url);
        
        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.tiktok.com/@user/video/1234567890")]
    [InlineData("https://tiktok.com/t/abc123")]
    [InlineData("https://vm.tiktok.com/abc123")]
    public void IsValid_TikTokUrl_ShouldReturnTrue(string url)
    {
        // Act
        var result = _validator.IsValid(url);
        
        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1234567890")]
    [InlineData("https://bilibili.com/video/av1234567890")]
    [InlineData("https://www.bilibili.com/bangumi/play/ep123456")]
    public void IsValid_BilibiliUrl_ShouldReturnTrue(string url)
    {
        // Act
        var result = _validator.IsValid(url);
        
        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.douyin.com/video/1234567890")]
    [InlineData("https://douyin.com/share/video/1234567890")]
    public void IsValid_DouyinUrl_ShouldReturnTrue(string url)
    {
        // Act
        var result = _validator.IsValid(url);
        
        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:1234567890abcdef1234567890abcdef12345678")]
    [InlineData("magnet:?xt=urn:btih:ABCDEF1234567890ABCDEF1234567890ABCDEF12&dn=TestTorrent")]
    public void IsValid_MagnetLink_ShouldReturnTrue(string url)
    {
        // Act
        var result = _validator.IsValid(url);
        
        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://invalid-domain-xyz.com")]
    [InlineData("ftp://example.com/file")]
    public void IsValid_InvalidUrl_ShouldReturnFalse(string url)
    {
        // Act
        var result = _validator.IsValid(url);
        
        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void GetPlatform_YouTubeUrl_ShouldReturnYouTube()
    {
        // Act
        var platform = _validator.GetPlatform("https://www.youtube.com/watch?v=abc123");
        
        // Assert
        platform.Should().Be(VideoPlatform.YouTube);
    }

    [Fact]
    public void GetPlatform_TikTokUrl_ShouldReturnTikTok()
    {
        // Act
        var platform = _validator.GetPlatform("https://www.tiktok.com/@user/video/123");
        
        // Assert
        platform.Should().Be(VideoPlatform.TikTok);
    }

    [Fact]
    public void GetPlatform_UnknownUrl_ShouldReturnUnknown()
    {
        // Act
        var platform = _validator.GetPlatform("https://unknown-site.com/page/123");
        
        // Assert
        platform.Should().Be(VideoPlatform.Unknown);
    }

    [Fact]
    public void GetPlatform_MagnetLink_ShouldReturnMagnet()
    {
        // Act
        var platform = _validator.GetPlatform("magnet:?xt=urn:btih:1234567890abcdef");
        
        // Assert
        platform.Should().Be(VideoPlatform.Magnet);
    }

    [Fact]
    public void ExtractVideoId_YouTubeUrl_ShouldExtractId()
    {
        // Act
        var videoId = _validator.ExtractVideoId("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        
        // Assert
        videoId.Should().Be("dQw4w9WgXcQ");
    }

    [Fact]
    public void ExtractVideoId_TikTokUrl_ShouldExtractId()
    {
        // Act
        var videoId = _validator.ExtractVideoId("https://www.tiktok.com/@user/video/71234567890");
        
        // Assert
        videoId.Should().Be("71234567890");
    }
}
