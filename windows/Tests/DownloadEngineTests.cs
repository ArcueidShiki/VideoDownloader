using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

/// <summary>
/// Tests for DownloadEngine — argument builders and progress parsers.
/// No subprocesses are actually launched; only pure-logic paths are exercised.
/// </summary>
public class DownloadEngineTests
{
    // ─────────────────────────────────────────────────────
    // BuildYtDlpArgs
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildYtDlpArgs_Default_ContainsUrlAndDefaultQuality()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com");

        Assert.Contains("https://example.com", args);
        Assert.Contains("--format", args);
        Assert.Contains("--output",  args);
        Assert.Contains("--no-playlist", args);
        Assert.Contains("--continue", args);
    }

    [Fact]
    public void BuildYtDlpArgs_WithCustomQuality_PassesQualityFlag()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", quality: "bestvideo[height<=720]+bestaudio");

        var idx = Array.IndexOf(args, "--format");
        Assert.True(idx >= 0);
        Assert.Contains("bestvideo[height<=720]+bestaudio", args[idx + 1]);
    }

    [Fact]
    public void BuildYtDlpArgs_WithOutputDir_OutputTemplateContainsDir()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", outputDir: @"C:\MyDownloads");

        var idx = Array.IndexOf(args, "--output");
        Assert.True(idx >= 0);
        Assert.StartsWith(@"C:\MyDownloads", args[idx + 1]);
    }

    [Fact]
    public void BuildYtDlpArgs_WithFilename_SanitisesPathTraversal()
    {
        // A filename with a path separator should be stripped to just the filename part
        var args = DownloadEngine.BuildYtDlpArgs(
            "https://example.com",
            outputDir: @"C:\Downloads",
            filename: @"..\evil\video.mp4");

        var idx = Array.IndexOf(args, "--output");
        Assert.True(idx >= 0);
        // Path.GetFileName strips directory components
        Assert.DoesNotContain("..", args[idx + 1]);
        Assert.EndsWith("video.mp4", args[idx + 1]);
    }

    [Fact]
    public void BuildYtDlpArgs_MergeOutputFormat_NativeSkipsMergeFlag()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", containerFormat: "native");
        Assert.DoesNotContain("--merge-output-format", args);
    }

    [Fact]
    public void BuildYtDlpArgs_MergeOutputFormat_NonNativeAddsMergeFlag()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", containerFormat: "mp4");
        Assert.Contains("--merge-output-format", args);
        var idx = Array.IndexOf(args, "--merge-output-format");
        Assert.Equal("mp4", args[idx + 1]);
    }

    [Fact]
    public void BuildYtDlpArgs_SpeedLimit_AddsLimitRateFlag()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", speedLimitBytesPerSec: 1_048_576);
        Assert.Contains("--limit-rate", args);
        var idx = Array.IndexOf(args, "--limit-rate");
        Assert.Equal("1048576", args[idx + 1]);
    }

    [Fact]
    public void BuildYtDlpArgs_NoSpeedLimit_DoesNotAddLimitRateFlag()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", speedLimitBytesPerSec: 0);
        Assert.DoesNotContain("--limit-rate", args);
    }

    [Fact]
    public void BuildYtDlpArgs_Subtitles_AddsSubFlags()
    {
        var args = DownloadEngine.BuildYtDlpArgs("https://example.com", downloadSubtitles: true);
        Assert.Contains("--write-auto-sub", args);
        Assert.Contains("--sub-lang", args);
    }

    // ─────────────────────────────────────────────────────
    // BuildAria2cArgs
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildAria2cArgs_Default_ContainsRequiredFlags()
    {
        var args = DownloadEngine.BuildAria2cArgs("https://example.com/file.zip");

        Assert.Contains("--dir",    args);
        Assert.Contains("--split",  args);
        Assert.Contains("--continue=true", args);
        // DHT flags are only added for torrent/magnet URLs, not plain HTTP
        Assert.DoesNotContain("--enable-dht=true", args);
        Assert.Contains("https://example.com/file.zip", args);
    }

    [Fact]
    public void BuildAria2cArgs_MagnetUrl_ContainsDhtFlags()
    {
        var args = DownloadEngine.BuildAria2cArgs(
            "magnet:?xt=urn:btih:db77bcfb8e36ada244f8a2164bc91c2640c56d45");

        Assert.Contains("--enable-dht=true", args);
        Assert.Contains("--bt-enable-lpd=true", args);
        Assert.Contains("--dht-entry-point=router.bittorrent.com:6881", args);
    }

    [Fact]
    public void BuildAria2cArgs_ConnectionsClamped_To16Max()
    {
        var args = DownloadEngine.BuildAria2cArgs("https://example.com/file.zip", connections: 99);
        var idx  = Array.IndexOf(args, "--split");
        Assert.True(idx >= 0);
        Assert.Equal("16", args[idx + 1]);
    }

    [Fact]
    public void BuildAria2cArgs_ConnectionsClamped_To1Min()
    {
        var args = DownloadEngine.BuildAria2cArgs("https://example.com/file.zip", connections: 0);
        var idx  = Array.IndexOf(args, "--split");
        Assert.True(idx >= 0);
        Assert.Equal("1", args[idx + 1]);
    }

    [Fact]
    public void BuildAria2cArgs_SelectedFiles_AddsSelectFileFlag()
    {
        var args = DownloadEngine.BuildAria2cArgs("magnet:?xt=urn:btih:abc", selectedFiles: "1,3,5");
        Assert.Contains("--select-file", args);
        var idx = Array.IndexOf(args, "--select-file");
        Assert.Equal("1,3,5", args[idx + 1]);
    }

    [Fact]
    public void BuildAria2cArgs_NoSelectedFiles_DoesNotAddSelectFileFlag()
    {
        var args = DownloadEngine.BuildAria2cArgs("magnet:?xt=urn:btih:abc");
        Assert.DoesNotContain("--select-file", args);
    }

    [Fact]
    public void BuildAria2cArgs_SpeedLimit_AddsMaxDownloadLimit()
    {
        var args = DownloadEngine.BuildAria2cArgs("https://example.com/f", speedLimitBytesPerSec: 512_000);
        Assert.Contains("--max-download-limit", args);
        var idx = Array.IndexOf(args, "--max-download-limit");
        Assert.Equal("512000", args[idx + 1]);
    }

    // ─────────────────────────────────────────────────────
    // ParseYtDlpProgress
    // ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("[download]  42.3% of ~123.45MiB at    1.23MiB/s ETA 00:12", 0.423, 1_289_748, 12)]
    [InlineData("[download]   5.0% of ~10.00MiB at  500.00KiB/s ETA 01:30",  0.050, 512_000,   90)]
    [InlineData("[download] 100.0% of ~1.00GiB at    2.00GiB/s ETA 00:00",   1.000, 2_147_483_648L, 0)]
    public void ParseYtDlpProgress_ValidLine_ReturnsCorrectValues(
        string line, double expectedProgress, long expectedSpeed, int expectedEta)
    {
        var result = DownloadEngine.ParseYtDlpProgress(line);

        Assert.NotNull(result);
        Assert.Equal(expectedProgress, result!.Progress, precision: 3);
        Assert.Equal(expectedEta, result.EtaSeconds);
        // Speed tolerance: within 1% due to floating-point multiplication
        Assert.InRange(result.SpeedBytesPerSec,
            (long)(expectedSpeed * 0.99), (long)(expectedSpeed * 1.01));
    }

    [Theory]
    [InlineData("[ffmpeg] Merging formats into output.mp4")]
    [InlineData("ERROR: Failed to download")]
    [InlineData("")]
    [InlineData("[download] Destination: video.mp4")]
    public void ParseYtDlpProgress_NonProgressLine_ReturnsNull(string line)
    {
        Assert.Null(DownloadEngine.ParseYtDlpProgress(line));
    }

    // ─────────────────────────────────────────────────────
    // ParseAria2cProgress
    // ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("[#abc123 1536MiB/3072MiB(50%) CN:8 DL:1.2MiB ETA:1m20s]", 0.50, 80)]
    [InlineData("[#def456 0B/1024B(0%) CN:1 DL:256B ETA:45s]",              0.00, 45)]
    [InlineData("[#ghi789 900MiB/1000MiB(90%) CN:4 DL:2.5MiB ETA:4s]",     0.90, 4)]
    public void ParseAria2cProgress_ValidLine_ReturnsCorrectValues(
        string line, double expectedProgress, int expectedEta)
    {
        var result = DownloadEngine.ParseAria2cProgress(line);

        Assert.NotNull(result);
        Assert.Equal(expectedProgress, result!.Progress, precision: 2);
        Assert.Equal(expectedEta, result.EtaSeconds);
    }

    [Theory]
    [InlineData("aria2c 1.37.0 (x86_64-pc-linux-gnu)")]
    [InlineData("")]
    [InlineData("Download complete")]
    public void ParseAria2cProgress_NonProgressLine_ReturnsNull(string line)
    {
        Assert.Null(DownloadEngine.ParseAria2cProgress(line));
    }

    // ─────────────────────────────────────────────────────
    // ParseEta (yt-dlp format: MM:SS or HH:MM:SS)
    // ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("00:00",  0)]
    [InlineData("00:30",  30)]
    [InlineData("01:30",  90)]
    [InlineData("10:00",  600)]
    [InlineData("01:00:00", 3600)]
    [InlineData("01:30:15", 5415)]
    public void ParseEta_ValidFormats_ReturnsCorrectSeconds(string eta, int expected)
    {
        Assert.Equal(expected, DownloadEngine.ParseEta(eta));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("--:--")]
    [InlineData("")]
    public void ParseEta_InvalidFormat_ReturnsNull(string eta)
    {
        Assert.Null(DownloadEngine.ParseEta(eta));
    }
}
