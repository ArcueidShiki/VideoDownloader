using System;
using System.Collections.Generic;
using System.IO;
using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

/// <summary>
/// Tests for DownloadNamingService.
///
/// Naming rules (new design — literals preferred over yt-dlp placeholders):
///   merged  (video+audio, mergeToMp4=true)  → {stem}.mp4
///   video-only (separate)                   → {stem} [{quality}] (no audio).{ext}
///   audio-only (separate)                   → {stem} [{bitrate}].{ext}
///   fallback (nothing selected)             → {stem}.%(ext)s
///   custom filename override                → {safe_override}.%(ext)s  (always wins)
///
/// stem  = SanitizeFilename(knownTitle) when knownTitle is supplied,
///         %(title)s otherwise.
/// ext   = literal container when provided, %(ext)s otherwise.
/// </summary>
public class DownloadNamingTests
{
    private const string Dir = @"C:\Downloads";

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — video-only (separate, no audio)
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_VideoOnly_ContainsNoAudioAndQualityLabel()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null);

        // No knownTitle → stem is %(title)s; container known → literal ext
        Assert.Contains("%(title)s", template);
        Assert.Contains("(no audio)", template);
        Assert.Contains("[1080p]", template);
        Assert.EndsWith(".mp4", template);
        Assert.DoesNotContain("%(ext)s", template);
    }

    [Fact]
    public void BuildOutputTemplate_VideoOnly_NoQualityLabel_StillHasNoAudioMarker()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: "webm",
            audioBitrateLabel: null, audioContainer: null);

        Assert.Contains("%(title)s", template);
        Assert.Contains("(no audio)", template);
        Assert.EndsWith(".webm", template);
        Assert.DoesNotContain("[", template); // no quality bracket when label is null
    }

    [Fact]
    public void BuildOutputTemplate_VideoOnly_NullContainer_FallsBackToExtVariable()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "720p", videoContainer: null,
            audioBitrateLabel: null, audioContainer: null);

        // ext unknown → must fall back to %(ext)s
        Assert.Contains("%(ext)s", template);
        Assert.Contains("(no audio)", template);
        Assert.Contains("[720p]", template);
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — audio-only (separate)
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_AudioOnly_ContainsBitrateLabel()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: "320 kbps", audioContainer: "m4a");

        // Bitrate label IS embedded (spaces → underscores via SanitizeLabel)
        Assert.Contains("[320_kbps]", template);
        Assert.Contains("%(title)s", template);
        Assert.EndsWith(".m4a", template);
        Assert.DoesNotContain("%(ext)s", template);
        Assert.DoesNotContain("no audio", template);
    }

    [Fact]
    public void BuildOutputTemplate_AudioOnly_NoBitrate_NoLabelInName()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: null, audioContainer: "m4a");

        var fname = Path.GetFileName(template);
        Assert.Equal("%(title)s.m4a", fname);
    }

    [Fact]
    public void BuildOutputTemplate_AudioOnly_NullContainer_FallsBackToExtVariable()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: "128 kbps", audioContainer: null);

        Assert.Contains("%(ext)s", template);
        Assert.Contains("[128_kbps]", template);
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — merged (video + audio)
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_Merged_UsesMp4Extension()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: true, mergeToMp4: true,
            videoQualityLabel: "1080p", videoContainer: "webm",
            audioBitrateLabel: "320 kbps", audioContainer: "m4a");

        // Merged always produces .mp4 (yt-dlp is given --merge-output-format mp4)
        Assert.Contains("%(title)s", template);
        Assert.EndsWith(".mp4", template);
        Assert.DoesNotContain("%(ext)s", template);
        Assert.DoesNotContain("no audio", template);
        Assert.DoesNotContain("kbps", template);
        Assert.DoesNotContain("1080p", template);
    }

    [Fact]
    public void BuildOutputTemplate_Merged_NoLabels_JustTitleMp4()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: true, mergeToMp4: true,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: null, audioContainer: null);

        var fname = Path.GetFileName(template);
        Assert.Equal("%(title)s.mp4", fname);
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — knownTitle produces literal paths
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_KnownTitle_Merged_IsFullyLiteralPath()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: true, mergeToMp4: true,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: "320 kbps", audioContainer: "m4a",
            knownTitle: "My Great Video");

        // No yt-dlp placeholders — path is fully resolved
        Assert.DoesNotContain("%(", template);
        Assert.Contains("My Great Video", template);
        Assert.EndsWith(".mp4", template);
        Assert.StartsWith(Dir, template);
    }

    [Fact]
    public void BuildOutputTemplate_KnownTitle_VideoOnly_LiteralStemWithNoAudioMarker()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null,
            knownTitle: "My Video");

        Assert.DoesNotContain("%(", template);
        Assert.Contains("My Video", template);
        Assert.Contains("[1080p]", template);
        Assert.Contains("(no audio)", template);
        Assert.EndsWith(".mp4", template);
    }

    [Fact]
    public void BuildOutputTemplate_KnownTitle_AudioOnly_LiteralStemWithBitrateLabel()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: "320 kbps", audioContainer: "m4a",
            knownTitle: "My Song");

        Assert.DoesNotContain("%(", template);
        Assert.Contains("My Song", template);
        Assert.Contains("[320_kbps]", template);
        Assert.EndsWith(".m4a", template);
    }

    [Fact]
    public void BuildOutputTemplate_KnownTitle_SanitizesIllegalChars()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: true, mergeToMp4: true,
            videoQualityLabel: null, videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null,
            knownTitle: "Video: The <Sequel>");

        // Illegal chars replaced with underscores — check filename only (drive colon is fine)
        var fname = Path.GetFileName(template);
        Assert.DoesNotContain(":", fname);
        Assert.DoesNotContain("<", fname);
        Assert.DoesNotContain(">", fname);
        Assert.Contains("Video", fname);
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — separate download collision avoidance
    // ─────────────────────────────────────────────────────

    [Fact]
    public void SeparateDownload_VideoAndAudioTemplates_DoNotCollide()
    {
        var videoTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null,
            knownTitle: "Shared Title");

        var audioTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: "320 kbps", audioContainer: "m4a",
            knownTitle: "Shared Title");

        // Different names — no collision
        Assert.NotEqual(videoTemplate, audioTemplate);
        Assert.Contains("(no audio)", videoTemplate);
        Assert.DoesNotContain("(no audio)", audioTemplate);
        Assert.Contains("[1080p]", videoTemplate);
        Assert.Contains("[320_kbps]", audioTemplate);
    }

    [Fact]
    public void SeparateDownload_WithoutKnownTitle_VideoUsesPlaceholderStem()
    {
        var videoTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null);

        var audioTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: "320 kbps", audioContainer: "m4a");

        // Both still use %(title)s as stem when knownTitle is absent
        Assert.Contains("%(title)s", videoTemplate);
        Assert.Contains("%(title)s", audioTemplate);
        // But they still differ via the "(no audio)" / bitrate markers
        Assert.NotEqual(videoTemplate, audioTemplate);
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — custom filename override
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_CustomFilename_OverridesAllRules()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null,
            filenameOverride: "my custom video");

        // Custom name: no quality label, no "(no audio)", always %(ext)s appended
        Assert.Contains("my custom video", template);
        Assert.DoesNotContain("no audio", template);
        Assert.DoesNotContain("1080p", template);
        Assert.EndsWith(".%(ext)s", template);
    }

    [Fact]
    public void BuildOutputTemplate_CustomFilename_WithExtVariableSuffix()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: true, mergeToMp4: true,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null,
            filenameOverride: "MyVideo");

        Assert.EndsWith(".%(ext)s", template);
        Assert.Contains("MyVideo", template);
    }

    [Fact]
    public void BuildOutputTemplate_CustomFilename_SanitizesIllegalChars()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: null, audioContainer: null,
            filenameOverride: "bad:name/here");

        Assert.DoesNotContain(":", template.Substring(Dir.Length)); // after drive prefix
        Assert.DoesNotContain("/", template.Substring(Dir.Length));
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — fallback (no selection)
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_NoSelection_UsesDefaultTitleExt()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: null, audioContainer: null);

        var fname = Path.GetFileName(template);
        Assert.Equal("%(title)s.%(ext)s", fname);
    }

    [Fact]
    public void BuildOutputTemplate_NoSelection_KnownTitle_UsesTitleWithExtPlaceholder()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: Dir,
            hasVideo: false, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioBitrateLabel: null, audioContainer: null,
            knownTitle: "My Video");

        var fname = Path.GetFileName(template);
        Assert.Equal("My Video.%(ext)s", fname);
    }

    // ─────────────────────────────────────────────────────
    // BuildOutputTemplate — output directory
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BuildOutputTemplate_OutputDirIncludedInTemplate()
    {
        var template = DownloadNamingService.BuildOutputTemplate(
            outputDir: @"C:\MyDownloads",
            hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoQualityLabel: "1080p", videoContainer: "mp4",
            audioBitrateLabel: null, audioContainer: null);

        Assert.StartsWith(@"C:\MyDownloads", template);
    }

    // ─────────────────────────────────────────────────────
    // SanitizeFilename
    // ─────────────────────────────────────────────────────

    [Fact]
    public void SanitizeFilename_InvalidChars_AreReplacedWithUnderscore()
    {
        var result = DownloadNamingService.SanitizeFilename("video<name>:with*invalid?chars");
        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
    }

    [Fact]
    public void SanitizeFilename_ValidChars_ArePreserved()
    {
        var result = DownloadNamingService.SanitizeFilename("My Video 2024");
        Assert.Equal("My Video 2024", result); // spaces OK in file names
    }

    // ─────────────────────────────────────────────────────
    // SanitizeLabel
    // ─────────────────────────────────────────────────────

    [Fact]
    public void SanitizeLabel_SpacesConvertedToUnderscores()
    {
        var result = DownloadNamingService.SanitizeLabel("320 kbps");
        Assert.Equal("320_kbps", result);
    }

    [Fact]
    public void SanitizeLabel_InvalidCharsReplaced()
    {
        var result = DownloadNamingService.SanitizeLabel("1080p/60fps");
        Assert.DoesNotContain("/", result);
    }

    // ─────────────────────────────────────────────────────
    // ResolveUniqueTemplate (legacy helper)
    // ─────────────────────────────────────────────────────

    [Fact]
    public void ResolveUniqueTemplate_NoTitlePlaceholder_ReturnsUnchanged()
    {
        var template = @"C:\Downloads\literal_file.mp4";
        var result = DownloadNamingService.ResolveUniqueTemplate(template, "title");
        Assert.Equal(template, result);
    }

    [Fact]
    public void ResolveUniqueTemplate_WithExtVariable_ReturnsUnchanged()
    {
        // Can't pre-check file existence when extension is unknown
        var template = @"C:\Downloads\%(title)s+1080p.%(ext)s";
        var result = DownloadNamingService.ResolveUniqueTemplate(template, "My Video");
        Assert.Equal(template, result);
    }

    [Fact]
    public void ResolveUniqueTemplate_FileDoesNotExist_ReturnsOriginalTemplate()
    {
        var template = Path.Combine(Path.GetTempPath(), "doesnotexist_%(title)s+1080p.mp4");
        var result = DownloadNamingService.ResolveUniqueTemplate(template, "TestVideo");
        Assert.Equal(template, result);
    }

    [Fact]
    public void ResolveUniqueTemplate_FileExists_AppendsNSuffix()
    {
        var dir      = Path.GetTempPath();
        var title    = $"nttest_{Guid.NewGuid():N}";
        var existing = Path.Combine(dir, $"{title}+1080p.mp4");
        File.WriteAllBytes(existing, Array.Empty<byte>());

        try
        {
            var template = Path.Combine(dir, $"%(title)s+1080p.mp4");
            var result   = DownloadNamingService.ResolveUniqueTemplate(template, title);

            Assert.NotEqual(existing, result);
            Assert.EndsWith(" (2).mp4", result);
        }
        finally
        {
            File.Delete(existing);
        }
    }

    // ─────────────────────────────────────────────────────
    // ComputeFilenameLabel
    // ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeFilenameLabel_FilePath_ReturnsFileName()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: null,
            filePath: @"C:\Downloads\My Video+1080p.mp4",
            filenameOverride: null,
            title: null);
        Assert.Equal("My Video+1080p.mp4", label);
    }

    [Fact]
    public void ComputeFilenameLabel_FilePathIsDirectory_SkipsToTitle()
    {
        var tmpDir = Path.GetTempPath();
        var label  = DownloadNamingService.ComputeFilenameLabel(
            filePaths: null,
            filePath: tmpDir,
            filenameOverride: null,
            title: "My Torrent");
        Assert.Equal("My Torrent", label);
    }

    [Fact]
    public void ComputeFilenameLabel_SingleFilePath_ReturnsFileName()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: new List<string> { @"C:\Downloads\video [1080p] (no audio).mp4" },
            filePath: null,
            filenameOverride: null,
            title: null);
        Assert.Equal("video [1080p] (no audio).mp4", label);
    }

    [Fact]
    public void ComputeFilenameLabel_TwoFilePaths_ShowsPlusNMore()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: new List<string>
            {
                @"C:\Downloads\video [1080p] (no audio).mp4",
                @"C:\Downloads\video [320_kbps].m4a",
            },
            filePath: null,
            filenameOverride: null,
            title: null);
        Assert.Equal("video [1080p] (no audio).mp4 +1 more", label);
    }

    [Fact]
    public void ComputeFilenameLabel_NoPath_FallsBackToTitle()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: null,
            filePath: null,
            filenameOverride: null,
            title: "Some Video Title");
        Assert.Equal("Some Video Title", label);
    }

    [Fact]
    public void ComputeFilenameLabel_FilenameOverride_UsedWhenNoPath()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: null,
            filePath: null,
            filenameOverride: "custom_name",
            title: "title is ignored");
        Assert.Equal("custom_name", label);
    }

    [Fact]
    public void ComputeFilenameLabel_EmptyWhenNothingSet()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: null,
            filePath: null,
            filenameOverride: null,
            title: null);
        Assert.Equal(string.Empty, label);
    }

    [Fact]
    public void ComputeFilenameLabel_FilePathsPrioritisedOverFilePath()
    {
        var label = DownloadNamingService.ComputeFilenameLabel(
            filePaths: new List<string> { @"C:\Downloads\video (no audio).mp4" },
            filePath: @"C:\Downloads\single.mp4", // should be ignored
            filenameOverride: null,
            title: null);
        Assert.Equal("video (no audio).mp4", label);
    }
}
