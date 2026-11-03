using System.Diagnostics;
using System.Text.Json;
using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

public class YtDlpIntegrationTests
{
    [Fact]
    public void StructuredProgressHandlesMissingValuesAndByteSpeeds()
    {
        var p = DownloadEngine.ParseYtDlpProgress(YtDlpIntegration.ProgressPrefix +
            "{\"downloaded\":50,\"total\":null,\"estimate\":100,\"speed\":12,\"eta\":null}");
        Assert.NotNull(p);
        Assert.Equal(.5, p.Progress);
        Assert.Equal(12, p.SpeedBytesPerSec);
        Assert.Null(p.EtaSeconds);
        Assert.Null(DownloadEngine.ParseYtDlpProgress(YtDlpIntegration.ProgressPrefix + "null"));
        Assert.Null(DownloadEngine.ParseYtDlpProgress(YtDlpIntegration.ProgressPrefix + "{broken"));
    }

    [Fact]
    public void CompletedPathIsStructuredAndPreservesUnicode()
    {
        var path = @"C:\Downloads\测试 雪.mp4";
        var output = new YtDlpOutput();
        output.Observe("[download] Destination: wrong.partial.mp4");
        Assert.Null(output.FinalPath);
        output.Observe(YtDlpIntegration.FilePrefix + JsonSerializer.Serialize(path));
        Assert.Equal(path, output.FinalPath);
    }

    [Fact]
    public void ErrorsPreserveCauseAndProvideDependencyGuidance()
    {
        var output = new YtDlpOutput();
        output.Observe("ERROR: ffmpeg not found");
        output.Observe("WARNING: unrelated later warning");
        Assert.Contains("Install ffmpeg and ffprobe", output.Error(1));
        Assert.Contains("ERROR: ffmpeg not found", output.Error(1));
        Assert.Contains("Update", YtDlpIntegration.FailureMessage(2, "no such option: --js-runtimes"));
    }

    [Fact]
    public void InvocationKeepsUrlOneArgumentAndMakesMergeErrorsFatal()
    {
        var url = "https://example.com/watch?v=test&list=another";
        var args = DownloadEngine.BuildYtDlpArgs(url);
        Assert.Equal(url, args[^1]);
        Assert.Equal("--", args[^2]);
        Assert.Contains("--abort-on-error", args);
        Assert.Contains("after_move:" + YtDlpIntegration.FilePrefix + "%(filepath)j", args);
        Assert.Equal("null", args[Array.IndexOf(args, "--output-na-placeholder") + 1]);
        var psi = new ProcessStartInfo("yt-dlp.exe") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        YtDlpIntegration.Configure(psi);
        Assert.Contains("--ignore-config", psi.ArgumentList);
        Assert.Equal(url, psi.ArgumentList[^1]);
        Assert.Equal("utf-8", psi.StandardOutputEncoding?.WebName);
    }

    [Fact]
    public async Task CancelThenRetryWaitsForOldWriterAndRejectsStaleCallbacks()
    {
        var runs = new YtDlpRunTracker();
        var id = Guid.NewGuid();
        var first = await runs.BeginAsync(id);
        var exit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runs.Track(id, first, exit.Task);
        runs.Cancel(id);
        var retry = runs.BeginAsync(id);
        Assert.False(retry.IsCompleted);
        Assert.False(runs.IsCurrent(id, first));
        exit.SetResult();
        var second = await retry;
        Assert.True(runs.IsCurrent(id, second));
        runs.Cancel(id);
        Assert.False(runs.IsCurrent(id, second));
    }
}
