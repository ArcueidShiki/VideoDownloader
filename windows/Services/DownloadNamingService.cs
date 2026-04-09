using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Stateless helper that builds yt-dlp --output path templates.
/// Extracted from DownloadViewModel so the logic can be unit-tested without a running UI.
///
/// When <c>knownTitle</c> is supplied the stem is resolved with C# string interpolation,
/// and when the container is also known the extension is literal — giving a fully
/// predictable output path we can track for delete / play.
/// When title/container are unknown we fall back to yt-dlp's %(title)s / %(ext)s
/// placeholders, resolved by yt-dlp at runtime.
///
/// Naming rules:
///   merged  (video + audio, merge=true)  →  {title}.mp4
///   video-only (no audio)                →  {title} [1080p] (no audio).{ext}
///   audio-only (no video)                →  {title} [320_kbps].{ext}
///   fallback   (nothing selected)        →  {title}.%(ext)s
///   custom filename                      →  {override}.%(ext)s  (always wins)
/// </summary>
public static class DownloadNamingService
{
    // ─────────────────────────────────────────────────────
    // Core template builder
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds the yt-dlp --output argument.
    /// Returns a fully-resolved literal path when <paramref name="knownTitle"/>
    /// and the relevant container are both available; otherwise returns a template
    /// containing yt-dlp's %(title)s / %(ext)s placeholders.
    /// </summary>
    /// <param name="outputDir">Destination directory.</param>
    /// <param name="hasVideo">True when a video format was selected.</param>
    /// <param name="hasAudio">True when an audio format was selected.</param>
    /// <param name="mergeToMp4">True when video+audio should be merged to mp4.</param>
    /// <param name="videoQualityLabel">e.g. "1080p". Null/empty → omit from name.</param>
    /// <param name="videoContainer">e.g. "mp4", "webm". Null/empty → %(ext)s fallback.</param>
    /// <param name="audioBitrateLabel">e.g. "320 kbps". Null/empty → omit from name.</param>
    /// <param name="audioContainer">e.g. "m4a", "webm". Null/empty → %(ext)s fallback.</param>
    /// <param name="filenameOverride">User-supplied stem (no extension). Takes priority.</param>
    /// <param name="keepBoth">Append (n) suffix when the resolved path already exists.</param>
    /// <param name="knownTitle">Video title from fetched metadata — used instead of %(title)s.</param>
    public static string BuildOutputTemplate(
        string outputDir,
        bool hasVideo,
        bool hasAudio,
        bool mergeToMp4,
        string? videoQualityLabel,
        string? videoContainer,
        string? audioBitrateLabel,
        string? audioContainer,
        string? filenameOverride = null,
        bool keepBoth = false,
        string? knownTitle = null)
    {
        // User-provided custom filename takes priority over all naming rules.
        // Keep %(ext)s so yt-dlp appends the correct extension automatically.
        if (!string.IsNullOrWhiteSpace(filenameOverride))
        {
            var safe = SanitizeFilename(filenameOverride!);
            return Path.Combine(outputDir, $"{safe}.%(ext)s");
        }

        // Build the filename stem.
        // When knownTitle is available use it directly (C# interpolation) so the
        // final path is predictable. Otherwise fall back to yt-dlp's %(title)s.
        var stem      = !string.IsNullOrWhiteSpace(knownTitle) ? SanitizeFilename(knownTitle!) : "%(title)s";
        bool isLiteral = stem != "%(title)s";   // no yt-dlp placeholders remaining in stem

        // ── Separate video-only ─────────────────────────────────────────────────
        // Embed resolution label + "(no audio)" so both files from a split download
        // are self-describing and cannot collide on disk.
        if (hasVideo && !hasAudio && !mergeToMp4)
        {
            var q   = string.IsNullOrEmpty(videoQualityLabel)
                ? string.Empty
                : $" [{SanitizeLabel(videoQualityLabel)}]";
            var ext = !string.IsNullOrEmpty(videoContainer) ? videoContainer! : "%(ext)s";
            return ResolveUniquePath(outputDir, $"{stem}{q} (no audio)", ext, isLiteral);
        }

        // ── Separate audio-only ─────────────────────────────────────────────────
        // Embed bitrate label so the file is identifiable.
        if (!hasVideo && hasAudio && !mergeToMp4)
        {
            var b   = string.IsNullOrEmpty(audioBitrateLabel)
                ? string.Empty
                : $" [{SanitizeLabel(audioBitrateLabel)}]";
            var ext = !string.IsNullOrEmpty(audioContainer) ? audioContainer! : "%(ext)s";
            return ResolveUniquePath(outputDir, $"{stem}{b}", ext, isLiteral);
        }

        // ── Merged video + audio ────────────────────────────────────────────────
        // yt-dlp is also passed --merge-output-format mp4, so the extension is mp4.
        if (hasVideo && hasAudio && mergeToMp4)
            return ResolveUniquePath(outputDir, stem, "mp4", isLiteral);

        // ── Single video stream (no audio selected, not "separate" mode) ────────
        if (hasVideo)
        {
            var ext = !string.IsNullOrEmpty(videoContainer) ? videoContainer! : "%(ext)s";
            return ResolveUniquePath(outputDir, stem, ext, isLiteral);
        }

        // ── Single audio stream (no video selected) ─────────────────────────────
        if (hasAudio)
        {
            var ext = !string.IsNullOrEmpty(audioContainer) ? audioContainer! : "%(ext)s";
            return ResolveUniquePath(outputDir, stem, ext, isLiteral);
        }

        // ── Fallback: nothing selected — let yt-dlp pick best format + extension ─
        return Path.Combine(outputDir, $"{stem}.%(ext)s");
    }

