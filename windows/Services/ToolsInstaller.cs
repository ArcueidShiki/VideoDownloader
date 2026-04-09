using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Downloads and installs yt-dlp, ffmpeg, ffprobe, and aria2c on first launch.
///
/// Detects the system CPU architecture and fetches the correct binary variant.
/// All files are installed to %LOCALAPPDATA%\AnyDownloader\bin\ so they are
/// available to BinaryResolver without requiring admin privileges.
///
/// Mirrors macOS DependencyChecker's one-time setup flow.
/// </summary>
public sealed class ToolsInstaller
{
    // ─────────────────────────────────────────────────────
    // Destination directory
    // ─────────────────────────────────────────────────────

    public static readonly string BinDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AnyDownloader", "bin");

    // ─────────────────────────────────────────────────────
    // System info
    // ─────────────────────────────────────────────────────

    /// <summary>CPU architecture of the running process.</summary>
    public static Architecture ProcessArch => RuntimeInformation.ProcessArchitecture;

    /// <summary>Human-readable architecture label, e.g. "x64", "x86", "arm64".</summary>
    public static string ArchLabel => ProcessArch switch
    {
        Architecture.X64  => "x64",
        Architecture.X86  => "x86",
        Architecture.Arm64 => "arm64",
        _                 => "x64",
    };

    /// <summary>Windows version string, e.g. "Windows 11 (10.0.22631)".</summary>
    public static string OsDescription => RuntimeInformation.OSDescription;

    // ─────────────────────────────────────────────────────
    // Tool descriptor
    // ─────────────────────────────────────────────────────

    public sealed record ToolInfo(
        string Name,           // "yt-dlp", "ffmpeg", "ffprobe", "aria2c"
        string ExeName,        // target filename on disk
        bool   IsInstalled,
        string? InstalledPath);

    // ─────────────────────────────────────────────────────
    // Status check
    // ─────────────────────────────────────────────────────

    /// <summary>Returns the current installation status of each required tool.</summary>
    public static ToolInfo[] GetStatus() => new[]
    {
        Check("yt-dlp",  "yt-dlp.exe"),
        Check("ffmpeg",  "ffmpeg.exe"),
        Check("ffprobe", "ffprobe.exe"),
        Check("aria2c",  "aria2c.exe"),
    };

    /// <summary>Returns true when all required tools are present.</summary>
    public static bool AllInstalled()
    {
        foreach (var t in GetStatus())
            if (!t.IsInstalled) return false;
        return true;
    }

    private static ToolInfo Check(string name, string exe)
    {
        // Check bundled tools\ first, then user bin dir, then PATH
        var path = BinaryResolver.FindYtDlp()  is string y && Path.GetFileName(y) == exe ? y
                 : BinaryResolver.FindFfmpeg()  is string f && Path.GetFileName(f) == exe ? f
                 : BinaryResolver.FindFfprobe() is string fp && Path.GetFileName(fp) == exe ? fp
                 : BinaryResolver.FindAria2c()  is string a && Path.GetFileName(a) == exe ? a
                 : File.Exists(Path.Combine(BinDir, exe)) ? Path.Combine(BinDir, exe)
                 : null;

        return new ToolInfo(name, exe, path is not null, path);
    }

    // ─────────────────────────────────────────────────────
    // Installation
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Downloads and installs all missing tools.
    /// Reports progress via the provided callbacks.
    /// </summary>
    public static async Task InstallAllAsync(
        IProgress<ToolInstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(BinDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "AnyDownloader/1.0");
        http.Timeout = TimeSpan.FromMinutes(10);

        await InstallYtDlpAsync(http, progress, ct);
        await InstallFfmpegAsync(http, progress, ct);
        await InstallAria2cAsync(http, progress, ct);
    }

    // ─────────────────────────────────────────────────────
    // yt-dlp
    // ─────────────────────────────────────────────────────

