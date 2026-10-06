using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

public class DependencyUpdaterTests
{
    [Fact]
    public async Task NoUpdateDoesNotStageOrRequireRestart()
    {
        using var f = new Fixture();
        f.LatestYt = Fixture.OldYt;
        var result = await f.Check();
        Assert.False(result.RestartRequired);
        Assert.False(result.Necessary);
        Assert.Contains("up to date", result.Message);
        Assert.Null(f.Store.Pending);
    }

    [Fact]
    public async Task OrdinaryUpdateIsStagedUntilIdleRestartAndCanRollBack()
    {
        using var f = new Fixture();
        var result = await f.Check();
        Assert.True(result.RestartRequired);
        Assert.False(result.Necessary);
        Assert.Empty(f.Store.ActivePaths());
        Assert.Equal(Fixture.OldYt, File.ReadAllText(f.Paths["yt-dlp.exe"]));
        await f.Store.ActivateAtStartupAsync(hasActiveJobs: true);
        Assert.Empty(f.Store.ActivePaths());
        Assert.NotNull(f.Store.Pending);
        await f.Store.ActivateAtStartupAsync(false);
        Assert.Equal(f.LatestYt, File.ReadAllText(f.Store.ActivePaths()["yt-dlp.exe"]));
        Assert.Null(f.Store.Pending);
        f.Store.MarkHealthy();
        f.Store.RequestRollback();
        // Requesting rollback also waits for a restart.
        Assert.NotEmpty(f.Store.ActivePaths());
        await f.Store.ActivateAtStartupAsync(false);
        f.Store.MarkHealthy();
        Assert.Empty(f.Store.ActivePaths());
        var afterRollback = await f.Check();
        Assert.False(afterRollback.RestartRequired);
        Assert.Contains("previously rolled back", afterRollback.Message);
    }

    [Fact]
    public async Task MissingDependencyMakesRepairNecessaryButDoesNotSwitchLiveTools()
    {
        using var f = new Fixture();
        File.Delete(f.Paths["deno.exe"]);
        var result = await f.Check();
        Assert.True(result.Necessary);
        Assert.True(result.RestartRequired);
        Assert.True(f.Store.Pending!.Required);
        Assert.Empty(f.Store.ActivePaths());
    }

    [Fact]
    public async Task RuntimeBelowDocumentedMinimumNeedsRepair()
    {
        using var f = new Fixture();
        File.WriteAllText(f.Paths["deno.exe"], "2.2.0");
        var result = await f.Check();
        Assert.True(result.Necessary);
        Assert.True(result.RestartRequired);
    }

    [Fact]
    public async Task RollbackRetainsPreviousManagedGeneration()
    {
        using var f = new Fixture();
        await f.Check();
        await f.Store.ActivateAtStartupAsync(false);
        f.Store.MarkHealthy();
        var first = f.Store.Active!.Generation;
        f.LatestYt = "2026.08.21";
        await f.Check();
        await f.Store.ActivateAtStartupAsync(false);
        f.Store.MarkHealthy();
        var second = f.Store.Active!.Generation;
        Assert.NotEqual(first, second);
        f.Store.RequestRollback();
        await f.Store.ActivateAtStartupAsync(false);
        f.Store.MarkHealthy();
        Assert.Equal(first, f.Store.Active!.Generation);
        Assert.True(Directory.Exists(f.Store.GenerationPath(second!)));
        Assert.Equal("2026.08.20", File.ReadAllText(f.Store.ActivePaths()["yt-dlp.exe"]));
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("malformed")]
    [InlineData("digest")]
    [InlineData("truncated")]
    [InlineData("interrupted")]
    [InlineData("redirect")]
    [InlineData("smoke")]
    [InlineData("archive")]
    public async Task FailedCheckOrDownloadPreservesWorkingFiles(string fault)
    {
        using var f = new Fixture { Fault = fault };
        var result = await f.Check();
        Assert.False(result.RestartRequired);
        Assert.False(result.Necessary);
        Assert.Contains("existing files were kept", result.Message);
        Assert.Null(f.Store.Pending);
        Assert.Empty(f.Store.ActivePaths());
        Assert.Equal(Fixture.OldYt, File.ReadAllText(f.Paths["yt-dlp.exe"]));
        var staging = Path.Combine(f.Root, "staging");
        Assert.True(!Directory.Exists(staging) || !Directory.EnumerateFileSystemEntries(staging).Any());
        if (fault is "digest" or "truncated" or "interrupted" or "redirect")
            Assert.DoesNotContain(f.Probe.Validated, p => p.Contains("staging"));
    }

