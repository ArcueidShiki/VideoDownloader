using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Static helpers for building subprocess arguments and launching
/// yt-dlp.exe / aria2c.exe processes.
/// Mirrors macOS YtDlpEngine / TorrentEngine argument builders.
/// </summary>
public static class DownloadEngine
{
    // ─────────────────────────────────────────────────────
    // Binary finders (delegated to BinaryResolver)
    // ─────────────────────────────────────────────────────

    public static string? FindYtDlp()  => BinaryResolver.FindYtDlp();
    public static string? FindAria2c() => BinaryResolver.FindAria2c();

    // ─────────────────────────────────────────────────────
    // Argument builders
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds yt-dlp arguments for a download.
    /// Mirrors macOS YtDlpEngine.buildArguments().
    /// </summary>
    public static string[] BuildYtDlpArgs(
        string url,
        string quality              = "bestvideo+bestaudio/best",
        string containerFormat      = "mp4",
        string? outputDir           = null,
        string? filename            = null,
        long speedLimitBytesPerSec  = 0,
        bool downloadSubtitles      = false,
        string? outputTemplate      = null)   // fully overrides --output when set
    {
        // If caller supplied a full template, use it directly; skip dir/filename logic
        if (outputTemplate is null)
        {
            outputDir ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                          + @"\Downloads";

            // Sanitise filename to prevent path traversal (mirrors macOS logic)
            string? safeFilename = null;
            if (!string.IsNullOrWhiteSpace(filename))
            {
                safeFilename = Path.GetFileName(filename);
                if (string.IsNullOrEmpty(safeFilename)) safeFilename = null;
            }

            outputTemplate = safeFilename is not null
                ? Path.Combine(outputDir, safeFilename)
                : Path.Combine(outputDir, "%(title)s.%(ext)s");
        }

        var args = new List<string>
        {
            "--format", $"{quality}/best",
        };

        if (containerFormat != "native")
            args.AddRange(["--merge-output-format", containerFormat]);

        args.AddRange([
            "--output",               outputTemplate!,
            "--progress", "--newline",
            "--no-playlist",
            "--continue",
            "--concurrent-fragments", "1",
        ]);

        if (downloadSubtitles)
            args.AddRange(["--write-auto-sub", "--sub-lang", "en"]);

        if (speedLimitBytesPerSec > 0)
            args.AddRange(["--limit-rate", speedLimitBytesPerSec.ToString()]);

        args.AddRange(YtDlpIntegration.OutputArguments);
        args.Add("--");
        args.Add(url);
        return args.ToArray();
    }

    /// <summary>
    /// Builds aria2c arguments for HTTP, torrent, or magnet downloads.
    /// Mirrors macOS TorrentEngine argument logic.
    /// </summary>
    public static string[] BuildAria2cArgs(
        string url,
        string? outputDir            = null,
        int connections              = 8,
        long speedLimitBytesPerSec   = 0,
        string? selectedFiles        = null)  // comma-separated indices, e.g. "1,3,5"
    {
        outputDir ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
        connections = Math.Clamp(connections, 1, 16);

        var isTorrent = url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
                     || url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

        var args = new List<string>
        {
            "--dir",                       outputDir,
            "--split",                     connections.ToString(),
            "--max-connection-per-server", connections.ToString(),
            "--continue=true",
            "--allow-overwrite=true",
            "--console-log-level=notice",
            "--download-result=hide",
        };

        if (isTorrent)
        {
            // Mirrors macOS TorrentEngine.buildArguments DHT/tracker configuration
            args.AddRange([
                "--summary-interval=1",
                "--enable-dht=true",
                "--enable-dht6=false",
                "--enable-peer-exchange=true",
                "--bt-enable-lpd=true",
                "--check-certificate=false",
                "--dht-entry-point=router.bittorrent.com:6881",
                "--dht-entry-point=router.utorrent.com:6881",
                "--dht-entry-point=dht.transmissionbt.com:6881",
                "--bt-tracker=udp://tracker.opentrackr.org:1337/announce",
                "--bt-tracker=udp://open.tracker.cl:1337/announce",
                "--bt-tracker=udp://tracker.openbittorrent.com:6969/announce",
                "--bt-tracker=udp://9.rarbg.com:2810/announce",
                "--bt-tracker=udp://tracker.torrent.eu.org:451/announce",
                "--bt-tracker-connect-timeout=20",
                "--bt-tracker-timeout=60",
                "--bt-stop-timeout=0",
                "--bt-max-peers=500",
            ]);
        }

        if (!string.IsNullOrEmpty(selectedFiles))
            args.AddRange(["--select-file", selectedFiles]);

        if (speedLimitBytesPerSec > 0)
            args.AddRange(["--max-download-limit", speedLimitBytesPerSec.ToString()]);

        args.Add(url);
        return args.ToArray();
    }