    private static async Task InstallYtDlpAsync(
        HttpClient http,
        IProgress<ToolInstallProgress>? progress,
        CancellationToken ct)
    {
        const string tool = "yt-dlp";
        if (File.Exists(Path.Combine(BinDir, "yt-dlp.exe")))
        {
            progress?.Report(new(tool, ToolInstallState.AlreadyInstalled, 1.0));
            return;
        }

        // yt-dlp provides direct per-arch downloads via GitHub releases/latest
        var exeName = ProcessArch switch
        {
            Architecture.X86  => "yt-dlp_x86.exe",
            Architecture.Arm64 => "yt-dlp_arm64.exe",
            _                  => "yt-dlp.exe",      // x64 default
        };

        var url = $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{exeName}";
        progress?.Report(new(tool, ToolInstallState.Downloading, 0.0));

        var dest = Path.Combine(BinDir, "yt-dlp.exe");
        await DownloadFileAsync(http, url, dest, p =>
            progress?.Report(new(tool, ToolInstallState.Downloading, p)), ct);

        progress?.Report(new(tool, ToolInstallState.Done, 1.0));
    }

    // ─────────────────────────────────────────────────────
    // ffmpeg + ffprobe (BtbN GPL builds)
    // ─────────────────────────────────────────────────────

    private static async Task InstallFfmpegAsync(
        HttpClient http,
        IProgress<ToolInstallProgress>? progress,
        CancellationToken ct)
    {
        // ffmpeg and ffprobe ship together in the same BtbN ZIP —
        // both rows must receive progress updates throughout.
        bool ffmpegOk  = File.Exists(Path.Combine(BinDir, "ffmpeg.exe"));
        bool ffprobeOk = File.Exists(Path.Combine(BinDir, "ffprobe.exe"));

        if (ffmpegOk)  progress?.Report(new("ffmpeg",  ToolInstallState.AlreadyInstalled, 1.0));
        if (ffprobeOk) progress?.Report(new("ffprobe", ToolInstallState.AlreadyInstalled, 1.0));

        if (ffmpegOk && ffprobeOk) return;

        // BtbN GPL nightly builds — win64 or win-arm64
        var archSuffix = ProcessArch == Architecture.Arm64 ? "win-arm64" : "win64";
        var zipName    = $"ffmpeg-master-latest-{archSuffix}-gpl.zip";
        var url        = $"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/{zipName}";

        if (!ffmpegOk)  progress?.Report(new("ffmpeg",  ToolInstallState.Downloading, 0.0));
        if (!ffprobeOk) progress?.Report(new("ffprobe", ToolInstallState.Downloading, 0.0));

        var zipPath = Path.Combine(Path.GetTempPath(), zipName);

        await DownloadFileAsync(http, url, zipPath, p =>
        {
            if (!ffmpegOk)  progress?.Report(new("ffmpeg",  ToolInstallState.Downloading, p * 0.8));
            if (!ffprobeOk) progress?.Report(new("ffprobe", ToolInstallState.Downloading, p * 0.8));
        }, ct);

        if (!ffmpegOk)  progress?.Report(new("ffmpeg",  ToolInstallState.Extracting, 0.85));
        if (!ffprobeOk) progress?.Report(new("ffprobe", ToolInstallState.Extracting, 0.85));

        var toExtract = new List<string>();
        if (!ffmpegOk)  toExtract.Add("ffmpeg.exe");
        if (!ffprobeOk) toExtract.Add("ffprobe.exe");

        await ExtractFromZipAsync(zipPath, BinDir, toExtract.ToArray(), ct);

        try { File.Delete(zipPath); } catch { /* best-effort cleanup */ }

        if (!ffmpegOk)  progress?.Report(new("ffmpeg",  ToolInstallState.Done, 1.0));
        if (!ffprobeOk) progress?.Report(new("ffprobe", ToolInstallState.Done, 1.0));
    }

