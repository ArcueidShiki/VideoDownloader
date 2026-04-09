using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Windows.Models;
using AnyDownloader.Windows.Services;
using Xunit;
using Xunit.Abstractions;

namespace AnyDownloader.Windows.Tests;

// ══════════════════════════════════════════════════════════════════════════════
// E2E Download Path Tests  —  12 cases (4 types × 3 scenarios)
//
// PURPOSE
//   Verify the end-to-end contract between DownloadNamingService path prediction
//   and the actual file created by yt-dlp, for every download type.
//
// DOWNLOAD TYPES
//   1. video-only  (video stream, no audio)
//   2. audio-only  (audio stream, no video)
//   3. separate    (video + audio, two independent files kept)
//   4. merged      (video + audio, merged to .mp4 by ffmpeg)
//
// SCENARIOS (3 assertions per type)
//   A – ConfirmFormat / Persistence
//         Predicted path from NamingService must equal the [download] Destination:
//         line yt-dlp emits.  This is the path ConfirmFormat saves to SQLite.
//   B – Play button
//         File.Exists(predictedPath) must be true so the play button can open it.
//   C – Delete dialog
//         Every path in the predicted list must exist on disk so the delete
//         confirmation dialog lists real files.
//
// REQUIREMENTS
//   • yt-dlp.exe present in tools\ or PATH  (tests skip otherwise)
//   • ffmpeg.exe present for merged test     (test skips otherwise)
//   • Network access to YouTube
//
// TEST VIDEO
//   https://www.youtube.com/shorts/wte1tdTwGLs  (~5 s, lightweight)
// ══════════════════════════════════════════════════════════════════════════════

[Collection("E2eSequential")]   // never run in parallel — share yt-dlp / network
public class E2eDownloadPathTests : IDisposable
{
    // ─────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────

    private const string TestUrl         = "https://www.youtube.com/shorts/wte1tdTwGLs";
    private static readonly TimeSpan     DownloadTimeout = TimeSpan.FromMinutes(3);

    // ─────────────────────────────────────────────────────
    // Infrastructure
    // ─────────────────────────────────────────────────────

    private readonly string          _tmpDir;
    private readonly string?         _ytDlpPath;
    private readonly string?         _ffmpegPath;
    private readonly ITestOutputHelper _out;

    // Cached once per test class instance — all 4 tests in a run share one fetch.
    private VideoInfoModel? _videoInfo;