    // ─────────────────────────────────────────────────────
    // Process launch helpers
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts a yt-dlp process and returns both the <see cref="Process"/> handle (needed
    /// to kill it on pause/cancel) and a <see cref="Task{Int32}"/> that resolves with the
    /// exit code only after <em>all</em> stdout/stderr output has been delivered.
    ///
    /// Critical: use <c>proc.WaitForExit()</c> (no-timeout overload) — this is the only
    /// .NET API that waits for both process termination AND async pipe-reader completion.
    /// The <c>Process.Exited</c> event fires too early (before the pipe is fully flushed),
    /// which would leave <c>capturedPath</c> null when the exit handler reads it.
    /// </summary>
    public static (Process Process, Task<int> ExitTask) StartYtDlpProcess(
        string ytDlpPath,
        string[] args,
        Action<string> onOutput)
    {
        var psi = BuildPsi(ytDlpPath, args);
        EnrichPath(psi);
        YtDlpIntegration.Configure(psi);

        var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) onOutput(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // WaitForExit() (no timeout) guarantees all async pipe data is delivered before
        // the task completes — unlike the Exited event which fires too early.
        var exitTask = Task.Run(() => { proc.WaitForExit(); return proc.ExitCode; });
        return (proc, exitTask);
    }

    /// <summary>
    /// Same guarantee as <see cref="StartYtDlpProcess"/> — exit task resolves only after
    /// all aria2c output lines have been delivered to <paramref name="onOutput"/>.
    /// </summary>
    public static (Process Process, Task<int> ExitTask) StartAria2cProcess(
        string aria2cPath,
        string[] args,
        Action<string> onOutput)
    {
        var psi = BuildPsi(aria2cPath, args);
        EnrichPath(psi);

        var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) onOutput(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        var exitTask = Task.Run(() => { proc.WaitForExit(); return proc.ExitCode; });
        return (proc, exitTask);
    }

    // ─────────────────────────────────────────────────────
    // Progress parsing
    // ─────────────────────────────────────────────────────

    // Pattern: "[download]  42.3% of ~123.45MiB at    1.23MiB/s ETA 00:12"
    private static readonly Regex _ytDlpProgressRegex = new(
        @"\[download\]\s+([\d.]+)%.*?at\s+([\d.]+)(KiB|MiB|GiB)/s.*?ETA\s+([\d:]+)",
        RegexOptions.Compiled);

    public static YtDlpProgressUpdate? ParseYtDlpProgress(string line)
    {
        if (line.StartsWith(YtDlpIntegration.ProgressPrefix, StringComparison.Ordinal))
            return YtDlpIntegration.ParseProgress(line);
        if (!line.Contains("[download]") || !line.Contains('%')) return null;

        var m = _ytDlpProgressRegex.Match(line);
        if (!m.Success) return null;

        if (!double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var pct)) return null;

        if (!double.TryParse(m.Groups[2].Value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var speed)) return null;

        var mult = m.Groups[3].Value switch
        {
            "KiB" => 1_024.0,
            "MiB" => 1_048_576.0,
            _     => 1_073_741_824.0,
        };

        return new YtDlpProgressUpdate(pct / 100.0, (long)(speed * mult), ParseEta(m.Groups[4].Value));
    }

