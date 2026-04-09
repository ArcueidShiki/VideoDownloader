using AnyDownloader.Windows.Models;
using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

/// <summary>
/// Tests for UrlClassifier — pure-logic, no WinUI3 dependencies.
/// </summary>
public class UrlClassifierTests
{
    // ─────────────────────────────────────────────────────
    // Classify
    // ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", DownloadType.YtDlp)]
    [InlineData("https://youtu.be/dQw4w9WgXcQ",                DownloadType.YtDlp)]
    [InlineData("https://music.youtube.com/watch?v=abc",        DownloadType.YtDlp)]
    [InlineData("https://m.youtube.com/watch?v=abc",            DownloadType.YtDlp)]
    [InlineData("https://www.tiktok.com/@user/video/123",       DownloadType.YtDlp)]
    [InlineData("https://vm.tiktok.com/ZM123/",                 DownloadType.YtDlp)]
    [InlineData("https://vt.tiktok.com/ZM456/",                 DownloadType.YtDlp)]
    [InlineData("https://www.bilibili.com/video/BV1xy",         DownloadType.YtDlp)]
    [InlineData("https://www.douyin.com/video/123",             DownloadType.YtDlp)]
    [InlineData("https://vimeo.com/123456",                     DownloadType.YtDlp)]
    [InlineData("https://player.vimeo.com/video/123",           DownloadType.YtDlp)]
    [InlineData("https://www.twitch.tv/videos/123",             DownloadType.YtDlp)]
    [InlineData("https://clips.twitch.tv/Foo",                  DownloadType.YtDlp)]
    [InlineData("https://twitter.com/user/status/123",          DownloadType.YtDlp)]
    [InlineData("https://x.com/user/status/123",                DownloadType.YtDlp)]
    [InlineData("https://www.instagram.com/p/abc/",             DownloadType.YtDlp)]
    [InlineData("https://www.facebook.com/video/123",           DownloadType.YtDlp)]
    [InlineData("https://fb.watch/abc/",                        DownloadType.YtDlp)]
    [InlineData("https://www.dailymotion.com/video/x123",       DownloadType.YtDlp)]
    [InlineData("https://www.nicovideo.jp/watch/sm123",         DownloadType.YtDlp)]
    [InlineData("https://weibo.com/123",                        DownloadType.YtDlp)]
    [InlineData("https://www.reddit.com/r/foo/comments/bar",    DownloadType.YtDlp)]
    [InlineData("https://v.redd.it/abc",                        DownloadType.YtDlp)]
    [InlineData("https://rumble.com/v123-video.html",           DownloadType.YtDlp)]
    [InlineData("https://odysee.com/@channel/video",            DownloadType.YtDlp)]
    public void Classify_KnownVideoPlatform_ReturnsYtDlp(string url, DownloadType expected)
    {
        Assert.Equal(expected, UrlClassifier.Classify(url));
    }

    [Theory]
    [InlineData("https://example.com/file.zip",    DownloadType.Http)]
    [InlineData("https://example.com/big.iso",     DownloadType.Http)]
    [InlineData("http://files.example.org/data",   DownloadType.Http)]
    [InlineData("ftp://files.example.org/data",    DownloadType.Http)] // ftp → Http engine
    public void Classify_HttpUrl_ReturnsHttp(string url, DownloadType expected)
    {
        Assert.Equal(expected, UrlClassifier.Classify(url));
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:abc123")]
    [InlineData("MAGNET:?xt=urn:btih:abc123")]
    public void Classify_MagnetLink_ReturnsTorrent(string input)
    {
        Assert.Equal(DownloadType.Torrent, UrlClassifier.Classify(input));
    }

    [Theory]
    [InlineData("https://example.com/file.torrent")]
    [InlineData("https://example.com/LINUX.TORRENT")]
    public void Classify_TorrentUrl_ReturnsTorrent(string url)
    {
        Assert.Equal(DownloadType.Torrent, UrlClassifier.Classify(url));
    }

    [Fact]
    public void Classify_EmptyString_ReturnsHttp()
    {
        Assert.Equal(DownloadType.Http, UrlClassifier.Classify(""));
        Assert.Equal(DownloadType.Http, UrlClassifier.Classify("   "));
    }

    // ─────────────────────────────────────────────────────
    // DetectPlatform
    // ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc",  "youtube")]
    [InlineData("https://youtu.be/abc",                  "youtube")]
    [InlineData("https://m.youtube.com/watch?v=abc",     "youtube")]
    [InlineData("https://www.tiktok.com/@u/video/1",     "tiktok")]
    [InlineData("https://vm.tiktok.com/abc",              "tiktok")]
    [InlineData("https://www.bilibili.com/video/BV1",    "bilibili")]
    [InlineData("https://www.douyin.com/video/1",        "douyin")]
    [InlineData("https://vimeo.com/123",                 "vimeo")]
    [InlineData("https://www.twitch.tv/videos/1",        "twitch")]
    [InlineData("https://twitter.com/x/status/1",        "twitter")]
    [InlineData("https://x.com/x/status/1",              "twitter")]
    [InlineData("https://www.instagram.com/p/abc",       "instagram")]
    [InlineData("https://www.facebook.com/video/1",      "facebook")]
    [InlineData("https://fb.watch/abc",                  "facebook")]
    [InlineData("https://www.dailymotion.com/video/x",   "dailymotion")]
    [InlineData("https://www.reddit.com/r/foo",          "reddit")]
    [InlineData("https://v.redd.it/abc",                 "reddit")]
    [InlineData("https://rumble.com/v1.html",            "rumble")]
    public void DetectPlatform_KnownHost_ReturnsLabel(string url, string expected)
    {
        Assert.Equal(expected, UrlClassifier.DetectPlatform(url));
    }

    [Theory]
    [InlineData("https://example.com/file.zip")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void DetectPlatform_UnknownOrEmpty_ReturnsEmpty(string input)
    {
        Assert.Equal(string.Empty, UrlClassifier.DetectPlatform(input));
    }

    // ─────────────────────────────────────────────────────
    // IsValid
    // ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com/file.zip")]
    [InlineData("http://example.com/file")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://www.youtube.com/watch?v=abc")]
    [InlineData("magnet:?xt=urn:btih:abc123")]
    public void IsValid_ValidInput_ReturnsTrue(string input)
    {
        Assert.True(UrlClassifier.IsValid(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public void IsValid_InvalidInput_ReturnsFalse(string input)
    {
        Assert.False(UrlClassifier.IsValid(input));
    }
}