    public E2eDownloadPathTests(ITestOutputHelper output)
    {
        _out        = output;
        _ytDlpPath  = BinaryResolver.FindYtDlp();
        _ffmpegPath = BinaryResolver.FindFfmpeg();
        _tmpDir     = Path.Combine(Path.GetTempPath(), $"AnyDL_E2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        output.WriteLine($"Temp dir : {_tmpDir}");
        output.WriteLine($"yt-dlp   : {_ytDlpPath ?? "(not found)"}");
        output.WriteLine($"ffmpeg   : {_ffmpegPath ?? "(not found)"}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TYPE 1 — VIDEO ONLY  (hasVideo=true, hasAudio=false, mergeToMp4=false)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scenario A: NamingService prediction matches yt-dlp's Destination line (persistence contract).
    /// Scenario B: File.Exists(predicted) after download                      (play button).
    /// Scenario C: predicted file is in the delete list and exists on disk    (delete dialog).
    /// </summary>
    [Fact]
    public async Task VideoOnly_AllScenarios()
    {
        SkipIfNoYtDlp();

        var (info, vFmt, _) = await GetFormatsAsync(needSeparateVideo: true);
        _out.WriteLine($"Video fmt : {vFmt!.FormatId} {vFmt.QualityLabel} .{vFmt.Container}");

        // ── Same computation as ConfirmFormat ──────────────────────────────────
        // Step 1: NamingService produces a template (may contain CJK chars).
        var template = DownloadNamingService.BuildOutputTemplate(
            _tmpDir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: vFmt.QualityLabel, videoContainer: vFmt.Container,
            audioBitrateLabel: null, audioContainer: null,
            knownTitle: info.Title);

        // Step 2: Ask yt-dlp what it will actually name the file (mirrors ConfirmFormat).
        //         yt-dlp 2026+ strips non-ASCII chars, so this is the authoritative path.
        var predicted = (!template.Contains('%'))
            ? await MetadataService.GetActualFilenameAsync(_ytDlpPath!, vFmt.FormatId, template, TestUrl) ?? template
            : template;

        _out.WriteLine($"Template  : {template}");
        _out.WriteLine($"Predicted : {predicted}");

        // Literal path is required — if '%' is present, NamingService gave up on prediction
        Assert.True(!predicted.Contains('%'),
            $"BuildOutputTemplate returned a yt-dlp template (contains %) instead of a literal path. " +
            $"Ensure knownTitle and videoContainer are non-null.\n  Got: {predicted}");

        // yt-dlp is invoked with the template (same as StartYtDlpDownloadAsync does)
        var result = await RunYtDlpAsync(vFmt.FormatId, "native", template);

        // ── A: Persistence contract ────────────────────────────────────────────
        // The [download] Destination: path yt-dlp emits IS the path ConfirmFormat
        // would save to SQLite.  They must be equal.
        Assert.NotNull(result.DestinationPath);
        AssertPathEqual(predicted, result.DestinationPath!,
            "Persistence contract: NamingService prediction must equal yt-dlp Destination path");

        // ── B: Play button ─────────────────────────────────────────────────────
        Assert.True(File.Exists(predicted),
            $"Play button would fail — file not found at predicted path:\n  {predicted}");

        // ── C: Delete dialog ───────────────────────────────────────────────────
        var deleteList = new[] { predicted };   // single file for video-only
        foreach (var f in deleteList)
            Assert.True(File.Exists(f),
                $"Delete dialog references non-existent file:\n  {f}");

        Assert.Equal(0, result.ExitCode);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TYPE 2 — AUDIO ONLY  (hasVideo=false, hasAudio=true, mergeToMp4=false)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AudioOnly_AllScenarios()
    {
        SkipIfNoYtDlp();

        var (info, _, aFmt) = await GetFormatsAsync(needAudio: true);
        _out.WriteLine($"Audio fmt : {aFmt!.FormatId} {aFmt.Label} .{aFmt.Container}");

        var template = DownloadNamingService.BuildOutputTemplate(
            _tmpDir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: aFmt.Label, audioContainer: aFmt.Container,
            knownTitle: info.Title);

        var predicted = (!template.Contains('%'))
            ? await MetadataService.GetActualFilenameAsync(_ytDlpPath!, aFmt.FormatId, template, TestUrl) ?? template
            : template;

        _out.WriteLine($"Template  : {template}");
        _out.WriteLine($"Predicted : {predicted}");

        Assert.True(!predicted.Contains('%'),
            $"BuildOutputTemplate returned a template instead of a literal path.\n  Got: {predicted}");

        var result = await RunYtDlpAsync(aFmt.FormatId, "native", template);

        // A: Persistence contract
        Assert.NotNull(result.DestinationPath);
        AssertPathEqual(predicted, result.DestinationPath!,
            "Persistence contract: NamingService prediction must equal yt-dlp Destination path");

        // Diagnostic: confirm what yt-dlp actually created
        _out.WriteLine($"File.Exists(result.DestinationPath): {File.Exists(result.DestinationPath!)}");
        _out.WriteLine($"File.Exists(predicted)             : {File.Exists(predicted)}");
        _out.WriteLine($"Paths equal                        : {string.Equals(result.DestinationPath!.Trim(), predicted.Trim(), StringComparison.OrdinalIgnoreCase)}");
        // List what's in the temp dir
        _out.WriteLine("Files in temp dir (EnumerateFiles):");
        foreach (var f in Directory.EnumerateFiles(_tmpDir, "*", SearchOption.AllDirectories))
            _out.WriteLine($"  {f}");

        // B: Play button
        Assert.True(File.Exists(predicted),
            $"Play button would fail — audio file not found at:\n  {predicted}");

        // C: Delete dialog
        Assert.True(File.Exists(predicted),
            $"Delete dialog references non-existent file:\n  {predicted}");

        Assert.Equal(0, result.ExitCode);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TYPE 3 — SEPARATE  (hasVideo=true, hasAudio=true, mergeToMp4=false)
    //  Two independent files; play uses FilePaths[0]; delete shows both.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Separate_AllScenarios()
    {
        SkipIfNoYtDlp();

        var (info, vFmt, aFmt) = await GetFormatsAsync(needSeparateVideo: true, needAudio: true);
        _out.WriteLine($"Video fmt : {vFmt!.FormatId} {vFmt.QualityLabel} .{vFmt.Container}");
        _out.WriteLine($"Audio fmt : {aFmt!.FormatId} {aFmt.Label} .{aFmt.Container}");

        // Same two-call pattern as ConfirmFormat / StartYtDlpSeparateAsync
        var videoTemplate = DownloadNamingService.BuildOutputTemplate(
            _tmpDir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            vFmt.QualityLabel, vFmt.Container, null, null,
            keepBoth: true, knownTitle: info.Title);

        var audioTemplate = DownloadNamingService.BuildOutputTemplate(
            _tmpDir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            null, null, aFmt.Label, aFmt.Container,
            keepBoth: true, knownTitle: info.Title);

        // Resolve yt-dlp's actual sanitized paths (mirrors ConfirmFormat behaviour)
        var predictedVideo = (!videoTemplate.Contains('%'))
            ? await MetadataService.GetActualFilenameAsync(_ytDlpPath!, vFmt.FormatId, videoTemplate, TestUrl) ?? videoTemplate
            : videoTemplate;

        var predictedAudio = (!audioTemplate.Contains('%'))
            ? await MetadataService.GetActualFilenameAsync(_ytDlpPath!, aFmt.FormatId, audioTemplate, TestUrl) ?? audioTemplate
            : audioTemplate;

        _out.WriteLine($"Template  video : {videoTemplate}");
        _out.WriteLine($"Template  audio : {audioTemplate}");
        _out.WriteLine($"Predicted video : {predictedVideo}");
        _out.WriteLine($"Predicted audio : {predictedAudio}");

        // Paths must be literal and must NOT collide
        Assert.True(!predictedVideo.Contains('%'), $"Video path contains yt-dlp template: {predictedVideo}");
        Assert.True(!predictedAudio.Contains('%'), $"Audio path contains yt-dlp template: {predictedAudio}");
        Assert.False(
            string.Equals(predictedVideo, predictedAudio, StringComparison.OrdinalIgnoreCase),
            "Predicted video and audio paths must not collide — one would overwrite the other.");

        // yt-dlp is invoked with the templates (same as StartYtDlpSeparateAsync does)
        var videoResult = await RunYtDlpAsync(vFmt.FormatId, "native", videoTemplate);
        var audioResult = await RunYtDlpAsync(aFmt.FormatId, "native", audioTemplate);

        // A: Persistence contract (both paths must be stored in FilePaths[])
        Assert.NotNull(videoResult.DestinationPath);
        Assert.NotNull(audioResult.DestinationPath);
        AssertPathEqual(predictedVideo, videoResult.DestinationPath!, "video Destination path");
        AssertPathEqual(predictedAudio, audioResult.DestinationPath!, "audio Destination path");

        // B: Play button uses FilePaths.FirstOrDefault(File.Exists)
        var playPath = new[] { predictedVideo, predictedAudio }.FirstOrDefault(File.Exists);
        Assert.NotNull(playPath);
        Assert.True(File.Exists(playPath),
            $"Play button would fail — no file exists in predicted FilePaths:\n  {predictedVideo}\n  {predictedAudio}");

        // C: Delete dialog shows every file — all must exist
        foreach (var f in new[] { predictedVideo, predictedAudio })
            Assert.True(File.Exists(f),
                $"Delete dialog references non-existent file:\n  {f}");

        Assert.Equal(0, videoResult.ExitCode);
        Assert.Equal(0, audioResult.ExitCode);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TYPE 4 — MERGED  (hasVideo=true, hasAudio=true, mergeToMp4=true)
    //  Single .mp4 produced by ffmpeg merger; play and delete use FilePath.
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Merged_AllScenarios()
    {
        SkipIfNoYtDlp();

        if (_ffmpegPath is null)
        {
            _out.WriteLine("SKIP: ffmpeg not found — merged download requires ffmpeg");
            return;
        }

        var (info, vFmt, aFmt) = await GetFormatsAsync(needSeparateVideo: true, needAudio: true);
        _out.WriteLine($"Video fmt : {vFmt!.FormatId} {vFmt.QualityLabel} .{vFmt.Container}");
        _out.WriteLine($"Audio fmt : {aFmt!.FormatId} {aFmt.Label} .{aFmt.Container}");

        var template = DownloadNamingService.BuildOutputTemplate(
            _tmpDir,
            hasVideo: true, hasAudio: true, mergeToMp4: true,
            vFmt.QualityLabel, vFmt.Container,
            aFmt.Label, aFmt.Container,
            knownTitle: info.Title);

        var quality = $"{vFmt.FormatId}+{aFmt.FormatId}";

        // For merged, GetActualFilenameAsync resolves against the combined format selector
        var predicted = (!template.Contains('%'))
            ? await MetadataService.GetActualFilenameAsync(_ytDlpPath!, quality, template, TestUrl) ?? template
            : template;

        _out.WriteLine($"Template  : {template}");
        _out.WriteLine($"Predicted : {predicted}");

        Assert.True(!predicted.Contains('%'), $"Merged path contains yt-dlp template: {predicted}");
        Assert.True(predicted.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase),
            $"Merged download must produce .mp4 but predicted: {predicted}");

        // yt-dlp is invoked with the template (same as StartYtDlpDownloadAsync does)
        var result  = await RunYtDlpAsync(quality, "mp4", template);

        _out.WriteLine($"Merger path: {result.MergerPath ?? "(none)"}");

        // A: Persistence contract
        //    For merged, the [Merger] line gives the final path (not [download] Destination:).
        var finalPath = result.MergerPath ?? result.DestinationPath;
        Assert.NotNull(finalPath);
        AssertPathEqual(predicted, finalPath!, "Merger output path");

        // B: Play button
        Assert.True(File.Exists(predicted),
            $"Play button would fail — merged .mp4 not found at:\n  {predicted}");

        // C: Delete dialog
        Assert.True(File.Exists(predicted),
            $"Delete dialog references non-existent file:\n  {predicted}");

        Assert.Equal(0, result.ExitCode);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Infrastructure helpers
    // ══════════════════════════════════════════════════════════════════════════

    private void SkipIfNoYtDlp()
    {
        if (_ytDlpPath is null)
        {
            _out.WriteLine("SKIP: yt-dlp not found in tools\\ or PATH — add it to run integration tests.");
            return;  // test "passes" trivially; CI must have yt-dlp to get real coverage
        }
    }

    // ─────────────────────────────────────────────────────
    // Video info (fetched once and cached for the test run)
    // ─────────────────────────────────────────────────────

    private async Task<(VideoInfoModel Info, VideoFormat? VFmt, AudioFormat? AFmt)> GetFormatsAsync(
        bool needSeparateVideo = false,
        bool needAudio         = false)
    {
        if (_videoInfo is null)
        {
            _out.WriteLine("Fetching video info...");
            _videoInfo = await MetadataService.FetchVideoInfoAsync(TestUrl, _ytDlpPath!);
            if (_videoInfo is null)
            {
                _out.WriteLine("SKIP: could not fetch video info (network?)");
                return (null!, null, null);
            }
            _out.WriteLine($"Title            : {_videoInfo.Title}");
            _out.WriteLine($"HasSeparateStreams: {_videoInfo.HasSeparateStreams}");
            _out.WriteLine($"Video formats    : {_videoInfo.VideoFormats.Length}");
            _out.WriteLine($"Audio formats    : {_videoInfo.AudioFormats.Length}");
        }

        var info = _videoInfo!;

        // Pick the SMALLEST available format to keep tests fast
        VideoFormat? vFmt = null;
        if (needSeparateVideo)
        {
            vFmt = info.VideoFormats
                .Where(f => !f.HasAudio)   // video-only streams only
                .OrderBy(f => ParseQuality(f.QualityLabel))
                .FirstOrDefault();
            if (vFmt is null)
            {
                _out.WriteLine("SKIP: no separate video-only formats for this video");
                return (info, null, null);
            }
        }

        AudioFormat? aFmt = null;
        if (needAudio)
        {
            aFmt = info.AudioFormats
                .OrderBy(f => f.BitrateKbps)  // pick lowest bitrate for speed
                .FirstOrDefault();
            if (aFmt is null)
            {
                _out.WriteLine("SKIP: no audio-only formats for this video");
                return (info, vFmt, null);
            }
        }

        return (info, vFmt, aFmt);
    }

    private static int ParseQuality(string label)
    {
        // "1080p" → 1080, "720p" → 720, etc.
        var s = label.TrimEnd('p');
        return int.TryParse(s, out var n) ? n : 0;
    }

    // ─────────────────────────────────────────────────────
    // yt-dlp process runner
    // ─────────────────────────────────────────────────────

    private sealed record YtDlpResult(
        string? DestinationPath,   // last [download] Destination: captured
        string? MergerPath,        // [Merger] Merging formats into "..."
        int     ExitCode);

    private async Task<YtDlpResult> RunYtDlpAsync(
        string quality,
        string containerFormat,
        string outputTemplate)
    {
        var args = BuildArgs(quality, containerFormat, outputTemplate);
        _out.WriteLine($"\nRunning: yt-dlp {string.Join(" ", args.Select(QuoteArg))}");

        var psi = new ProcessStartInfo(_ytDlpPath!)
        {
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        string? lastDestination = null;
        string? mergerPath      = null;
        var cts = new CancellationTokenSource(DownloadTimeout);

        using var proc = new Process { StartInfo = psi };

        // Wire event handlers BEFORE Start (required by .NET Process API)
        var outputLock = new object();
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _out.WriteLine($"  > {e.Data}");
            lock (outputLock)
            {
                if (e.Data.Contains("[download] Destination:"))
                {
                    var idx = e.Data.IndexOf("[download] Destination: ", StringComparison.Ordinal);
                    if (idx >= 0) lastDestination = e.Data[(idx + 24)..].Trim();
                }
                if (e.Data.Contains("[Merger] Merging formats into"))
                {
                    var s = e.Data.IndexOf('"');
                    var en = e.Data.LastIndexOf('"');
                    if (s >= 0 && en > s) mergerPath = e.Data[(s + 1)..en].Trim();
                }
            }
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) _out.WriteLine($"  ERR> {e.Data}");
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(cts.Token);

        _out.WriteLine($"Exit code       : {proc.ExitCode}");
        _out.WriteLine($"Destination path: {lastDestination ?? "(none)"}");
        _out.WriteLine($"Merger path     : {mergerPath      ?? "(none)"}");

        return new YtDlpResult(lastDestination, mergerPath, proc.ExitCode);
    }

    private static string[] BuildArgs(string quality, string containerFormat, string outputTemplate)
    {
        var list = new List<string> { "--format", $"{quality}/best" };
        if (containerFormat != "native")
            list.AddRange(["--merge-output-format", containerFormat]);
        list.AddRange([
            "--output",               outputTemplate,
            "--progress", "--newline",
            "--no-playlist",
            "--continue",
            "--concurrent-fragments", "1",
            TestUrl,                  // URL must be last
        ]);
        return list.ToArray();
    }

    private static string QuoteArg(string a) =>
        a.Contains(' ') ? $"\"{a}\"" : a;

    // ─────────────────────────────────────────────────────
    // Path helpers
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Normalises path separators and trailing whitespace for comparison.
    /// yt-dlp may emit backslashes or forward slashes depending on platform.
    /// </summary>
    private static string NormalizePath(string p) =>
        p.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    /// <summary>Case-insensitive path equality with a descriptive failure message.</summary>
    private static void AssertPathEqual(string expected, string actual, string context)
    {
        var e = NormalizePath(expected);
        var a = NormalizePath(actual);
        Assert.True(
            string.Equals(e, a, StringComparison.OrdinalIgnoreCase),
            $"{context}\n  Expected : {e}\n  Actual   : {a}");
    }
}

// ─────────────────────────────────────────────────────
// Collection definition — prevents parallel execution of E2E tests
// ─────────────────────────────────────────────────────
[CollectionDefinition("E2eSequential", DisableParallelization = true)]
public class E2eSequentialCollection { }
