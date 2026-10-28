using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AnyDownloader.Windows.Services;

/// <summary>The yt-dlp CLI boundary shared by metadata and downloads.</summary>
public static class YtDlpIntegration
{
    public const string FilePrefix = "__ANY_FILE__";
    public const string ProgressPrefix = "__ANY_PROGRESS__";

    public static string[] OutputArguments =>
    [
        "--no-simulate", "--abort-on-error", "--no-colors",
        "--output-na-placeholder", "null",
        "--progress-template", "download:" + ProgressPrefix +
            "{\"downloaded\":%(progress.downloaded_bytes)j,\"total\":%(progress.total_bytes)j,\"estimate\":%(progress.total_bytes_estimate)j,\"speed\":%(progress.speed)j,\"eta\":%(progress.eta)j}",
        "--print", "after_move:" + FilePrefix + "%(filepath)j",
    ];

    public static void Configure(ProcessStartInfo psi)
    {
        var existing = psi.ArgumentList.ToArray();
        psi.ArgumentList.Clear();
        foreach (var arg in new[] { "--ignore-config", "--encoding", "utf-8" })
            psi.ArgumentList.Add(arg);
        var deno = BinaryResolver.FindDeno();
        var node = BinaryResolver.FindNode();
        if (deno is not null || node is not null)
        {
            psi.ArgumentList.Add("--js-runtimes");
            psi.ArgumentList.Add(deno is not null ? $"deno:{deno}" : $"node:{node}");
        }
        var ffmpeg = BinaryResolver.FindFfmpeg();
        if (ffmpeg is not null)
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(Path.GetDirectoryName(ffmpeg)!);
        }
        foreach (var arg in existing) psi.ArgumentList.Add(arg);
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
    }

    public static YtDlpProgressUpdate? ParseProgress(string line)
    {
        if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line[ProgressPrefix.Length..]);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            double? Number(string key) => doc.RootElement.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) &&
                double.IsFinite(n) && n >= 0 ? n : null;
            var total = Number("total");
            if (total is null or 0) total = Number("estimate");
            var downloaded = Number("downloaded") ?? 0;
            var speed = Number("speed") ?? 0;
            var eta = Number("eta");
            return new YtDlpProgressUpdate(total > 0 ? Math.Clamp(downloaded / total.Value, 0, 1) : 0,
                (long)Math.Min(speed, long.MaxValue), eta <= int.MaxValue ? (int?)eta : null);
        }
        catch (JsonException) { return null; }
    }

    public static string? ParseFinalPath(string line)
    {
        if (!line.StartsWith(FilePrefix, StringComparison.Ordinal)) return null;
        try { return JsonSerializer.Deserialize<string>(line[FilePrefix.Length..]); }
        catch (JsonException) { return null; }
    }

    public static string FailureMessage(int exitCode, string diagnostics)
    {
        var hint = diagnostics.Contains("no such option: --js-runtimes", StringComparison.OrdinalIgnoreCase)
            ? "Update the app's yt-dlp executable; it does not support JavaScript runtimes. "
            : diagnostics.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) &&
              (diagnostics.Contains("not installed", StringComparison.OrdinalIgnoreCase) || diagnostics.Contains("not found", StringComparison.OrdinalIgnoreCase))
                ? "Install ffmpeg and ffprobe in the app's tools folder, then retry. "
                : diagnostics.Contains("JavaScript runtime", StringComparison.OrdinalIgnoreCase) || diagnostics.Contains("challenge solving failed", StringComparison.OrdinalIgnoreCase)
                    ? "Install a supported Deno or Node runtime and update yt-dlp with its EJS scripts. " : "";
        var details = diagnostics.Trim();
        return $"{hint}yt-dlp exited with code {exitCode}." + (details.Length > 0 ? $"\n{details}" : " No error details were returned.");
    }
}

/// <summary>Thread-safe capture because stdout and stderr callbacks can run concurrently.</summary>
public sealed class YtDlpOutput
{
    private readonly object _lock = new();
    private readonly StringBuilder _diagnostics = new();
    private string? _finalPath;
    public string? FinalPath { get { lock (_lock) return _finalPath; } }
    public void Observe(string line)
    {
        lock (_lock)
        {
            if (line.StartsWith(YtDlpIntegration.FilePrefix, StringComparison.Ordinal))
                _finalPath = YtDlpIntegration.ParseFinalPath(line);
            else if (!line.StartsWith(YtDlpIntegration.ProgressPrefix, StringComparison.Ordinal))
            {
                _diagnostics.AppendLine(line);
                if (_diagnostics.Length > 16_384) _diagnostics.Remove(0, _diagnostics.Length - 16_384);
            }
        }
    }
    public string Error(int exitCode) { lock (_lock) return YtDlpIntegration.FailureMessage(exitCode, _diagnostics.ToString()); }
}