    [Fact]
    public async Task CancelledDownloadDoesNotLeavePendingInstall()
    {
        using var f = new Fixture();
        using var ct = new CancellationTokenSource();
        ct.Cancel();
        var result = await f.Check(ct.Token);
        Assert.False(result.RestartRequired);
        Assert.Null(f.Store.Pending);
    }

    [Fact]
    public async Task NextStartupCleansAnInterruptedPrivateStagingDirectory()
    {
        using var f = new Fixture();
        var partial = Path.Combine(f.Root, "staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(partial);
        File.WriteAllText(Path.Combine(partial, "yt-dlp.exe.download"), "partial");
        f.ReopenStore();
        await f.Store.ActivateAtStartupAsync(false);
        Assert.False(Directory.Exists(partial));
        Assert.Equal(Fixture.OldYt, File.ReadAllText(f.Paths["yt-dlp.exe"]));
    }

    [Fact]
    public async Task UnhealthyActivationAutomaticallyRestoresPreviousVersion()
    {
        using var f = new Fixture();
        await f.Check();
        await f.Store.ActivateAtStartupAsync(false);
        Assert.False(f.Store.Active!.Healthy);
        f.ReopenStore(); // simulate process exiting/crashing before marking the window healthy
        var message = await f.Store.ActivateAtStartupAsync(false);
        Assert.Contains("interrupted activation", message);
        Assert.Empty(f.Store.ActivePaths());
        Assert.Null(f.Store.Pending);
    }

    [Fact]
    public async Task InterruptedPointerReplacementLeavesBundledToolsUsable()
    {
        using var f = new Fixture();
        await f.Check();
        Directory.CreateDirectory(Path.Combine(f.Root, "active.json")); // force an actual file replacement failure
        var result = await f.Store.ActivateAtStartupAsync(false);
        Assert.Contains("could not be installed", result);
        Assert.Empty(f.Store.ActivePaths());
        Assert.Equal(Fixture.OldYt, File.ReadAllText(f.Paths["yt-dlp.exe"]));
    }

    [Fact]
    public async Task TamperedStagingIsRejectedAgainAtStartup()
    {
        using var f = new Fixture();
        await f.Check();
        File.AppendAllText(Path.Combine(f.Store.GenerationPath(f.Store.Pending!.Generation!), "yt-dlp.exe"), "tampered");
        var result = await f.Store.ActivateAtStartupAsync(false);
        Assert.Contains("changed after verification", result);
        Assert.Empty(f.Store.ActivePaths());
    }

    [Fact]
    public async Task SecondAppInstanceCannotActivateOrStageUpdates()
    {
        using var f = new Fixture();
        await f.Check();
        using var second = new DependencyUpdateStore(f.Root, f.Probe);
        Assert.False(second.AcquireSession());
        await second.ActivateAtStartupAsync(false);
        Assert.Empty(f.Store.ActivePaths());
        var result = await new DependencyUpdater(f.Http, f.Probe, second).CheckAndStageAsync(f.Paths);
        Assert.Contains("Another", result.Message);
    }

    [Theory]
    [InlineData("http://github.com/yt-dlp/yt-dlp")]
    [InlineData("https://github.com.attacker.example/file")]
    [InlineData("https://github.com:444/file")]
    [InlineData("https://user:pass@github.com/file")]
    public void RejectsUntrustedTransport(string url) => Assert.False(DependencyUpdater.AllowedDownloadHost(new Uri(url)));

    [Fact]
    public void MetadataCannotRedirectToAnotherRepositoryOrOptIntoUnreviewedMajorRuntime()
    {
        using var f = new Fixture();
        var json = f.Metadata(DependencySpec.YtDlp).Replace("yt-dlp/yt-dlp/releases", "attacker/tool/releases");
        Assert.Throws<InvalidDataException>(() => DependencyRelease.Parse(DependencySpec.YtDlp, json));
        json = f.Metadata(DependencySpec.Deno).Replace("2.9.7", "3.0.0");
        Assert.Throws<InvalidDataException>(() => DependencyRelease.Parse(DependencySpec.Deno, json));
        Assert.Throws<InvalidDataException>(() => f.Store.GenerationPath("../../other"));
    }

    [Fact]
    public void ExecutableArchitectureIsVerifiedBeforeExecution()
    {
        using var f = new Fixture();
        var path = Path.Combine(f.Root, "wrong.exe");
        var bytes = new byte[256];
        bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 0x80;
        bytes[0x80] = 0x50; bytes[0x81] = 0x45;
        bytes[0x84] = 0x4c; bytes[0x85] = 0x01; // x86, not x64
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => DependencyProbe.ValidateX64Executable(path));
        bytes[0x84] = 0x64; bytes[0x85] = 0x86;
        File.WriteAllBytes(path, bytes);
        DependencyProbe.ValidateX64Executable(path);
    }