    // Pattern: "[#abc123 1.5GiB/3.0GiB(50%) CN:5 DL:1.2MiB ETA:1m20s]"
    private static readonly Regex _aria2ProgressRegex = new(
        @"\((\d+)%\).*?DL:([\d.]+)(B|KB|KiB|MB|MiB|GB|GiB).*?ETA:([\w:]+)",
        RegexOptions.Compiled);

    public static Aria2cProgressUpdate? ParseAria2cProgress(string line)
    {
        var m = _aria2ProgressRegex.Match(line);
        if (!m.Success) return null;

        if (!int.TryParse(m.Groups[1].Value, out var pct)) return null;
        if (!double.TryParse(m.Groups[2].Value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var speed)) return null;

        var mult = m.Groups[3].Value switch
        {
            "KiB" or "KB" => 1_024.0,
            "MiB" or "MB" => 1_048_576.0,
            "GiB" or "GB" => 1_073_741_824.0,
            _             => 1.0,
        };

        return new Aria2cProgressUpdate(pct / 100.0, (long)(speed * mult), ParseAria2cEta(m.Groups[4].Value));
    }

    public static int? ParseEta(string eta)
    {
        var p = eta.Split(':');
        if (p.Length == 2 && int.TryParse(p[0], out var m) && int.TryParse(p[1], out var s))
            return m * 60 + s;
        if (p.Length == 3 && int.TryParse(p[0], out var h) &&
            int.TryParse(p[1], out var mm) && int.TryParse(p[2], out var ss))
            return h * 3600 + mm * 60 + ss;
        return null;
    }

    // aria2c ETA format: "1m20s", "45s", "1h20m"
    private static int? ParseAria2cEta(string eta)
    {
        int total = 0;
        var match = Regex.Match(eta, @"(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?");
        if (!match.Success) return null;

        if (match.Groups[1].Success) total += int.Parse(match.Groups[1].Value) * 3600;
        if (match.Groups[2].Success) total += int.Parse(match.Groups[2].Value) * 60;
        if (match.Groups[3].Success) total += int.Parse(match.Groups[3].Value);
        return total > 0 ? total : null;
    }

    // ─────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────

    private static ProcessStartInfo BuildPsi(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>
    /// Prepends the binary's directory to PATH so ffmpeg/ffprobe placed in
    /// the same tools\ folder are always found, even when launched from Start Menu.
    /// </summary>
    private static void EnrichPath(ProcessStartInfo psi)
    {
        // Also add the bundled tools\ directory
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var exeDir   = Path.GetDirectoryName(psi.FileName) ?? string.Empty;

        var existing = psi.Environment.TryGetValue("PATH", out var p) ? p ?? "" : "";
        var toAdd    = new List<string>();

        if (!string.IsNullOrEmpty(toolsDir) &&
            !existing.Contains(toolsDir, StringComparison.OrdinalIgnoreCase))
            toAdd.Add(toolsDir);

        if (!string.IsNullOrEmpty(exeDir) &&
            !existing.Contains(exeDir, StringComparison.OrdinalIgnoreCase))
            toAdd.Add(exeDir);

        if (toAdd.Count > 0)
            psi.Environment["PATH"] = string.Join(Path.PathSeparator.ToString(), toAdd) + Path.PathSeparator + existing;
    }
}

/// <summary>Parsed result of a single yt-dlp --newline progress line.</summary>
public record YtDlpProgressUpdate(double Progress, long SpeedBytesPerSec, int? EtaSeconds);

/// <summary>Parsed result of a single aria2c progress line.</summary>
public record Aria2cProgressUpdate(double Progress, long SpeedBytesPerSec, int? EtaSeconds);
