using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

public sealed class YtDlpLiveFactAttribute : FactAttribute
{
    public YtDlpLiveFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ANYDOWNLOADER_LIVE_YTDLP")))
            Skip = "Set ANYDOWNLOADER_LIVE_YTDLP to opt into public-media integration tests.";
    }
}

[Collection("E2eSequential")]
public class YtDlpLiveTests
{
    private static string Exe => Environment.GetEnvironmentVariable("ANYDOWNLOADER_LIVE_YTDLP")!;
    private const string Sample = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4";

    [YtDlpLiveFact]
    public async Task PublicCc0MetadataAndDownloadProduceCompleteFile()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var info = await MetadataService.FetchVideoInfoAsync(Sample, Exe, timeout.Token);
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.Title));
        var directory = Path.Combine(Path.GetTempPath(), "AnyDL_native_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = new YtDlpOutput();
            var progress = new List<YtDlpProgressUpdate>();
            var args = DownloadEngine.BuildYtDlpArgs(Sample, "best", outputDir: directory);
            var (proc, exit) = DownloadEngine.StartYtDlpProcess(Exe, args, line =>
            {
                output.Observe(line);
                var update = DownloadEngine.ParseYtDlpProgress(line);
                if (update is not null) lock (progress) progress.Add(update);
            });
            using (proc)
            using (timeout.Token.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } }))
            {
                Assert.Equal(0, await exit);
                Assert.NotNull(output.FinalPath);
                var data = await File.ReadAllBytesAsync(output.FinalPath!);
                Assert.True(data.Length > 100_000);
                Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(data, 4, 4));
                Assert.Contains(progress, p => p.Progress == 1);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [YtDlpLiveFact]
    public async Task UnavailableVideoReturnsExtractorErrorRatherThanNullMetadata()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MetadataService.FetchVideoInfoAsync(
            "https://www.youtube.com/watch?v=BaW_jenozKc", Exe, timeout.Token));
        Assert.Contains("yt-dlp exited with code", error.Message);
        Assert.Contains("ERROR:", error.Message);
    }

    [YtDlpLiveFact]
    public async Task CancelledMetadataTerminatesAndNextFetchWorks()
    {
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            MetadataService.FetchVideoInfoAsync(Sample, Exe, cancelled.Token));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Assert.NotNull(await MetadataService.FetchVideoInfoAsync(Sample, Exe, timeout.Token));
    }
}
