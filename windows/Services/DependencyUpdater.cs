using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AnyDownloader.Windows.Services;

public sealed record DependencyUpdateResult(string Message, bool RestartRequired = false, bool Necessary = false);

/// <summary>Download-only while the app is running; activation belongs to the next startup.</summary>
public sealed class DependencyUpdater
{
    private readonly HttpClient _http;
    private readonly IDependencyProbe _probe;
    private readonly DependencyUpdateStore _store;
    public DependencyUpdater(HttpClient http, IDependencyProbe probe, DependencyUpdateStore store)
        { _http = http; _probe = probe; _store = store; }

    public static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(4) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AnyDownloader/1.1");
        return http;
    }

    public async Task<DependencyUpdateResult> CheckAndStageAsync(IReadOnlyDictionary<string, string> current,
        CancellationToken ct = default)
    {
        if (!_store.OwnsSession) return new("Another AnyDownloader window manages updates. Current dependencies remain available.");
        var necessary = false;
        string? staging = null;
        try
        {
            if (_store.Pending is { } pending)
                return new("Verified dependencies are ready. Restart to install them.", true, pending.Required);
            var versions = new Dictionary<string, string>();
            var repair = new HashSet<string>();
            foreach (var spec in DependencySpec.All)
            {
                try
                {
                    if (!current.TryGetValue(spec.Exe, out var path) || !File.Exists(path))
                        throw new FileNotFoundException($"{spec.Exe} is missing.");
                    var version = await _probe.VersionAsync(spec, path, ct);
                    await _probe.ValidateAsync(spec, path, version, ct);
                    versions[spec.Exe] = version;
                }
                catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
                {
                    repair.Add(spec.Exe);
                    necessary = true;
                }
            }
            var releases = new List<DependencyRelease>();
            var rejected = false;
            foreach (var spec in DependencySpec.All)
            {
                var json = await GetBytesAsync(new Uri($"https://api.github.com/repos/{spec.Repository}/releases/latest"), 2_000_000, ct);
                var release = DependencyRelease.Parse(spec, Encoding.UTF8.GetString(json));
                if (repair.Contains(spec.Exe) || spec.ParseVersion(release.Version) > spec.ParseVersion(versions[spec.Exe]))
                {
                    if (_store.WasRejected(spec.Exe, release.Version)) { rejected = true; continue; }
                    releases.Add(release);
                }
            }
            if (releases.Count == 0)
                return new(rejected ? "This update was previously rolled back. Keeping existing tools until a different release is available."
                    : "yt-dlp and Deno are up to date. FFmpeg, aria2 and app runtimes update with app releases.", false, necessary);
            if (repair.Any(exe => !releases.Any(r => r.Spec.Exe == exe)))
                return new("A required dependency could not be repaired safely. Install a reviewed app release.", false, true);

            var id = Guid.NewGuid().ToString("N");
            staging = Path.Combine(_store.Root, "staging", id);
            Directory.CreateDirectory(staging);
            var manifest = new List<StoredDependency>();
            foreach (var spec in DependencySpec.All)
            {
                ct.ThrowIfCancellationRequested();
                var dest = Path.Combine(staging, spec.Exe);
                var release = releases.SingleOrDefault(r => r.Spec == spec);
                var version = release?.Version ?? versions[spec.Exe];
                if (release is null) File.Copy(current[spec.Exe], dest);
                else
                {
                    var package = Path.Combine(staging, spec.Asset + ".download");
                    await DownloadAsync(release, package, ct);
                    if (!string.Equals(await DependencyUpdateStore.HashAsync(package, ct), release.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"{spec.Exe} download digest mismatch.");
                    if (spec.Zip)
                    {
                        using var zip = ZipFile.OpenRead(package);
                        // Extract only this exact, bounded entry; never trust archive paths.
                        var entries = zip.Entries.Where(e => e.FullName == spec.Exe).ToArray();
                        if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > 250_000_000)
                            throw new InvalidDataException("Unexpected dependency archive.");
                        entries[0].ExtractToFile(dest);
                    }
                    else File.Move(package, dest);
                    if (File.Exists(package)) File.Delete(package);
                }
                // All execution happens after digest verification (or copying our active tools).
                await _probe.ValidateAsync(spec, dest, version, ct);
                manifest.Add(new(spec.Exe, version, await DependencyUpdateStore.HashAsync(dest, ct)));
            }
            ct.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(new DependencyGeneration(id, manifest.ToArray())), ct);
            var generation = _store.GenerationPath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(generation)!);
            Directory.Move(staging, generation);
            staging = null;
            _store.SavePending(id, necessary);
            var names = string.Join(", ", releases.Select(r => $"{r.Spec.Exe[..^4]} {r.Version}"));
            return new(necessary ? $"Required dependency repair ready ({names}). Restart before video downloads."
                : $"Update ready ({names}). Restart when convenient; current downloads can continue.", true, necessary);
        }
        catch (Exception ex)
        {
            var reason = ex is OperationCanceledException ? "Check or download was interrupted." : ex.Message;
            return new((necessary ? "A required video dependency is missing or incompatible. " : "") +
                "Update check did not complete; existing files were kept. " + reason, false, necessary);
        }
        finally
        {
            // Only the private randomly-named staging directory created by this call is removed.
            if (staging is not null) { try { Directory.Delete(staging, true); } catch (IOException) { } }
        }
    }

    private async Task<byte[]> GetBytesAsync(Uri uri, long limit, CancellationToken ct)
    {
        using var response = await GetAsync(uri, ct);
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(ct), buffer, limit, ct);
        return buffer.ToArray();
    }

    private async Task DownloadAsync(DependencyRelease release, string path, CancellationToken ct)
    {
        using var response = await GetAsync(new Uri(release.Url), ct);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(ct), output, release.Size, ct);
        if (output.Length != release.Size) throw new InvalidDataException("Dependency download was incomplete.");
        await output.FlushAsync(ct);
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken ct)
    {
        for (int redirect = 0; redirect < 5; redirect++)
        {
            if (!AllowedDownloadHost(uri)) throw new InvalidDataException("Rejected dependency download redirect.");
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null) throw new InvalidDataException("Missing release redirect.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"Release service returned {(int)status}; try again later.");
            }
            return response;
        }
        throw new InvalidDataException("Too many release download redirects.");
    }

    internal static bool AllowedDownloadHost(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && uri.Host is "api.github.com" or "github.com" or
            "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";

    private static async Task CopyBoundedAsync(Stream source, Stream dest, long limit, CancellationToken ct)
    {
        await using (source)
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                total += read;
                if (total > limit) throw new InvalidDataException("Dependency response exceeded expected size.");
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
    }
}
