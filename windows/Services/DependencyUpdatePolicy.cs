using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnyDownloader.Windows.Services;

public sealed record DependencySpec(string Exe, string Repository, string Asset, bool Zip)
{
    // Automatic replacement is limited to the two isolated CLI executables in our x64 package.
    public static readonly DependencySpec YtDlp = new("yt-dlp.exe", "yt-dlp/yt-dlp", "yt-dlp.exe", false);
    public static readonly DependencySpec Deno = new("deno.exe", "denoland/deno", "deno-x86_64-pc-windows-msvc.zip", true);
    public static readonly DependencySpec[] All = [YtDlp, Deno];

    public Version ParseVersion(string value)
    {
        value = value.Trim().TrimStart('v');
        var pattern = Exe == YtDlp.Exe ? @"^\d{4}\.\d{2}\.\d{2}$" : @"^\d+\.\d+\.\d+$";
        if (!Regex.IsMatch(value, pattern) || !Version.TryParse(value, out var version))
            throw new InvalidDataException($"Invalid {Exe} version.");
        if (Exe == YtDlp.Exe && !DateTime.TryParseExact(value, "yyyy.MM.dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)) throw new InvalidDataException("Invalid yt-dlp release date.");
        return version;
    }

    public bool Supported(Version version) => Exe == Deno.Exe
        ? version.Major == 2 && version >= new Version(2, 3, 0)
        : true; // yt-dlp compatibility is checked against the CLI options actually used.
}

public sealed record DependencyRelease(DependencySpec Spec, string Version, string Url, string Sha256, long Size)
{
    public static DependencyRelease Parse(DependencySpec spec, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("Only stable published releases are accepted.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = spec.ParseVersion(tag);
        if (!spec.Supported(version)) throw new InvalidDataException($"{spec.Exe} needs a reviewed app release for this version.");
        var matches = root.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == spec.Asset).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Expected one x64 {spec.Asset} release asset.");
        var asset = matches[0];
        var url = asset.GetProperty("browser_download_url").GetString() ?? "";
        var expected = $"https://github.com/{spec.Repository}/releases/download/{tag}/{spec.Asset}";
        if (url != expected) throw new InvalidDataException("Unexpected release asset origin or path.");
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Release has no usable SHA-256 digest; keeping installed tools.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 250_000_000) throw new InvalidDataException("Unexpected dependency download size.");
        return new(spec, tag.TrimStart('v'), url, digest[7..], size);
    }
}

public interface IDependencyProbe
{
    Task<string> VersionAsync(DependencySpec spec, string path, CancellationToken ct);
    Task ValidateAsync(DependencySpec spec, string path, string version, CancellationToken ct);
}

public sealed class DependencyProbe : IDependencyProbe
{
    public async Task<string> VersionAsync(DependencySpec spec, string path, CancellationToken ct)
    {
        var output = await RunAsync(path, ["--version"], ct);
        var first = output.Split('\n')[0].Trim();
        if (spec == DependencySpec.Deno) first = first.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
        spec.ParseVersion(first);
        return first;
    }

    public async Task ValidateAsync(DependencySpec spec, string path, string version, CancellationToken ct)
    {
        ValidateX64Executable(path);
        var observed = await VersionAsync(spec, path, ct);
        if (spec.ParseVersion(observed) != spec.ParseVersion(version) || !spec.Supported(spec.ParseVersion(observed)))
            throw new InvalidDataException($"{spec.Exe} version/compatibility check failed.");
        if (spec == DependencySpec.YtDlp)
        {
            var help = await RunAsync(path, ["--ignore-config", "--help"], ct);
            foreach (var flag in new[] { "--js-runtimes", "--progress-template", "--output-na-placeholder", "--print" })
                if (!help.Contains(flag, StringComparison.Ordinal)) throw new InvalidDataException($"yt-dlp lacks required option {flag}.");
        }
    }

    public static void ValidateX64Executable(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 128 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Not a Windows executable.");
        stream.Position = 0x3c;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > stream.Length - 6) throw new InvalidDataException("Invalid executable header.");
        stream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664)
            throw new InvalidDataException("Dependency architecture is not Windows x64.");
    }

    private static async Task<string> RunAsync(string path, string[] args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var psi = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var proc = Process.Start(psi) ?? throw new IOException("Could not start dependency check.");
        var output = proc.StandardOutput.ReadToEndAsync();
        var error = proc.StandardError.ReadToEndAsync();
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch (InvalidOperationException) { }
            await proc.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw;
        }
        await Task.WhenAll(output, error);
        if (proc.ExitCode != 0) throw new InvalidDataException($"{Path.GetFileName(path)} failed its startup check ({proc.ExitCode}).");
        return await output;
    }
}
