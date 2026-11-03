using System;
using System.Collections.Generic;

namespace AnyDownloader.Windows.Models;

/// <summary>
/// Video metadata fetched from yt-dlp --dump-json.
/// Mirrors macOS VideoInfo struct in AnyDownloaderCore.
/// </summary>
public sealed record VideoInfoModel(
    string Title,
    string? ThumbnailUrl,
    string? DurationFormatted,
    string? Uploader,
    VideoFormat[] VideoFormats,
    AudioFormat[] AudioFormats,
    bool HasSeparateStreams);

/// <summary>A single video stream available from yt-dlp.</summary>
public sealed record VideoFormat(
    string FormatId,
    string QualityLabel,   // "1080p", "720p", etc.
    string Container,      // "mp4", "webm", etc.
    string Codec,          // "H.264", "AV1", "VP9", etc.
    long? FilesizeBytes,
    bool HasAudio)         // true for combined streams
{
    public string DisplaySize => FilesizeBytes.HasValue
        ? FormatSize(FilesizeBytes.Value)
        : "Unknown size";

    public string Description => $"{QualityLabel} · {Container.ToUpperInvariant()} · {Codec}"
        + (HasAudio ? " +Audio" : "")
        + (FilesizeBytes.HasValue ? $"  ({DisplaySize})" : "");

    private static string FormatSize(long b) =>
        b >= 1_073_741_824 ? $"{b / 1_073_741_824.0:F2} GB" :
        b >= 1_048_576     ? $"{b / 1_048_576.0:F1} MB" :
        b >= 1_024         ? $"{b / 1_024.0:F0} KB" :
                             $"{b} B";
}

/// <summary>A single audio-only stream available from yt-dlp.</summary>
public sealed record AudioFormat(
    string FormatId,
    string Label,          // e.g. "320 kbps"
    string Container,
    int BitrateKbps,
    long? FilesizeBytes)
{
    public string DisplaySize => FilesizeBytes.HasValue
        ? FormatSize(FilesizeBytes.Value)
        : string.Empty;

    public string Description => $"{Label} · {Container.ToUpperInvariant()}"
        + (FilesizeBytes.HasValue ? $"  ({DisplaySize})" : "");

    private static string FormatSize(long b) =>
        b >= 1_073_741_824 ? $"{b / 1_073_741_824.0:F2} GB" :
        b >= 1_048_576     ? $"{b / 1_048_576.0:F1} MB" :
        b >= 1_024         ? $"{b / 1_024.0:F0} KB" :
                             $"{b} B";
}

/// <summary>Represents a single file entry inside a torrent archive.</summary>
public sealed class TorrentFileEntry
{
    public int Index { get; init; }
    public string Filename { get; init; } = string.Empty;
    public long FilesizeBytes { get; init; }
    public bool IsSelected { get; set; } = true;

    public string ShortName => System.IO.Path.GetFileName(Filename.TrimEnd('/'));
    public string DisplayPath => Filename;

    public string DisplaySize => FormatSize(FilesizeBytes);

    private static string FormatSize(long b) =>
        b >= 1_073_741_824 ? $"{b / 1_073_741_824.0:F2} GB" :
        b >= 1_048_576     ? $"{b / 1_048_576.0:F1} MB" :
        b >= 1_024         ? $"{b / 1_024.0:F0} KB" :
                             $"{b} B";
}

/// <summary>User's confirmed format selection from the FormatPickerDialog.</summary>
public sealed record FormatSelection(
    string? VideoFormatId,
    string? AudioFormatId,
    bool IncludeVideo,
    bool IncludeAudio,
    bool MergeToMp4,
    string? OutputDirectory,
    /// <summary>
    /// Optional yt-dlp output filename (no extension; yt-dlp appends .ext).
    /// Null/empty = use automatic name from yt-dlp template.
    /// </summary>
    string? FilenameOverride = null);

/// <summary>User's confirmed file selection from the TorrentFilePickerDialog.</summary>
public sealed record TorrentSelection(
    IReadOnlyList<int> SelectedIndices,
    string? OutputDirectory);
