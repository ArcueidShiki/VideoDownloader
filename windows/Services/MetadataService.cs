using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Windows.Models;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Fetches metadata for video URLs (via yt-dlp --dump-json) and
/// torrent file listings (via aria2c -S).
/// Mirrors macOS URLInfoFetcher + TorrentEngine metadata logic.
/// </summary>
public static class MetadataService
{
    // ─────────────────────────────────────────────────────
    // Video metadata (yt-dlp)
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs yt-dlp --dump-json and parses the output into a VideoInfoModel.
    /// Throws with yt-dlp diagnostics when extraction fails.
    /// </summary>
    public static async Task<VideoInfoModel?> FetchVideoInfoAsync(
        string url,
        string ytDlpPath,
        CancellationToken ct = default)
    {
        var output = await RunProcessAsync(
            ytDlpPath,
            new[] { "--dump-single-json", "--no-playlist", "--quiet", "--", url },
            ct, isYtDlp: true);

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("yt-dlp returned no video metadata. Check the URL and update yt-dlp.");

        try
        {
            return ParseYtDlpJson(output);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("yt-dlp returned invalid video metadata. Update yt-dlp and try again.", ex);
        }
    }

    private static VideoInfoModel ParseYtDlpJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var title     = GetString(root, "title") ?? "Unknown";
        var thumbnail = GetString(root, "thumbnail");
        var uploader  = GetString(root, "uploader") ?? GetString(root, "channel");
        var duration  = root.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.Number
                            ? FormatDuration((int)dur.GetDouble())
                            : null;

        var videoFormats = new List<VideoFormat>();
        var audioFormats = new List<AudioFormat>();
        bool hasSeparateStreams = false;