    // ─────────────────────────────────────────────────────
    // Internal path helpers
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds <c>dir\stem.ext</c>.
    /// When <paramref name="checkCollision"/> is true and the path is fully literal
    /// (no yt-dlp % placeholders), appends a Windows-style " (n)" counter if the
    /// file already exists.
    /// </summary>
    private static string ResolveUniquePath(string dir, string stem, string ext, bool checkCollision)
    {
        var path = Path.Combine(dir, $"{stem}.{ext}");

        // Skip collision check when yt-dlp placeholders remain — extension or title
        // will be substituted by yt-dlp at runtime, so we cannot resolve the path here.
        if (!checkCollision || path.Contains('%'))
            return path;

        if (!File.Exists(path)) return path;

        for (int n = 2; n < 1_000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}).{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    // ─────────────────────────────────────────────────────
    // Legacy collision helper (kept for unit-test compatibility)
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// If the resolved path (with known title substituted) already exists on disk,
    /// returns a modified template with a Windows-style " (n)" suffix.
    /// Only works when %(ext)s is NOT present (extension is hard-coded).
    /// </summary>
    public static string ResolveUniqueTemplate(string template, string knownTitle)
    {
        if (!template.Contains("%(title)s")) return template;
        if (template.Contains("%(ext)s"))    return template; // ext unknown — can't pre-check

        var resolved = template.Replace("%(title)s", SanitizeFilename(knownTitle));
        if (!File.Exists(resolved)) return template; // no conflict

        var dir      = Path.GetDirectoryName(resolved) ?? string.Empty;
        var nameBase = Path.GetFileNameWithoutExtension(resolved);
        var ext      = Path.GetExtension(resolved);

        for (int n = 2; n < 1_000; n++)
        {
            var candidate = Path.Combine(dir, $"{nameBase} ({n}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
        return template;
    }

    // ─────────────────────────────────────────────────────
    // FilenameLabel helper (extracted for testability)
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the display label shown in the task-row chip.
    ///   Priority: FilePaths (multi-file) → FilePath → FilenameOverride → Title → empty.
    /// </summary>
    public static string ComputeFilenameLabel(
        IReadOnlyList<string>? filePaths,
        string? filePath,
        string? filenameOverride,
        string? title)
    {
        // Multi-file (separate video+audio)
        if (filePaths is { Count: > 0 })
        {
            var first = Path.GetFileName(filePaths[0]);
            return filePaths.Count == 1 ? first : $"{first} +{filePaths.Count - 1} more";
        }
        // Single file — skip if FilePath resolves to a directory
        if (!string.IsNullOrEmpty(filePath) && !Directory.Exists(filePath))
            return Path.GetFileName(filePath)!;
        if (!string.IsNullOrEmpty(filenameOverride))
            return filenameOverride!;
        if (!string.IsNullOrEmpty(title))
            return title!;
        return string.Empty;
    }

    // ─────────────────────────────────────────────────────
    // Sanitizers (public for test access)
    // ─────────────────────────────────────────────────────

    /// <summary>Replaces characters that are illegal in file names with underscores.</summary>
    public static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
    }

    /// <summary>
    /// Like SanitizeFilename but also converts spaces to underscores — used for
    /// quality/bitrate labels embedded inside the filename (e.g. "1080p", "320_kbps").
    /// </summary>
    public static string SanitizeLabel(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(label.Select(c =>
            Array.IndexOf(invalid, c) >= 0 ? '_' : c == ' ' ? '_' : c).ToArray());
    }
}
