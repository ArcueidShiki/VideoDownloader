using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Locates yt-dlp.exe, ffmpeg.exe, ffprobe.exe, and aria2c.exe.
///
/// Search order:
///   1. tools\ subfolder next to the app executable (MSIX/installer package).
///   2. %LOCALAPPDATA%\AnyDownloader\bin\ (user-level install).
///   3. Well-known package-manager locations (Scoop, Winget, Chocolatey, manual).
///   4. Directories on the system PATH.
/// </summary>
public static class BinaryResolver
{
    private static readonly string AppToolsDir =
        Path.Combine(AppContext.BaseDirectory, "tools");

    private static readonly string UserBinDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "AnyDownloader", "bin");

    // ─────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────

    public static string? FindYtDlp()    => Resolve("yt-dlp.exe");
    public static string? FindFfmpeg()   => Resolve("ffmpeg.exe");
    public static string? FindFfprobe()  => Resolve("ffprobe.exe");
    public static string? FindAria2c()   => Resolve("aria2c.exe");
    public static string? FindDeno()     => Resolve("deno.exe");
    public static string? FindNode()     => Resolve("node.exe");

    /// <summary>
    /// Returns true if all required tools are available.
    /// yt-dlp + ffmpeg are needed for video downloads.
    /// aria2c is needed for HTTP and torrent downloads.
    /// </summary>
    public static bool AreAllPresent(out string[] missing)
    {
        var list = new List<string>();
        if (FindYtDlp()  is null) list.Add("yt-dlp");
        if (FindFfmpeg() is null) list.Add("ffmpeg");
        if (FindAria2c() is null) list.Add("aria2c");
        missing = list.ToArray();
        return list.Count == 0;
    }

    /// <summary>
    /// Returns true if yt-dlp (minimum requirement) is present.
    /// </summary>
    public static bool HasMinimumTools() => FindYtDlp() is not null;

    // ─────────────────────────────────────────────────────
    // Core resolver
    // ─────────────────────────────────────────────────────

    private static string? Resolve(string exe)
    {
        if (DependencyUpdateCoordinator.ResolveActive(exe) is string active && File.Exists(active)) return active;
        var candidates = new List<string>
        {
            // 1. Bundled tools next to the .exe / MSIX package
            Path.Combine(AppToolsDir, exe),

            // 2. User-level AnyDownloader bin directory
            Path.Combine(UserBinDir, exe),

            // 3. Scoop (current user)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                         "scoop", "shims", exe),

            // 4. Winget / standalone install in %LOCALAPPDATA%\Programs
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Programs", Path.GetFileNameWithoutExtension(exe), exe),

            // 5. Chocolatey (system-wide)
            Path.Combine(@"C:\ProgramData\chocolatey\bin", exe),

            // 6. Common manual paths
            Path.Combine(@"C:\", Path.GetFileNameWithoutExtension(exe), exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Path.GetFileNameWithoutExtension(exe), exe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Path.GetFileNameWithoutExtension(exe), exe),
        };

        // 7. Every directory listed in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(dir))
                candidates.Add(Path.Combine(dir.Trim(), exe));
        }

        return FirstExisting(candidates);
    }

    private static string? FirstExisting(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p)) return p;
            }
            catch { /* ignore bad paths */ }
        }
        return null;
    }
}