        if (root.TryGetProperty("formats", out var fmtsEl) &&
            fmtsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fmtsEl.EnumerateArray())
            {
                var formatId = GetString(f, "format_id") ?? "unknown";
                var ext      = GetString(f, "ext") ?? "";
                var vcodec   = GetString(f, "vcodec") ?? "none";
                var acodec   = GetString(f, "acodec") ?? "none";

                // Skip storyboard or thumbnail formats
                if (vcodec.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                    acodec.Equals("none", StringComparison.OrdinalIgnoreCase))
                    continue;

                long? filesize = null;
                if (f.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number)
                    filesize = fs.GetInt64();
                else if (f.TryGetProperty("filesize_approx", out var fsa) && fsa.ValueKind == JsonValueKind.Number)
                    filesize = fsa.GetInt64();

                bool hasVideo = !vcodec.Equals("none", StringComparison.OrdinalIgnoreCase);
                bool hasAudio = !acodec.Equals("none", StringComparison.OrdinalIgnoreCase);

                if (hasVideo)
                {
                    int? height = null;
                    if (f.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number)
                        height = h.GetInt32();

                    var qualityLabel = height.HasValue ? $"{height}p" : (GetString(f, "format_note") ?? formatId);
                    var codec        = FormatVideoCodec(vcodec);

                    videoFormats.Add(new VideoFormat(formatId, qualityLabel, ext, codec, filesize, hasAudio));

                    if (!hasAudio) hasSeparateStreams = true;
                }
                else if (hasAudio)
                {
                    int abr = 0;
                    if (f.TryGetProperty("abr", out var ab) && ab.ValueKind == JsonValueKind.Number)
                        abr = (int)ab.GetDouble();
                    else if (f.TryGetProperty("tbr", out var tb) && tb.ValueKind == JsonValueKind.Number)
                        abr = (int)tb.GetDouble();

                    var label = abr > 0 ? $"{abr} kbps" : (GetString(f, "format_note") ?? formatId);
                    var codec = FormatAudioCodec(acodec);
                    audioFormats.Add(new AudioFormat(formatId, label, ext, abr, filesize));
                }
            }
        }

        // Sort video formats by resolution (highest first)
        videoFormats.Sort((a, b) =>
        {
            int ParseQ(string q) => int.TryParse(q.TrimEnd('p'), out var n) ? n : 0;
            return ParseQ(b.QualityLabel) - ParseQ(a.QualityLabel);
        });

        // Sort audio by bitrate (highest first)
        audioFormats.Sort((a, b) => b.BitrateKbps - a.BitrateKbps);

        return new VideoInfoModel(title, thumbnail, duration, uploader,
            videoFormats.ToArray(), audioFormats.ToArray(), hasSeparateStreams);
    }

    // ─────────────────────────────────────────────────────
    // Actual output filename resolution (yt-dlp)
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>yt-dlp --print filename --skip-download</c> to resolve the exact
    /// sanitized output path that yt-dlp would use — without downloading anything.
    ///
    /// yt-dlp applies its own platform-specific sanitization to <paramref name="outputTemplate"/>
    /// (e.g. stripping CJK / non-ASCII characters on some versions), so this is the only
    /// reliable way to predict the final file path. Throws if extraction fails.
    /// </summary>
    /// <param name="ytDlpPath">Path to yt-dlp executable.</param>
    /// <param name="format">Format selector, e.g. "f137" or "f137+f140".</param>
    /// <param name="outputTemplate">The --output template (may be a literal path).</param>
    /// <param name="url">Video URL.</param>
    public static async Task<string?> GetActualFilenameAsync(
        string ytDlpPath,
        string format,
        string outputTemplate,
        string url,
        CancellationToken ct = default)
    {
        var output = await RunProcessAsync(
            ytDlpPath,
            ["--print", "filename",
             "--skip-download",
             "--format",  $"{format}/best",
             "--output",  outputTemplate,
             "--no-playlist",
             "--", url],
            ct, isYtDlp: true);

        return string.IsNullOrWhiteSpace(output) ? null
            : output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
    }

    // ─────────────────────────────────────────────────────
    // Torrent file listing (aria2c -S)
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs aria2c -S on a local .torrent file and returns the file list.
    /// For magnet links, returns a single pseudo-entry (full download).
    /// </summary>
    public static async Task<TorrentFileEntry[]> FetchTorrentFilesAsync(
        string urlOrPath,
        string aria2cPath,
        CancellationToken ct = default)
    {
        // Magnet links cannot be listed without resolving the metadata from peers.
        // Return a single "All files" placeholder for magnets.
        if (urlOrPath.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                new TorrentFileEntry
                {
                    Index = 1,
                    Filename = "All files (magnet link — resolve at download time)",
                    FilesizeBytes = 0,
                    IsSelected = true,
                }
            };
        }

        var output = await RunProcessAsync(
            aria2cPath,
            new[] { "-S", urlOrPath },
            ct);

        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<TorrentFileEntry>();

        return ParseAria2cFileList(output);
    }

    private static readonly Regex _aria2FileLineRegex =
        new(@"^\s*(\d+)\|(.+?)\s*\|(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static TorrentFileEntry[] ParseAria2cFileList(string output)
    {
        var entries = new List<TorrentFileEntry>();

        foreach (Match m in _aria2FileLineRegex.Matches(output))
        {
            if (!int.TryParse(m.Groups[1].Value.Trim(), out var idx)) continue;
            var filename = m.Groups[2].Value.Trim();
            var sizeStr  = m.Groups[3].Value.Trim();

            if (string.IsNullOrWhiteSpace(filename) || filename == "path/filename") continue;

            entries.Add(new TorrentFileEntry
            {
                Index        = idx,
                Filename     = filename,
                FilesizeBytes = ParseSizeString(sizeStr),
                IsSelected   = true,
            });
        }

        return entries.ToArray();
    }

    private static long ParseSizeString(string s)
    {
        // Handles: "1.63GiB", "450MiB", "80KiB", "1024B"
        s = s.Trim();
        double mult = 1;
        if (s.EndsWith("GiB", StringComparison.OrdinalIgnoreCase)) { mult = 1_073_741_824; s = s[..^3]; }
        else if (s.EndsWith("MiB", StringComparison.OrdinalIgnoreCase)) { mult = 1_048_576; s = s[..^3]; }
        else if (s.EndsWith("KiB", StringComparison.OrdinalIgnoreCase)) { mult = 1_024; s = s[..^3]; }
        else if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase))  { mult = 1_000_000_000; s = s[..^2]; }
        else if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase))  { mult = 1_000_000; s = s[..^2]; }
        else if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase))  { mult = 1_000; s = s[..^2]; }
        else if (s.EndsWith("B", StringComparison.OrdinalIgnoreCase))   { s = s[..^1]; }

        return double.TryParse(s.Trim(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var v) ? (long)(v * mult) : 0;
    }

    // ─────────────────────────────────────────────────────
    // Process runner
    // ─────────────────────────────────────────────────────

    private static async Task<string> RunProcessAsync(
        string exe,
        string[] args,
        CancellationToken ct,
        bool isYtDlp = false)
    {
        ct.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (isYtDlp) YtDlpIntegration.Configure(psi);

        using var proc = new Process { StartInfo = psi };

        proc.Start();
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (isYtDlp && proc.ExitCode != 0)
            throw new InvalidOperationException(YtDlpIntegration.FailureMessage(proc.ExitCode, await stderr));
        return await stdout;
    }

    // ─────────────────────────────────────────────────────
    // Formatters
    // ─────────────────────────────────────────────────────

    private static string FormatDuration(int totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private static string FormatVideoCodec(string raw)
    {
        if (raw.StartsWith("avc",  StringComparison.OrdinalIgnoreCase)) return "H.264";
        if (raw.StartsWith("hev",  StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("hvc",  StringComparison.OrdinalIgnoreCase)) return "H.265";
        if (raw.StartsWith("av01", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("av1",  StringComparison.OrdinalIgnoreCase)) return "AV1";
        if (raw.StartsWith("vp9",  StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("vp09", StringComparison.OrdinalIgnoreCase)) return "VP9";
        if (raw.StartsWith("vp8",  StringComparison.OrdinalIgnoreCase)) return "VP8";
        // Return first segment (e.g. "avc1.640028" → "avc1")
        var dot = raw.IndexOf('.');
        return dot > 0 ? raw[..dot].ToUpperInvariant() : raw.ToUpperInvariant();
    }

    private static string FormatAudioCodec(string raw)
    {
        if (raw.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)) return "AAC";
        if (raw.StartsWith("opus", StringComparison.OrdinalIgnoreCase)) return "Opus";
        if (raw.StartsWith("vorbis", StringComparison.OrdinalIgnoreCase)) return "Vorbis";
        if (raw.StartsWith("mp3",  StringComparison.OrdinalIgnoreCase)) return "MP3";
        var dot = raw.IndexOf('.');
        return dot > 0 ? raw[..dot].ToUpperInvariant() : raw.ToUpperInvariant();
    }

    // ─────────────────────────────────────────────────────
    // JSON helpers
    // ─────────────────────────────────────────────────────

    private static string? GetString(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
