using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using AnyDownloader.Windows.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AnyDownloader.Windows.Models;

/// <summary>
/// Represents a single download task in the queue.
/// All mutable properties are observable so the UI updates automatically.
/// Mirrors macOS DownloadItem in AnyDownloaderCore.
/// </summary>
public sealed partial class DownloadItemModel : ObservableObject
{
    // ─────────────────────────────────────────────────────
    // Identity (immutable after construction)
    // ─────────────────────────────────────────────────────

    public Guid Id { get; init; } = Guid.NewGuid();
    public string Url { get; }
    public DownloadType Type { get; }
    public string SourcePlatform { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    // ─────────────────────────────────────────────────────
    // Observable state
    // ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(ProgressBrush))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(IsParsingVisible))]
    [NotifyPropertyChangedFor(nameof(IsPauseVisible))]
    [NotifyPropertyChangedFor(nameof(IsResumeVisible))]
    [NotifyPropertyChangedFor(nameof(IsRetryVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlayVisible))]
    [NotifyPropertyChangedFor(nameof(IsStartVisible))]
    [NotifyPropertyChangedFor(nameof(IsOpenFolderVisible))]
    [NotifyPropertyChangedFor(nameof(IsPickOutputDirVisible))]
    [NotifyPropertyChangedFor(nameof(IsSpeedVisible))]
    [NotifyPropertyChangedFor(nameof(IsEtaVisible))]
    [NotifyPropertyChangedFor(nameof(IsProgressVisible))]
    [NotifyPropertyChangedFor(nameof(IsThumbnailVisible))]
    [NotifyPropertyChangedFor(nameof(IsFormatInfoVisible))]
    [NotifyPropertyChangedFor(nameof(IsMetaLabelVisible))]
    private DownloadState _state = DownloadState.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(IsParsingVisible))]
    private double _progress; // 0.0 – 1.0

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
    [NotifyPropertyChangedFor(nameof(IsSpeedVisible))]
    private long _speedBytesPerSec;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EtaText))]
    [NotifyPropertyChangedFor(nameof(IsEtaVisible))]
    private int? _etaSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    [NotifyPropertyChangedFor(nameof(FilenameLabel))]
    private string? _title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilenameLabel))]
    [NotifyPropertyChangedFor(nameof(IsMetaLabelVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlayVisible))]
    private string? _filePath;

    /// <summary>
    /// Paths to all downloaded files.  Set for separate (non-merged) video+audio downloads.
    /// Empty for single-file downloads — use <see cref="FilePath"/> instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilenameLabel))]
    [NotifyPropertyChangedFor(nameof(IsMetaLabelVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlayVisible))]
    [NotifyPropertyChangedFor(nameof(IsSeparateFilesSectionVisible))]
    [NotifyPropertyChangedFor(nameof(SeparateFilesCountLabel))]
    private List<string> _filePaths = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _errorMessage;

    /// <summary>Metadata fetched from yt-dlp.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatInfoText))]
    private VideoInfoModel? _videoInfo;

    /// <summary>Thumbnail image loaded from ThumbnailUrl.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThumbnailVisible))]
    private BitmapImage? _thumbnail;

    /// <summary>User-selected video format ID.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatInfoText))]
    [NotifyPropertyChangedFor(nameof(FormatLabel))]
    private string? _selectedVideoFormatId;

    /// <summary>User-selected audio format ID.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatInfoText))]
    [NotifyPropertyChangedFor(nameof(FormatLabel))]
    private string? _selectedAudioFormatId;

    /// <summary>Whether the merged output should be .mp4.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatLabel))]
    private bool _mergeToMp4 = true;

    /// <summary>Optional custom filename (without extension) chosen by the user in the format picker.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilenameLabel))]
    private string? _filenameOverride;

    /// <summary>List of files in this torrent (populated after metadata fetch).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTorrentSectionVisible))]
    [NotifyPropertyChangedFor(nameof(TorrentFilesCountLabel))]
    private List<TorrentFileEntry> _torrentFiles = new();

    /// <summary>Whether the torrent file list is expanded in the UI.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTorrentFilesListVisible))]
    [NotifyPropertyChangedFor(nameof(TorrentExpanderGlyph))]
    private bool _torrentFilesExpanded;

    /// <summary>Whether the separate video+audio file list is expanded in the UI.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSeparateFilesListVisible))]
    [NotifyPropertyChangedFor(nameof(SeparateFilesExpanderGlyph))]
    private bool _separateFilesExpanded;

    /// <summary>Per-download output directory override. Null = use global default.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathLabel))]
    [NotifyPropertyChangedFor(nameof(IsMetaLabelVisible))]
    private string? _itemOutputDirectory;

    // ─────────────────────────────────────────────────────
    // Commands — set by the ViewModel after construction
    // ─────────────────────────────────────────────────────

    public IRelayCommand PauseCommand { get; init; }
    public IRelayCommand ResumeCommand { get; init; }
    public IRelayCommand RetryCommand { get; init; }
    public IRelayCommand DeleteCommand { get; init; }
    public IRelayCommand OpenFolderCommand { get; init; }
    public IRelayCommand PlayCommand { get; init; }
    public IRelayCommand StartCommand { get; init; }
    public IRelayCommand PickOutputDirCommand { get; init; }

    // ─────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────

    public DownloadItemModel(
        string url,
        DownloadType type,
        IRelayCommand pauseCommand,
        IRelayCommand resumeCommand,
        IRelayCommand retryCommand,
        IRelayCommand deleteCommand,
        IRelayCommand openFolderCommand,
        IRelayCommand playCommand,
        IRelayCommand startCommand,
        IRelayCommand pickOutputDirCommand)
    {
        Url = url;
        Type = type;
        PauseCommand = pauseCommand;
        ResumeCommand = resumeCommand;
        RetryCommand = retryCommand;
        DeleteCommand = deleteCommand;
        OpenFolderCommand = openFolderCommand;
        PlayCommand = playCommand;
        StartCommand = startCommand;
        PickOutputDirCommand = pickOutputDirCommand;
    }

    // ─────────────────────────────────────────────────────
    // Computed display properties
    // ─────────────────────────────────────────────────────

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title! : TruncateUrl(Url);

    public double ProgressPercent => Progress * 100.0;

    public string ProgressLabel => Progress > 0 ? $"{ProgressPercent:F0} %" : string.Empty;

    /// <summary>Platform-aware label for the type badge.</summary>
    public string TypeLabel => SourcePlatform switch
    {
        "youtube"   => "YouTube",
        "tiktok"    => "TikTok",
        "bilibili"  => "Bilibili",
        "douyin"    => "Douyin",
        "vimeo"     => "Vimeo",
        "twitch"    => "Twitch",
        "twitter"   => "X",
        "instagram" => "Instagram",
        "facebook"  => "Facebook",
        _           => Type switch
        {
            DownloadType.YtDlp   => "VIDEO",
            DownloadType.Torrent => "TORRENT",
            _                    => "HTTP",
        },
    };

    /// <summary>Platform-aware accent color for the type badge.</summary>
    public Brush TypeAccentBrush => SourcePlatform switch
    {
        "youtube"   => new SolidColorBrush(Color.FromArgb(255, 255,  0,  0)),
        "tiktok"    => new SolidColorBrush(Color.FromArgb(255,  20, 20, 20)),
        "bilibili"  => new SolidColorBrush(Color.FromArgb(255,   0,160,215)),
        "douyin"    => new SolidColorBrush(Color.FromArgb(255, 254, 44, 85)),
        "vimeo"     => new SolidColorBrush(Color.FromArgb(255,  26,183,234)),
        "twitch"    => new SolidColorBrush(Color.FromArgb(255, 145, 70,255)),
        "twitter"   => new SolidColorBrush(Color.FromArgb(255,  29,161,242)),
        "instagram" => new SolidColorBrush(Color.FromArgb(255, 225, 48,108)),
        "facebook"  => new SolidColorBrush(Color.FromArgb(255,  24,119,242)),
        _           => Type switch
        {
            DownloadType.YtDlp   => new SolidColorBrush(Color.FromArgb(255, 200, 40, 40)),
            DownloadType.Torrent => new SolidColorBrush(Color.FromArgb(255,  40,120,200)),
            _                    => new SolidColorBrush(Color.FromArgb(255,  60,140, 60)),
        },
    };

    public string StatusText => State switch
    {
        DownloadState.Pending        => "Pending",
        DownloadState.FetchingInfo   => "Fetching info…",
        DownloadState.SelectingFormat => "Awaiting format selection…",
        DownloadState.SelectingFiles  => "Awaiting file selection…",
        DownloadState.Queued         => "Queued",
        DownloadState.Active         => Type == DownloadType.Torrent && Progress == 0
                                            ? "Resolving magnet link…"
                                            : "Downloading",
        DownloadState.Paused         => $"Paused{(Progress > 0 ? $" at {ProgressPercent:F0}%" : "")}",
        DownloadState.Completed      => "Completed",
        DownloadState.Failed         => !string.IsNullOrWhiteSpace(ErrorMessage)
                                            ? $"Failed: {ErrorMessage}"
                                            : "Failed",
        _                            => State.ToString(),
    };

    public Brush StatusBrush => State switch
    {
        DownloadState.Completed      => new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
        DownloadState.Failed         => new SolidColorBrush(Color.FromArgb(255, 220,  80,  80)),
        DownloadState.Paused         => new SolidColorBrush(Color.FromArgb(255, 200, 160,  60)),
        DownloadState.SelectingFormat
            or DownloadState.SelectingFiles
                                     => new SolidColorBrush(Color.FromArgb(255, 130, 180, 255)),
        _                            => new SolidColorBrush(Colors.White),
    };

    public Brush ProgressBrush => State switch
    {
        DownloadState.Completed => new SolidColorBrush(Color.FromArgb(255, 100, 200, 100)),
        DownloadState.Failed    => new SolidColorBrush(Color.FromArgb(255, 220,  80,  80)),
        DownloadState.Paused    => new SolidColorBrush(Color.FromArgb(255, 200, 160,  60)),
        _                       => new SolidColorBrush(Color.FromArgb(255,   0, 120, 215)),
    };

    public bool IsIndeterminate =>
        State is DownloadState.FetchingInfo
               or DownloadState.SelectingFormat
               or DownloadState.SelectingFiles
        || (State == DownloadState.Active && Type == DownloadType.Torrent && Progress == 0);

    /// <summary>Shows spinning ProgressRing while a magnet link is being resolved.</summary>
    public Visibility IsParsingVisible =>
        State == DownloadState.Active && Type == DownloadType.Torrent && Progress == 0
            ? Visibility.Visible : Visibility.Collapsed;

    // Visibility helpers
    public Visibility IsPauseVisible =>
        State == DownloadState.Active ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsResumeVisible =>
        State == DownloadState.Paused ? Visibility.Visible : Visibility.Collapsed;

    // Retry for failed; re-download for completed
    public Visibility IsRetryVisible =>
        State is DownloadState.Failed or DownloadState.Completed
            ? Visibility.Visible : Visibility.Collapsed;

    // Open folder: always visible so users can open the save location at any time
    public Visibility IsOpenFolderVisible => Visibility.Visible;

    public Visibility IsPickOutputDirVisible =>
        State is DownloadState.Pending or DownloadState.FetchingInfo
               or DownloadState.SelectingFormat or DownloadState.SelectingFiles
               or DownloadState.Queued or DownloadState.Paused
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsSpeedVisible =>
        State == DownloadState.Active && SpeedBytesPerSec > 0
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsEtaVisible =>
        State == DownloadState.Active && EtaSeconds.HasValue
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsProgressVisible =>
        State is DownloadState.Active or DownloadState.Paused or DownloadState.FetchingInfo
               or DownloadState.SelectingFormat or DownloadState.SelectingFiles
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsThumbnailVisible =>
        Thumbnail is not null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsFormatInfoVisible =>
        !string.IsNullOrEmpty(FormatInfoText) ? Visibility.Visible : Visibility.Collapsed;

    // Torrent file list expand/collapse
    public Visibility IsTorrentSectionVisible =>
        Type == DownloadType.Torrent && TorrentFiles.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsTorrentFilesListVisible =>
        TorrentFilesExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string TorrentExpanderGlyph =>
        TorrentFilesExpanded ? "\uE70E" : "\uE70D"; // ChevronUp / ChevronDown

    public string TorrentFilesCountLabel =>
        TorrentFiles.Count == 1 ? "1 file" : $"{TorrentFiles.Count} files";

    [RelayCommand]
    private void ToggleTorrentFiles() => TorrentFilesExpanded = !TorrentFilesExpanded;

    [RelayCommand]
    private void ToggleSeparateFiles() => SeparateFilesExpanded = !SeparateFilesExpanded;

    // Separate video+audio file list expand/collapse
    public Visibility IsSeparateFilesSectionVisible =>
        FilePaths.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsSeparateFilesListVisible =>
        SeparateFilesExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string SeparateFilesExpanderGlyph =>
        SeparateFilesExpanded ? "\uE70E" : "\uE70D"; // ChevronUp / ChevronDown

    public string SeparateFilesCountLabel =>
        FilePaths.Count == 1 ? "1 file" : $"{FilePaths.Count} files";

    /// <summary>Play button: visible on any completed download (command handles missing-file fallback).</summary>
    public Visibility IsPlayVisible =>
        State == DownloadState.Completed ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Start button: visible when queued, waiting for user to manually begin the download.</summary>
    public Visibility IsStartVisible =>
        State == DownloadState.Queued ? Visibility.Visible : Visibility.Collapsed;

    // ─────────────────────────────────────────────────────
    // Meta labels: path / filename / format
    // ─────────────────────────────────────────────────────

    /// <summary>The save directory for this download (shown as label).</summary>
    public string PathLabel =>
        ItemOutputDirectory
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";

    /// <summary>
    /// Effective filename label shown in the task row chip.
    /// Logic lives in <see cref="DownloadNamingService.ComputeFilenameLabel"/> for testability.
    /// </summary>
    public string FilenameLabel =>
        DownloadNamingService.ComputeFilenameLabel(FilePaths, FilePath, FilenameOverride, Title);

    /// <summary>Selected format description (quality + merge indicator).</summary>
    public string FormatLabel
    {
        get
        {
            if (Type == DownloadType.Torrent) return "Torrent";
            if (VideoInfo is null) return string.Empty;

            var parts = new List<string>();

            if (!string.IsNullOrEmpty(SelectedVideoFormatId))
            {
                var fmt = Array.Find(VideoInfo.VideoFormats, f => f.FormatId == SelectedVideoFormatId);
                if (fmt is not null) parts.Add(fmt.QualityLabel);
            }

            if (!string.IsNullOrEmpty(SelectedAudioFormatId))
            {
                var fmt = Array.Find(VideoInfo.AudioFormats, f => f.FormatId == SelectedAudioFormatId);
                if (fmt is not null) parts.Add(fmt.Label);
            }

            if (parts.Count == 0) return string.Empty;

            var label = string.Join(" + ", parts);
            if (!string.IsNullOrEmpty(SelectedVideoFormatId) &&
                !string.IsNullOrEmpty(SelectedAudioFormatId))
                label += MergeToMp4 ? " → MP4" : " (separate)";

            return label;
        }
    }

    public Visibility IsMetaLabelVisible =>
        !string.IsNullOrEmpty(FilenameLabel) || State != DownloadState.Pending
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility IsFormatLabelVisible =>
        !string.IsNullOrEmpty(FormatLabel) ? Visibility.Visible : Visibility.Collapsed;

    public string SpeedText => FormatSpeed(SpeedBytesPerSec);

    public string EtaText => EtaSeconds.HasValue ? $"ETA {FormatEta(EtaSeconds.Value)}" : string.Empty;

    /// <summary>Secondary info line below title (selected format, duration, etc.).</summary>
    public string FormatInfoText
    {
        get
        {
            if (VideoInfo is null) return string.Empty;

            var parts = new List<string>();

            // Duration
            if (!string.IsNullOrEmpty(VideoInfo.DurationFormatted))
                parts.Add(VideoInfo.DurationFormatted);

            // Uploader
            if (!string.IsNullOrEmpty(VideoInfo.Uploader))
                parts.Add(VideoInfo.Uploader);

            // Selected format
            if (!string.IsNullOrEmpty(SelectedVideoFormatId))
            {
                var fmt = Array.Find(VideoInfo.VideoFormats,
                    f => f.FormatId == SelectedVideoFormatId);
                if (fmt is not null)
                    parts.Add(fmt.QualityLabel + (MergeToMp4 ? " MP4" : ""));
            }

            return string.Join(" · ", parts);
        }
    }

    // ─────────────────────────────────────────────────────
    // Formatting helpers
    // ─────────────────────────────────────────────────────

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec <= 0) return string.Empty;
        if (bytesPerSec >= 1_073_741_824) return $"{bytesPerSec / 1_073_741_824.0:F1} GB/s";
        if (bytesPerSec >= 1_048_576)     return $"{bytesPerSec / 1_048_576.0:F1} MB/s";
        if (bytesPerSec >= 1_024)         return $"{bytesPerSec / 1_024.0:F0} KB/s";
        return $"{bytesPerSec} B/s";
    }

    private static string FormatEta(int seconds)
    {
        if (seconds <= 0) return "0s";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:D2}s";
        return $"{ts.Seconds}s";
    }

    private static string TruncateUrl(string url)
    {
        if (url.Length <= 60) return url;
        return url[..30] + "…" + url[^20..];
    }
}

// DownloadState, DownloadType, and FilterTab are defined in DownloadEnums.cs