    // ─────────────────────────────────────────────────────
    // aria2c
    // ─────────────────────────────────────────────────────

    private static async Task InstallAria2cAsync(
        HttpClient http,
        IProgress<ToolInstallProgress>? progress,
        CancellationToken ct)
    {
        const string tool = "aria2c";
        if (File.Exists(Path.Combine(BinDir, "aria2c.exe")))
        {
            progress?.Report(new(tool, ToolInstallState.AlreadyInstalled, 1.0));
            return;
        }

        // Get latest release info from GitHub API
        progress?.Report(new(tool, ToolInstallState.ResolvingVersion, 0.05));

        var apiUrl  = "https://api.github.com/repos/aria2/aria2/releases/latest";
        var apiJson = await http.GetStringAsync(apiUrl, ct);

        var zipUrl = ParseAria2cDownloadUrl(apiJson, ProcessArch);
        if (zipUrl is null)
        {
            // Fallback: aria2 doesn't have arm64 native; use x64 (runs under emulation)
            zipUrl = ParseAria2cDownloadUrl(apiJson, Architecture.X64);
        }

        if (zipUrl is null)
            throw new InvalidOperationException("Could not determine aria2c download URL.");

        progress?.Report(new(tool, ToolInstallState.Downloading, 0.1));
        var zipName = Path.GetFileName(new Uri(zipUrl).AbsolutePath);
        var zipPath = Path.Combine(Path.GetTempPath(), zipName);

        await DownloadFileAsync(http, zipUrl, zipPath, p =>
            progress?.Report(new(tool, ToolInstallState.Downloading, 0.1 + p * 0.8)), ct);

        progress?.Report(new(tool, ToolInstallState.Extracting, 0.92));
        await ExtractFromZipAsync(zipPath, BinDir, new[] { "aria2c.exe" }, ct);

        try { File.Delete(zipPath); } catch { }

        progress?.Report(new(tool, ToolInstallState.Done, 1.0));
    }

    /// <summary>Parses the GitHub releases API JSON to find the right aria2c zip URL.</summary>
    internal static string? ParseAria2cDownloadUrl(string apiJson, Architecture arch)
    {
        var archTag = arch switch
        {
            Architecture.X86  => "win-32bit",
            Architecture.Arm64 => "win-64bit", // use x64 build for ARM64 emulation
            _                  => "win-64bit",
        };

        using var doc  = JsonDocument.Parse(apiJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("assets", out var assets)) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameProp)) continue;
            var name = nameProp.GetString() ?? string.Empty;

            if (name.Contains(archTag, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return asset.TryGetProperty("browser_download_url", out var urlProp)
                    ? urlProp.GetString()
                    : null;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────

    private static async Task DownloadFileAsync(
        HttpClient http,
        string url,
        string dest,
        Action<double>? onProgress,
        CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total   = response.Content.Headers.ContentLength ?? -1;
        var received = 0L;

        await using var stream  = await response.Content.ReadAsStreamAsync(ct);
        await using var outFile = File.Create(dest);

        var buffer = new byte[65_536];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await outFile.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            received += bytesRead;
            if (total > 0)
                onProgress?.Invoke((double)received / total);
        }
    }

    private static Task ExtractFromZipAsync(
        string zipPath,
        string destDir,
        string[] targetFiles,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var targets   = new HashSet<string>(targetFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(entry.FullName);
                if (!targets.Contains(fileName)) continue;

                var dest = Path.Combine(destDir, fileName);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }, ct);
    }
}

// ─────────────────────────────────────────────────────
// Progress types
// ─────────────────────────────────────────────────────

public enum ToolInstallState
{
    ResolvingVersion,
    Downloading,
    Extracting,
    Done,
    AlreadyInstalled,
    Failed,
}

public sealed record ToolInstallProgress(
    string   ToolName,
    ToolInstallState State,
    double   Progress);  // 0.0 – 1.0