    private sealed class FakeProbe : IDependencyProbe
    {
        public List<string> Validated { get; } = new();
        public Func<bool> RejectNew { get; set; } = () => false;
        public Task<string> VersionAsync(DependencySpec spec, string path, CancellationToken ct)
            => Task.FromResult(File.ReadAllText(path));
        public Task ValidateAsync(DependencySpec spec, string path, string version, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Validated.Add(path);
            if (File.ReadAllText(path) != version || !spec.Supported(spec.ParseVersion(version)) ||
                (path.Contains("staging") && RejectNew())) throw new InvalidDataException("Smoke test failed.");
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public const string OldYt = "2026.08.19";
        public string LatestYt = "2026.08.20";
        public string Fault = "";
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "AnyDL_update_test_" + Guid.NewGuid().ToString("N"));
        public Dictionary<string, string> Paths { get; } = new();
        public FakeProbe Probe { get; } = new();
        public DependencyUpdateStore Store { get; private set; }
        public HttpClient Http { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            foreach (var spec in DependencySpec.All)
            {
                var path = Path.Combine(Root, spec.Exe);
                File.WriteAllText(path, spec == DependencySpec.YtDlp ? OldYt : "2.9.7");
                Paths[spec.Exe] = path;
            }
            Probe.RejectNew = () => Fault == "smoke";
            Store = new(Root, Probe); Assert.True(Store.AcquireSession());
            Http = new(new Handler(this));
        }
        public void ReopenStore() { Store.Dispose(); Store = new(Root, Probe); Assert.True(Store.AcquireSession()); }
        public Task<DependencyUpdateResult> Check(CancellationToken ct = default)
            => new DependencyUpdater(Http, Probe, Store).CheckAndStageAsync(Paths, ct);
        public byte[] Payload(DependencySpec spec)
        {
            if (!spec.Zip) return Encoding.UTF8.GetBytes(LatestYt);
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                var entry = zip.CreateEntry(Fault == "archive" ? "../deno.exe" : "deno.exe");
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(Fault == "archive" ? "2.9.8" : "2.9.7");
            }
            return stream.ToArray();
        }
        public string Metadata(DependencySpec spec)
        {
            var payload = Payload(spec);
            var version = spec == DependencySpec.YtDlp ? LatestYt : Fault == "archive" ? "v2.9.8" : "v2.9.7";
            return JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = version, assets = new[] {
                new { name = spec.Asset, browser_download_url = $"https://github.com/{spec.Repository}/releases/download/{version}/{spec.Asset}",
                    digest = "sha256:" + (Fault == "digest" ? new string('0',64) : Convert.ToHexString(SHA256.HashData(payload))), size = payload.Length }
            } });
        }
        public void Dispose() { Http.Dispose(); Store.Dispose(); Directory.Delete(Root, true); }
        private sealed class Handler(Fixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (fixture.Fault == "offline") throw new HttpRequestException("Offline.");
                var spec = request.RequestUri!.AbsolutePath.Contains("yt-dlp") ? DependencySpec.YtDlp : DependencySpec.Deno;
                if (request.RequestUri.Host == "api.github.com")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixture.Fault == "malformed" ? "{}" : fixture.Metadata(spec)) });
                if (fixture.Fault == "redirect")
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = new Uri("https://attacker.example/tool.exe");
                    return Task.FromResult(redirect);
                }
                if (fixture.Fault == "interrupted") throw new IOException("Connection interrupted during download.");
                var bytes = fixture.Payload(spec);
                if (fixture.Fault == "truncated") bytes = bytes[..3];
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
        }
    }
}
