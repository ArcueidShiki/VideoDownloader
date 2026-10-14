using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

public sealed class DependencyUpdateLiveFactAttribute : FactAttribute
{
    public DependencyUpdateLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ANYDOWNLOADER_LIVE_UPDATES") != "1")
            Skip = "Set ANYDOWNLOADER_LIVE_UPDATES=1 and ANYDOWNLOADER_LIVE_YTDLP to verify official release checks.";
    }
}

[Collection("E2eSequential")]
public class DependencyUpdateLiveTests
{
    [DependencyUpdateLiveFact]
    public async Task OfficialReleaseMetadataAndRealBundledToolsPassStartupChecks()
    {
        var tools = Path.GetDirectoryName(Environment.GetEnvironmentVariable("ANYDOWNLOADER_LIVE_YTDLP"))!;
        var paths = DependencySpec.All.ToDictionary(s => s.Exe, s => Path.Combine(tools, s.Exe));
        var before = await DependencyUpdateStore.HashAsync(paths["yt-dlp.exe"], default);
        var root = Path.Combine(Path.GetTempPath(), "AnyDL_update_live_" + Guid.NewGuid().ToString("N"));
        try
        {
            var probe = new DependencyProbe();
            using var store = new DependencyUpdateStore(root, probe);
            Assert.True(store.AcquireSession());
            using var http = DependencyUpdater.CreateHttpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var result = await new DependencyUpdater(http, probe, store).CheckAndStageAsync(paths, timeout.Token);
            Assert.DoesNotContain("did not complete", result.Message);
            Assert.False(result.Necessary);
            Assert.Empty(store.ActivePaths());
            Assert.Equal(before, await DependencyUpdateStore.HashAsync(paths["yt-dlp.exe"], default));
            if (result.RestartRequired)
            {
                Assert.Contains("installed", await store.ActivateAtStartupAsync(false, timeout.Token));
                store.MarkHealthy();
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
