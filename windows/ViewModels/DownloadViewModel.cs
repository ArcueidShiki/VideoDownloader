using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Windows.UI;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using AnyDownloader.Windows.Models;
using AnyDownloader.Windows.Services;

namespace AnyDownloader.Windows.ViewModels;

/// <summary>
/// Central view model that owns the download queue, manages the full state
/// machine (FetchingInfo → SelectingFormat/Files → Active → Completed),
/// and provides tab-based filtering.
///
/// Mirrors the macOS DownloadStore's logic using MVVM + async/await.
/// </summary>
public sealed partial class DownloadViewModel : ObservableObject
{
    // ─────────────────────────────────────────────────────
    // Master queue (all items)
    // ─────────────────────────────────────────────────────

    public ObservableCollection<DownloadItemModel> Downloads { get; } = new();

    // ─────────────────────────────────────────────────────
    // Filtered view (bound to the ListView)
    // ─────────────────────────────────────────────────────

    public ObservableCollection<DownloadItemModel> FilteredDownloads { get; } = new();

    // ─────────────────────────────────────────────────────
    // Tab filter
    // ─────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDownloads))]
    private FilterTab _selectedFilter = FilterTab.All;

    partial void OnSelectedFilterChanged(FilterTab value) => RefreshFilteredDownloads();

    // ─────────────────────────────────────────────────────
    // Status + aggregate speed
    // ─────────────────────────────────────────────────────

    [ObservableProperty] private string _statusSummary = string.Empty;
    [ObservableProperty] private string _aggregateSpeedText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIndicatorColor))]
    private bool _isActive;

    /// <summary>Green when downloading, gray when idle — bound to Ellipse.Fill in XAML.</summary>
    public Color StatusIndicatorColor => IsActive
        ? Color.FromArgb(255, 80, 200, 80)
        : Color.FromArgb(255, 120, 120, 120);

    // Tab label texts (with item counts)
    [ObservableProperty] private string _allTabLabel    = "All";
    [ObservableProperty] private string _activeTabLabel = "Active";
    [ObservableProperty] private string _pausedTabLabel = "Paused";
    [ObservableProperty] private string _doneTabLabel   = "Done";
    [ObservableProperty] private string _torrentsTabLabel = "Torrents";

    // ─────────────────────────────────────────────────────
    // Output directory
    // ─────────────────────────────────────────────────────

    [ObservableProperty] private string _outputDirectory;

    partial void OnOutputDirectoryChanged(string value)
    {
        AppSettings.Instance.OutputDirectory = value;
        AppSettings.Instance.Save();
    }

    // ─────────────────────────────────────────────────────
    // Empty-state visibility
    // ─────────────────────────────────────────────────────

    public Visibility HasNoDownloads =>
        FilteredDownloads.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

    // ─────────────────────────────────────────────────────
    // Process map (id → running process)
    // ─────────────────────────────────────────────────────

    private readonly DownloadProcessRegistry _activeProcesses = new();
    private readonly object _processLock = new();
    private readonly YtDlpRunTracker _ytDlpRuns = new();
    private readonly YtDlpRunTracker _aria2Runs = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _metadataCancellation = new();
    public bool HasActiveOperations
    {
        get
        {
            lock (_processLock)
                return _activeProcesses.Count > 0 || _metadataCancellation.Count > 0 ||
                    Downloads.Any(d => d.State is DownloadState.Active or DownloadState.FetchingInfo or
                        DownloadState.SelectingFormat or DownloadState.SelectingFiles);
        }
    }

    // ─────────────────────────────────────────────────────
    // Persistence
    // ─────────────────────────────────────────────────────

    private readonly DownloadPersistenceService _persistence = new();

    // ─────────────────────────────────────────────────────
    // Dialog bridges (TaskCompletionSource keyed by item ID)
    // ─────────────────────────────────────────────────────

    private readonly Dictionary<Guid, TaskCompletionSource<FormatSelection?>>  _formatTcs  = new();
    private readonly Dictionary<Guid, TaskCompletionSource<TorrentSelection?>> _torrentTcs = new();
    private readonly object _tcsLock = new();

    // ─────────────────────────────────────────────────────
    // In-memory metadata cache (URL → VideoInfo)
    // ─────────────────────────────────────────────────────

    private readonly Dictionary<string, VideoInfoModel> _videoInfoCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    // ─────────────────────────────────────────────────────
    // Events raised when a dialog must be shown
    // ─────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread when a format picker dialog must be shown.</summary>
    public event Action<DownloadItemModel>? FormatPickerRequested;

    /// <summary>Raised on the UI thread when a torrent file picker dialog must be shown.</summary>
    public event Action<DownloadItemModel>? TorrentPickerRequested;

    /// <summary>Raised when the user clicks the per-row output-folder button.</summary>
    public event Action<DownloadItemModel, Action<string?>>? PickItemOutputDirRequested;

    /// <summary>Raised when the user clicks Delete; View shows the confirmation dialog and calls back with the choice.</summary>
    public event Action<DownloadItemModel, Action<DeleteChoice>>? DeleteConfirmRequested;

    /// <summary>Raised when a URL that is already in the queue is added again.</summary>
    public event Action<DownloadItemModel, Action<DuplicateChoice>>? DuplicateDownloadRequested;

    /// <summary>
    /// Raised when the user confirms "Task + file" deletion.  View shows a second dialog
    /// listing every file path that will be deleted; callback receives true = confirmed,
    /// false = cancelled.  The list always contains at least one entry.
    /// </summary>
    public event Action<IReadOnlyList<string>, Action<bool>>? FileDeleteConfirmRequested;

    // ─────────────────────────────────────────────────────
    // Infrastructure
    // ─────────────────────────────────────────────────────

    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _speedTimer;

    // ─────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────

    public DownloadViewModel()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _outputDirectory = AppSettings.Instance.OutputDirectory;

        Downloads.CollectionChanged += (_, _) =>
        {
            RefreshFilteredDownloads();
            RefreshStatusSummary();
        };

        // Poll speed + status every 500 ms (like macOS StatusBar updates)
        _speedTimer = _dispatcher.CreateTimer();
        _speedTimer.Interval = TimeSpan.FromMilliseconds(500);
        _speedTimer.Tick += (_, _) => RefreshStatusSummary();
        _speedTimer.Start();

        // Restore persisted download history (fire-and-forget; UI ready before items load)
        _ = LoadPersistedItemsAsync();
    }

    // ─────────────────────────────────────────────────────
    // Persistence — load on startup
    // ─────────────────────────────────────────────────────

    private async Task LoadPersistedItemsAsync()
    {
        List<PersistedDownloadData> saved;
        try { saved = await _persistence.LoadAllAsync(); }
        catch { return; }

        RunOnUi(async () =>
        {
            foreach (var d in saved)
            {
                try
                {
                    var item = CreateItem(d.Url, d.Type, d.SourcePlatform,
                                         existingId: d.Id, existingCreatedAt: d.CreatedAt);

                    // Restore persisted state (no PropertyChanged during this block)
                    item.State                 = d.State;
                    item.Progress              = d.Progress;
                    item.FilePath              = d.FilePath;
                    if (d.FilePaths is { Count: > 0 })
                        item.FilePaths = d.FilePaths;
                    item.ErrorMessage          = d.ErrorMessage;
                    item.Title                 = d.Title;
                    item.SelectedVideoFormatId = d.SelectedVideoFormatId;
                    item.SelectedAudioFormatId = d.SelectedAudioFormatId;
                    item.MergeToMp4            = d.MergeToMp4;
                    item.ItemOutputDirectory   = d.ItemOutputDirectory;

                    if (d.VideoInfo is not null)
                    {
                        item.VideoInfo = d.VideoInfo;
                        if (!string.IsNullOrEmpty(d.VideoInfo.ThumbnailUrl))
                        {
                            try { item.Thumbnail = new BitmapImage(new Uri(d.VideoInfo.ThumbnailUrl)); }
                            catch { /* bad URL */ }
                        }
                    }

                    if (d.TorrentFiles is { Count: > 0 })
                        item.TorrentFiles = d.TorrentFiles;

                    // Wire property changes AFTER restoring to avoid save-on-load
                    item.PropertyChanged += OnItemPropertyChanged;
                    await _persistence.SaveAsync(item);
                    Downloads.Add(item);
                }
                catch { /* skip corrupt row */ }
            }
        });
    }

    // ─────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Classifies the URL, creates a download item, and starts the appropriate
    /// metadata fetch or direct download flow.
    /// </summary>
    public void AddDownload(string url)
    {
        try
        {
            var trimmed = NormalizeMagnetUri(url.Trim());
            if (string.IsNullOrEmpty(trimmed)) return;

            // Check for an existing download with the same URL
            DownloadItemModel? existing = null;
            RunOnUi(() => existing = Downloads.FirstOrDefault(d => d.Url == trimmed));

            if (existing is not null && DuplicateDownloadRequested is not null)
            {
                RunOnUi(() =>
                {
                    DuplicateDownloadRequested.Invoke(existing, choice =>
                    {
                        switch (choice)
                        {
                            case DuplicateChoice.Cancel:
                                return;
                            case DuplicateChoice.DownloadAgain:
                                // Cancel existing, then start fresh
                                CancelItem(existing);
                                StartNewDownload(trimmed);
                                break;
                            case DuplicateChoice.KeepBoth:
                                // Add a new copy alongside the existing one
                                StartNewDownload(trimmed);
                                break;
                        }
                    });
                });
                return;
            }

            StartNewDownload(trimmed);
        }
        catch (Exception ex)
        {
            RunOnUi(() => StatusSummary = $"Error adding download: {ex.Message}");
        }
    }

    private void StartNewDownload(string trimmed)
    {
        var type     = ClassifyUrl(trimmed);
        var platform = DetectPlatform(trimmed);
        var item     = CreateItem(trimmed, type, platform);

        RunOnUi(() =>
        {
            item.PropertyChanged += OnItemPropertyChanged;
            Downloads.Add(item);
            _ = _persistence.SaveAsync(item); // should save after select the video info
        });

        switch (type)
        {
            case DownloadType.YtDlp:
                _ = FetchYtDlpMetadataAsync(item);
                break;
            case DownloadType.Torrent:
                _ = FetchTorrentMetadataAsync(item);
                break;
            default:
                _ = StartDirectDownloadAsync(item);
                break;
        }
    }

    // ─────────────────────────────────────────────────────
    // Dialog result callbacks (called from MainWindow)
    // ─────────────────────────────────────────────────────

    public void ConfirmFormat(Guid itemId, FormatSelection selection)
    {
        TaskCompletionSource<FormatSelection?>? tcs;
        lock (_tcsLock)
        {
            _formatTcs.TryGetValue(itemId, out tcs);
            _formatTcs.Remove(itemId);
        }
        tcs?.TrySetResult(selection);
    }

    public void CancelFormatPicker(Guid itemId)
    {
        TaskCompletionSource<FormatSelection?>? tcs;
        lock (_tcsLock)
        {
            _formatTcs.TryGetValue(itemId, out tcs);
            _formatTcs.Remove(itemId);
        }
        tcs?.TrySetResult(null);
    }

    public void ConfirmTorrentFiles(Guid itemId, TorrentSelection selection)
    {
        TaskCompletionSource<TorrentSelection?>? tcs;
        lock (_tcsLock)
        {
            _torrentTcs.TryGetValue(itemId, out tcs);
            _torrentTcs.Remove(itemId);
        }
        tcs?.TrySetResult(selection);
    }

    public void CancelTorrentPicker(Guid itemId)
    {
        TaskCompletionSource<TorrentSelection?>? tcs;
        lock (_tcsLock)
        {
            _torrentTcs.TryGetValue(itemId, out tcs);
            _torrentTcs.Remove(itemId);
        }
        tcs?.TrySetResult(null);
    }

    // ─────────────────────────────────────────────────────
    // Bulk relay commands
    // ─────────────────────────────────────────────────────

    [RelayCommand]
    private void StartAll()
    {
        foreach (var item in Downloads.ToArray())
        {
            if (item.State is DownloadState.Queued or DownloadState.Paused)
                _ = RestartAfterPauseAsync(item);
        }
    }

    [RelayCommand]
    private void PauseAll()
    {
        foreach (var item in Downloads.ToArray())
        {
            if (item.State == DownloadState.Active)
                PauseItem(item);
        }
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        var done = Downloads.Where(d => d.State is DownloadState.Completed or DownloadState.Failed).ToArray();
        RunOnUi(() =>
        {
            foreach (var item in done)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
                Downloads.Remove(item);
                _ = _persistence.DeleteAsync(item.Id);
            }
        });
    }

    // ─────────────────────────────────────────────────────
    // Metadata fetch — yt-dlp
    // ─────────────────────────────────────────────────────

    private async Task FetchYtDlpMetadataAsync(DownloadItemModel item)
    {
        if (BlockIncompatibleVideoDependency(item)) return;
        using var cancellation = new CancellationTokenSource();
        lock (_processLock)
        {
            if (_metadataCancellation.Remove(item.Id, out var previous)) previous.Cancel();
            _metadataCancellation[item.Id] = cancellation;
        }
        var ct = cancellation.Token;
        RunOnUi(() => item.State = DownloadState.FetchingInfo);

        try
        {
            var ytDlpPath = BinaryResolver.FindYtDlp();
            if (ytDlpPath is null)
            {
                RunOnUi(() =>
                {
                    item.State = DownloadState.Failed;
                    item.ErrorMessage = "yt-dlp not found. Add it to the tools\\ folder or install it.";
                });
                return;
            }

            // Check in-memory cache first; avoids a slow yt-dlp --dump-json call for known URLs
            VideoInfoModel? videoInfo;
            lock (_cacheLock)
                _videoInfoCache.TryGetValue(item.Url, out videoInfo);

            if (videoInfo is null)
            {
                videoInfo = await MetadataService.FetchVideoInfoAsync(item.Url, ytDlpPath, ct);
                if (videoInfo is not null)
                    lock (_cacheLock)
                        _videoInfoCache[item.Url] = videoInfo;
            }

            ct.ThrowIfCancellationRequested();
            if (videoInfo is null) throw new InvalidOperationException("yt-dlp returned no video metadata.");

            // Set up the TCS before raising the event to avoid a race
            var tcs = new TaskCompletionSource<FormatSelection?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_tcsLock)
                _formatTcs[item.Id] = tcs;

            RunOnUi(() =>
            {
                if (ct.IsCancellationRequested) return;
                item.VideoInfo = videoInfo;
                item.Title     = videoInfo.Title;

                if (!string.IsNullOrEmpty(videoInfo.ThumbnailUrl))
                {
                    try { item.Thumbnail = new BitmapImage(new Uri(videoInfo.ThumbnailUrl)); }
                    catch { /* ignore bad URL */ }
                }

                item.State = DownloadState.SelectingFormat;
                FormatPickerRequested?.Invoke(item); // MainWindow shows ContentDialog
            });

            var selection = await tcs.Task.WaitAsync(ct);

            if (selection is null)
            {
                // User cancelled
                RunOnUi(() =>
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    Downloads.Remove(item);
                });
                await _persistence.DeleteAsync(item.Id);
                return;
            }

            // Persist the user's choice before the optional filename network probe.
            // A failed probe must not turn Retry into a default-format/default-folder download.
            await RunOnUiAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                item.SelectedVideoFormatId = selection.VideoFormatId;
                item.SelectedAudioFormatId = selection.AudioFormatId;
                item.MergeToMp4 = selection.MergeToMp4;
                item.FilenameOverride = selection.FilenameOverride;
                item.ItemOutputDirectory = string.IsNullOrEmpty(selection.OutputDirectory)
                    ? item.ItemOutputDirectory ?? OutputDirectory : selection.OutputDirectory;
            });
            await _persistence.SaveAsync(item);

            // ── Resolve exact output paths via yt-dlp --print filename --skip-download ─
            // DownloadNamingService builds the --output TEMPLATE (same args as the real
            // download call).  yt-dlp resolves that template with its own sanitisation
            // rules (stripping CJK characters, applying OS restrictions, etc.) and prints
            // the final path without downloading anything.  This is the only reliable way
            // to know where the file will actually land.
            var ytDlpPath2 = BinaryResolver.FindYtDlp();   // already checked non-null above
            var outDir  = string.IsNullOrEmpty(selection.OutputDirectory)
                ? (item.ItemOutputDirectory ?? OutputDirectory)
                : selection.OutputDirectory;
            bool hasVid = !string.IsNullOrEmpty(selection.VideoFormatId);
            bool hasAud = !string.IsNullOrEmpty(selection.AudioFormatId);
            var vFmt    = item.VideoInfo?.VideoFormats.FirstOrDefault(f => f.FormatId == selection.VideoFormatId);
            var aFmt    = item.VideoInfo?.AudioFormats.FirstOrDefault(f => f.FormatId == selection.AudioFormatId);

            string?       predictedFilePath  = null;
            List<string>? predictedFilePaths = null;

            if (hasVid && hasAud && !selection.MergeToMp4)
            {
                // Separate mode: ask yt-dlp to resolve each template independently
                var vTemplate = DownloadNamingService.BuildOutputTemplate(
                    outDir, hasVideo: true, hasAudio: false, mergeToMp4: false,
                    vFmt?.QualityLabel, vFmt?.Container,
                    audioBitrateLabel: null, audioContainer: null,
                    selection.FilenameOverride, keepBoth: true, knownTitle: item.Title);
                var aTemplate = DownloadNamingService.BuildOutputTemplate(
                    outDir, hasVideo: false, hasAudio: true, mergeToMp4: false,
                    videoQualityLabel: null, videoContainer: null,
                    aFmt?.Label, aFmt?.Container,
                    selection.FilenameOverride, keepBoth: true, knownTitle: item.Title);
                var vActual = !vTemplate.Contains('%')
                    ? await MetadataService.GetActualFilenameAsync(ytDlpPath2!, selection.VideoFormatId!, vTemplate, item.Url, ct)
                    : null;
                var aActual = !aTemplate.Contains('%')
                    ? await MetadataService.GetActualFilenameAsync(ytDlpPath2!, selection.AudioFormatId!, aTemplate, item.Url, ct)
                    : null;
                var paths = new List<string>();
                if (vActual is not null) paths.Add(vActual);
                if (aActual is not null) paths.Add(aActual);
                if (paths.Count > 0) predictedFilePaths = paths;
            }
            else
            {
                // Merged or single-stream: one file
                var template = DownloadNamingService.BuildOutputTemplate(
                    outDir, hasVid, hasAud, selection.MergeToMp4,
                    vFmt?.QualityLabel, vFmt?.Container,
                    aFmt?.Label, aFmt?.Container,
                    selection.FilenameOverride, keepBoth: true, knownTitle: item.Title);
                var quality = hasVid && hasAud
                    ? $"{selection.VideoFormatId}+{selection.AudioFormatId}"
                    : (hasVid ? selection.VideoFormatId! : selection.AudioFormatId!);
                if (!template.Contains('%'))
                    predictedFilePath = await MetadataService.GetActualFilenameAsync(ytDlpPath2!, quality, template, item.Url, ct);
                else
                    predictedFilePath = null;
            }

            // ── Apply to item on UI thread (awaited — guarantees completion) ─────────
            await RunOnUiAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (predictedFilePath  is not null) item.FilePath  = predictedFilePath;
                if (predictedFilePaths is not null) item.FilePaths = predictedFilePaths;
                item.State = DownloadState.Queued;
            });

            // ── Persist immediately - paths are now set ──────────────────────────────
            await _persistence.SaveAsync(item);
            // Download stays Queued - user starts it manually via StartCommand.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                if (ct.IsCancellationRequested) return;
                item.State        = DownloadState.Failed;
                item.ErrorMessage = ex.Message;
            });
        }
        finally
        {
            lock (_processLock)
                if (_metadataCancellation.TryGetValue(item.Id, out var current) && ReferenceEquals(current, cancellation))
                    _metadataCancellation.Remove(item.Id);
            RefreshStatusSummary();
        }
    }

    // ─────────────────────────────────────────────────────
    // Metadata fetch — torrent
    // ─────────────────────────────────────────────────────

    private async Task FetchTorrentMetadataAsync(DownloadItemModel item)
    {
        RunOnUi(() => item.State = DownloadState.FetchingInfo);

        try
        {
            var aria2cPath = BinaryResolver.FindAria2c();
            if (aria2cPath is null)
            {
                RunOnUi(() =>
                {
                    item.State        = DownloadState.Failed;
                    item.ErrorMessage = "aria2c not found. Add it to the tools\\ folder or install it.";
                });
                return;
            }

            // Magnet links: resolve metadata separately, then show file picker.
            if (item.Url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                await FetchMagnetMetadataAsync(item, aria2cPath);
                return;
            }

            var files = await MetadataService.FetchTorrentFilesAsync(item.Url, aria2cPath);

            var tcs = new TaskCompletionSource<TorrentSelection?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_tcsLock)
                _torrentTcs[item.Id] = tcs;

            RunOnUi(() =>
            {
                item.TorrentFiles = new List<TorrentFileEntry>(files);
                item.State        = DownloadState.SelectingFiles;
                TorrentPickerRequested?.Invoke(item);
            });

            var selection = await tcs.Task;

            if (selection is null)
            {
                RunOnUi(() =>
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    Downloads.Remove(item);
                });
                return;
            }

            RunOnUi(() =>
            {
                if (!string.IsNullOrEmpty(selection.OutputDirectory))
                    item.ItemOutputDirectory = selection.OutputDirectory;
                item.State = DownloadState.Queued;
                // Download stays Queued — user starts it manually via StartCommand.
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                item.State        = DownloadState.Failed;
                item.ErrorMessage = ex.Message;
            });
        }
        finally
        {
            RefreshStatusSummary();
        }
    }

    // ─────────────────────────────────────────────────────
    // Magnet metadata (extracted so variable names don't
    // shadow the ones in FetchTorrentMetadataAsync)
    // ─────────────────────────────────────────────────────

    private async Task FetchMagnetMetadataAsync(DownloadItemModel item, string aria2cPath)
    {
        // Show "Resolving magnet link…" spinner while we wait for DHT peers
        RunOnUi(() => item.State = DownloadState.FetchingInfo);

        var metadata = await MagnetMetadataFetcher.FetchAsync(
            item.Url, aria2cPath, TimeSpan.FromSeconds(45));

        if (metadata is not null && metadata.Files.Count > 0)
        {
            // Metadata resolved — show the file picker
            var tcs = new TaskCompletionSource<TorrentSelection?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_tcsLock)
                _torrentTcs[item.Id] = tcs;

            RunOnUi(() =>
            {
                item.Title        = metadata.Name;
                item.TorrentFiles = metadata.Files.ToList();
                item.State        = DownloadState.SelectingFiles;
                TorrentPickerRequested?.Invoke(item);
            });

            var selection = await tcs.Task;

            if (selection is null)
            {
                RunOnUi(() =>
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                    Downloads.Remove(item);
                });
                return;
            }

            RunOnUi(() =>
            {
                if (!string.IsNullOrEmpty(selection.OutputDirectory))
                    item.ItemOutputDirectory = selection.OutputDirectory;
                item.State = DownloadState.Queued;
                // Download stays Queued — user starts it manually via StartCommand.
            });
        }
        else
        {
            // Timed out — queue without file list; aria2c resolves the magnet during download.
            // User starts it manually via StartCommand (good opportunity to change the directory).
            RunOnUi(() => item.State = DownloadState.Queued);
        }
    }

    // ─────────────────────────────────────────────────────
    // yt-dlp download
    // ─────────────────────────────────────────────────────

    private Task StartYtDlpDownloadAsync(DownloadItemModel item)
    {
        var ytDlpPath = BinaryResolver.FindYtDlp();
        if (ytDlpPath is null)
        {
            RunOnUi(() => { item.State = DownloadState.Failed; item.ErrorMessage = "yt-dlp not found."; });
            return Task.CompletedTask;
        }

        var outputDir = item.ItemOutputDirectory ?? OutputDirectory;

        // Build quality selector and container format from user's explicit choices.
        // Rules:
        //   video + audio + merge  → "videoId+audioId", containerFormat = mp4/mkv
        //   video only             → "videoId",         containerFormat = native (no remux)
        //   audio only             → "audioId",         containerFormat = native
        //   nothing selected       → fall back to best available
        bool hasVideo = !string.IsNullOrEmpty(item.SelectedVideoFormatId);
        bool hasAudio = !string.IsNullOrEmpty(item.SelectedAudioFormatId);

        var videoFmt = item.VideoInfo?.VideoFormats.FirstOrDefault(f => f.FormatId == item.SelectedVideoFormatId);
        var audioFmt = item.VideoInfo?.AudioFormats.FirstOrDefault(f => f.FormatId == item.SelectedAudioFormatId);

        if (hasVideo && hasAudio && !item.MergeToMp4)
        {
            // Download as two separate files (no ffmpeg merge)
            return StartYtDlpSeparateAsync(item, ytDlpPath, outputDir);
        }

        // Build quality selector and container format
        string quality;
        string containerFormat;

        if (hasVideo && hasAudio)
        {
            quality         = $"{item.SelectedVideoFormatId}+{item.SelectedAudioFormatId}";
            containerFormat = "mp4";
        }
        else if (hasVideo)
        {
            quality         = item.SelectedVideoFormatId!;
            containerFormat = "native";
        }
        else if (hasAudio)
        {
            quality         = item.SelectedAudioFormatId!;
            containerFormat = "native";
        }
        else
        {
            quality         = AppSettings.Instance.DefaultQuality;
            containerFormat = "mp4";
        }

        // Apply naming convention output template
        var outputTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir, hasVideo, hasAudio, item.MergeToMp4,
            videoFmt?.QualityLabel, videoFmt?.Container,
            audioFmt?.Label,        audioFmt?.Container,
            item.FilenameOverride, keepBoth: true, knownTitle: item.Title);

        var args = DownloadEngine.BuildYtDlpArgs(
            url:            item.Url,
            quality:        quality,
            containerFormat: containerFormat,
            outputTemplate: outputTemplate);

        return RunYtDlpAsync(item, ytDlpPath, args);
    }

    private Task RunYtDlpAsync(DownloadItemModel item, string ytDlpPath, string[] args)
        => RunYtDlpSequenceAsync(item, ytDlpPath, [args]);

    private async Task RunYtDlpSequenceAsync(DownloadItemModel item, string ytDlpPath, string[][] phases)
    {
        if (BlockIncompatibleVideoDependency(item)) return;
        var attempt = await _ytDlpRuns.BeginAsync(item.Id);
        if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
        RunOnUi(() =>
        {
            if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
            item.State = DownloadState.Active;
            item.ErrorMessage = null;
        });
        var paths = new List<string>();
        try
        {
            for (int phase = 0; phase < phases.Length; phase++)
            {
                var phaseIndex = phase;
                var output = new YtDlpOutput();
                Process proc;
                Task<int> exitTask;
                lock (_processLock)
                {
                    if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
                    (proc, exitTask) = DownloadEngine.StartYtDlpProcess(ytDlpPath, phases[phase], line =>
                    {
                        if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
                        output.Observe(line);
                        var update = DownloadEngine.ParseYtDlpProgress(line);
                        if (update is not null) RunOnUi(() =>
                        {
                            if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
                            item.Progress = (phaseIndex + update.Progress) / phases.Length;
                            item.SpeedBytesPerSec = update.SpeedBytesPerSec;
                            item.EtaSeconds = update.EtaSeconds;
                        });
                    });
                    _activeProcesses.Track(item.Id, proc);
                    _ytDlpRuns.Track(item.Id, attempt, exitTask);
                }
                var exitCode = await _activeProcesses.WaitAndReleaseAsync(item.Id, proc, exitTask);
                if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
                if (exitCode != 0) throw new InvalidOperationException(output.Error(exitCode));
                if (output.FinalPath is not string finalPath || !File.Exists(finalPath))
                    throw new InvalidOperationException("yt-dlp did not produce a completed output file. " + output.Error(exitCode));
                paths.Add(finalPath);
            }
            await RunOnUiAsync(() =>
            {
                if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
                item.State = DownloadState.Completed;
                item.Progress = 1;
                item.FilePath = paths[0];
                if (paths.Count > 1) item.FilePaths = paths;
                item.Title ??= Path.GetFileNameWithoutExtension(paths[0]);
                item.SpeedBytesPerSec = 0;
                item.EtaSeconds = null;
            });
            if (_ytDlpRuns.IsCurrent(item.Id, attempt)) await _persistence.SaveAsync(item);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (!_ytDlpRuns.IsCurrent(item.Id, attempt)) return;
                item.State = DownloadState.Failed;
                item.ErrorMessage = ex.Message;
                item.SpeedBytesPerSec = 0;
                item.EtaSeconds = null;
            });
            if (_ytDlpRuns.IsCurrent(item.Id, attempt)) await _persistence.SaveAsync(item);
        }
        finally { RefreshStatusSummary(); }
    }

    private Task StartYtDlpSeparateAsync(DownloadItemModel item, string ytDlpPath, string outputDir)
    {
        var videoFmt = item.VideoInfo?.VideoFormats
            .FirstOrDefault(f => f.FormatId == item.SelectedVideoFormatId);
        var audioFmt = item.VideoInfo?.AudioFormats
            .FirstOrDefault(f => f.FormatId == item.SelectedAudioFormatId);

        // Use naming convention: video-only = title+1080p+(no audio).ext
        //                        audio-only = title+128kbps.ext
        var videoTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir, hasVideo: true, hasAudio: false, mergeToMp4: false,
            videoFmt?.QualityLabel, videoFmt?.Container,
            audioBitrateLabel: null, audioContainer: null,
            item.FilenameOverride, keepBoth: true, knownTitle: item.Title);
        var audioTemplate = DownloadNamingService.BuildOutputTemplate(
            outputDir, hasVideo: false, hasAudio: true, mergeToMp4: false,
            videoQualityLabel: null, videoContainer: null,
            audioFmt?.Label, audioFmt?.Container,
            item.FilenameOverride, keepBoth: true, knownTitle: item.Title);

        var videoArgs = DownloadEngine.BuildYtDlpArgs(
            url: item.Url, quality: item.SelectedVideoFormatId!,
            containerFormat: "native", outputTemplate: videoTemplate);
        var audioArgs = DownloadEngine.BuildYtDlpArgs(
            url: item.Url, quality: item.SelectedAudioFormatId!,
            containerFormat: "native", outputTemplate: audioTemplate);

        return RunYtDlpDualAsync(item, ytDlpPath, outputDir, videoArgs, audioArgs);
    }

    /// <summary>Runs yt-dlp twice: video first (progress 0-50%), then audio (50-100%).</summary>
    private Task RunYtDlpDualAsync(
        DownloadItemModel item, string ytDlpPath, string outputDir,
        string[] videoArgs, string[] audioArgs)
        => RunYtDlpSequenceAsync(item, ytDlpPath, [videoArgs, audioArgs]);

    // Naming helpers live in DownloadNamingService (testable static class).

    // ─────────────────────────────────────────────────────
    // Torrent / HTTP download (aria2c)
    // ─────────────────────────────────────────────────────

    private Task StartTorrentDownloadAsync(DownloadItemModel item, TorrentSelection selection)
    {
        var aria2cPath = BinaryResolver.FindAria2c();
        if (aria2cPath is null)
        {
            RunOnUi(() => { item.State = DownloadState.Failed; item.ErrorMessage = "aria2c not found."; });
            return Task.CompletedTask;
        }

        var outputDir = item.ItemOutputDirectory ?? OutputDirectory;

        // Build --select-file argument for chosen indices
        var selectedFiles = selection.SelectedIndices.Count > 0
            ? string.Join(",", selection.SelectedIndices)
            : null;

        var args = DownloadEngine.BuildAria2cArgs(item.Url, outputDir: outputDir, selectedFiles: selectedFiles);
        return RunAria2cAsync(item, aria2cPath, args);
    }

    private Task StartDirectDownloadAsync(DownloadItemModel item)
    {
        var aria2cPath = BinaryResolver.FindAria2c();
        if (aria2cPath is null)
        {
            RunOnUi(() => { item.State = DownloadState.Failed; item.ErrorMessage = "aria2c not found."; });
            return Task.CompletedTask;
        }

        var outputDir = item.ItemOutputDirectory ?? OutputDirectory;
        var args      = DownloadEngine.BuildAria2cArgs(item.Url, outputDir: outputDir);
        return RunAria2cAsync(item, aria2cPath, args);
    }

    private async Task RunAria2cAsync(DownloadItemModel item, string aria2cPath, string[] args)
    {
        var attempt = await _aria2Runs.BeginAsync(item.Id);
        if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
        try
        {
            await RunOnUiAsync(() =>
            {
                if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                item.State = DownloadState.Active;
                item.ErrorMessage = null;
            });
            Process proc;
            Task<int> exitTask;
            lock (_processLock)
            {
                if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                (proc, exitTask) = DownloadEngine.StartAria2cProcess(aria2cPath, args,
                onOutput: line =>
                {
                    if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                    // aria2c prefixes lines with "MM/DD HH:MM:SS NOTICE " so we
                    // cannot use StartsWith — use Contains and locate the marker.
                    const string completeMarker = "Download complete:";
                    var markerIdx = line.IndexOf(completeMarker, StringComparison.OrdinalIgnoreCase);
                    if (markerIdx >= 0)
                    {
                        var path = line[(markerIdx + completeMarker.Length)..].Trim();
                        if (!string.IsNullOrEmpty(path))
                            RunOnUi(() =>
                            {
                                if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                                item.FilePath ??= path;
                                item.Title    ??= Path.GetFileNameWithoutExtension(path);
                            });
                    }

                    var update = DownloadEngine.ParseAria2cProgress(line);
                    if (update is not null)
                        RunOnUi(() =>
                        {
                            if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                            item.Progress         = update.Progress;
                            item.SpeedBytesPerSec = update.SpeedBytesPerSec;
                            item.EtaSeconds       = update.EtaSeconds;
                        });
                });
                _activeProcesses.Track(item.Id, proc);
                _aria2Runs.Track(item.Id, attempt, exitTask);
            }
            var exitCode = await _activeProcesses.WaitAndReleaseAsync(item.Id, proc, exitTask);
            await RunOnUiAsync(() =>
            {
                if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                item.SpeedBytesPerSec = 0;
                item.EtaSeconds = null;
                if (exitCode == 0)
                {
                    item.State            = DownloadState.Completed;
                    item.Progress         = 1.0;
                    // Torrent downloads may not emit "Download complete: <path>" for every file;
                    // fall back to the output directory so the folder/play buttons always work.
                    if (string.IsNullOrEmpty(item.FilePath))
                        item.FilePath = item.ItemOutputDirectory ?? OutputDirectory;
                    item.SpeedBytesPerSec = 0;
                    item.EtaSeconds       = null;
                }
                else
                {
                    item.State        = DownloadState.Failed;
                    item.ErrorMessage = $"aria2c exited with code {exitCode}";
                }
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                if (!_aria2Runs.IsCurrent(item.Id, attempt)) return;
                item.State = DownloadState.Failed;
                item.ErrorMessage = ex.Message;
                item.SpeedBytesPerSec = 0;
                item.EtaSeconds = null;
            });
        }
        finally
        {
            RefreshStatusSummary();
        }
    }

    // ─────────────────────────────────────────────────────
    // Pause / cancel helpers
    // ─────────────────────────────────────────────────────

    private void PauseItem(DownloadItemModel item)
    {
        RunOnUi(() => { item.State = DownloadState.Paused; item.SpeedBytesPerSec = 0; item.EtaSeconds = null; });
        KillProcess(item.Id);
    }

    internal void CancelItem(DownloadItemModel item)
    {
        KillProcess(item.Id);
        _ = _persistence.DeleteAsync(item.Id);
        RunOnUi(() =>
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            Downloads.Remove(item);
        });
        // Also resolve any pending dialog TCS
        CancelFormatPicker(item.Id);
        CancelTorrentPicker(item.Id);
    }

    private void KillProcess(Guid id)
    {
        lock (_processLock)
        {
            _ytDlpRuns.Cancel(id);
            _aria2Runs.Cancel(id);
            if (_metadataCancellation.TryGetValue(id, out var cancellation)) cancellation.Cancel();
            _activeProcesses.Kill(id);
        }
    }

    private bool BlockIncompatibleVideoDependency(DownloadItemModel item)
    {
        if (DependencyUpdateCoordinator.VideoBlockReason is not string reason) return false;
        RunOnUi(() => { item.State = DownloadState.Failed; item.ErrorMessage = reason; });
        return true;
    }

    // ─────────────────────────────────────────────────────
    // Filter
    // ─────────────────────────────────────────────────────

    private void RefreshFilteredDownloads()
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(RefreshFilteredDownloads);
            return;
        }

        FilteredDownloads.Clear();
        foreach (var item in Downloads.OrderByDescending(i => i.CreatedAt))
        {
            if (MatchesFilter(item))
                FilteredDownloads.Add(item);
        }

        OnPropertyChanged(nameof(HasNoDownloads));
    }

    private bool MatchesFilter(DownloadItemModel item) => SelectedFilter switch
    {
        FilterTab.Active   => item.State is DownloadState.Active or DownloadState.FetchingInfo
                                          or DownloadState.SelectingFormat or DownloadState.SelectingFiles
                                          or DownloadState.Queued,
        FilterTab.Paused   => item.State == DownloadState.Paused,
        FilterTab.Done     => item.State is DownloadState.Completed or DownloadState.Failed,
        FilterTab.Torrents => item.Type  == DownloadType.Torrent,
        _                  => true, // All
    };

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DownloadItemModel item) return;

        switch (e.PropertyName)
        {
            case nameof(DownloadItemModel.State):
                RefreshFilteredDownloads();
                RefreshStatusSummary();
                _ = _persistence.SaveAsync(item);
                break;

            // Save when rich metadata or user selections change
            case nameof(DownloadItemModel.FilePath):
            case nameof(DownloadItemModel.FilePaths):
            case nameof(DownloadItemModel.VideoInfo):
            case nameof(DownloadItemModel.TorrentFiles):
            case nameof(DownloadItemModel.SelectedVideoFormatId):
            case nameof(DownloadItemModel.SelectedAudioFormatId):
            case nameof(DownloadItemModel.ItemOutputDirectory):
            case nameof(DownloadItemModel.Title):
                _ = _persistence.SaveAsync(item);
                break;
        }
    }

    // ─────────────────────────────────────────────────────
    // Status summary + aggregate speed
    // ─────────────────────────────────────────────────────

    private void RefreshStatusSummary()
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(RefreshStatusSummary);
            return;
        }

        var active    = Downloads.Count(d => d.State == DownloadState.Active);
        var paused    = Downloads.Count(d => d.State == DownloadState.Paused);
        var completed = Downloads.Count(d => d.State == DownloadState.Completed);
        var failed    = Downloads.Count(d => d.State == DownloadState.Failed);
        var torrents  = Downloads.Count(d => d.Type  == DownloadType.Torrent);
        var total     = Downloads.Count;

        IsActive      = active > 0;
        StatusSummary = total == 0
            ? "No downloads"
            : $"{active} active · {paused} paused · {completed} done";

        // Aggregate speed across active downloads
        var totalSpeed = Downloads.Where(d => d.State == DownloadState.Active)
                                  .Sum(d => d.SpeedBytesPerSec);
        AggregateSpeedText = totalSpeed > 0 ? FormatSpeed(totalSpeed) : string.Empty;

        // Update tab labels with counts
        AllTabLabel      = total    > 0 ? $"All ({total})"         : "All";
        ActiveTabLabel   = active   > 0 ? $"Active ({active})"     : "Active";
        PausedTabLabel   = paused   > 0 ? $"Paused ({paused})"     : "Paused";
        DoneTabLabel     = (completed + failed) > 0
                                ? $"Done ({completed + failed})"   : "Done";
        TorrentsTabLabel = torrents > 0 ? $"Torrents ({torrents})" : "Torrents";
    }

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec >= 1_073_741_824) return $"{bytesPerSec / 1_073_741_824.0:F1} GB/s";
        if (bytesPerSec >= 1_048_576)     return $"{bytesPerSec / 1_048_576.0:F1} MB/s";
        if (bytesPerSec >= 1_024)         return $"{bytesPerSec / 1_024.0:F0} KB/s";
        return $"{bytesPerSec} B/s";
    }

    // ─────────────────────────────────────────────────────
    // URL classification + platform detection
    // ─────────────────────────────────────────────────────

    private static readonly HashSet<string> _ytDlpHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com","www.youtube.com","youtu.be","m.youtube.com",
        "tiktok.com","www.tiktok.com","vm.tiktok.com",
        "bilibili.com","www.bilibili.com",
        "douyin.com","www.douyin.com",
        "vimeo.com","www.vimeo.com",
        "twitch.tv","www.twitch.tv","clips.twitch.tv",
        "twitter.com","x.com","www.twitter.com",
        "instagram.com","www.instagram.com",
        "facebook.com","www.facebook.com","fb.watch",
        "dailymotion.com","www.dailymotion.com",
        "nicovideo.jp","www.nicovideo.jp",
        "weibo.com","weibo.cn",
    };

    private static DownloadType ClassifyUrl(string input)
    {
        var t = input.Trim();
        if (t.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return DownloadType.Torrent;
        if (t.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))  return DownloadType.Torrent;
        if (File.Exists(t) && t.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            return DownloadType.Torrent;

        if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            if (_ytDlpHosts.Contains(uri.Host)) return DownloadType.YtDlp;
            if (uri.AbsolutePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                return DownloadType.Torrent;
        }

        return DownloadType.Http;
    }

    private static string DetectPlatform(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return string.Empty;
        var host = uri.Host.TrimStart('m').TrimStart('.');

        return host switch
        {
            _ when host.Contains("youtube.com") || host == "youtu.be" => "youtube",
            _ when host.Contains("tiktok.com")                         => "tiktok",
            _ when host.Contains("bilibili.com")                       => "bilibili",
            _ when host.Contains("douyin.com")                         => "douyin",
            _ when host.Contains("vimeo.com")                          => "vimeo",
            _ when host.Contains("twitch.tv")                          => "twitch",
            _ when host.Contains("twitter.com") || host == "x.com"    => "twitter",
            _ when host.Contains("instagram.com")                      => "instagram",
            _ when host.Contains("facebook.com") || host == "fb.watch" => "facebook",
            _ => string.Empty,
        };
    }

    // ─────────────────────────────────────────────────────
    // Item factory
    // ─────────────────────────────────────────────────────

    private DownloadItemModel CreateItem(string url, DownloadType type, string platform,
        Guid? existingId = null, DateTimeOffset? existingCreatedAt = null)
    {
        DownloadItemModel? item = null;

        item = new DownloadItemModel(
            url:    url,
            type:   type,
            pauseCommand: new RelayCommand(() => PauseItem(item!)),
            resumeCommand: new RelayCommand(() => _ = RestartAfterPauseAsync(item!)),
            retryCommand: new RelayCommand(() =>
            {
                // Reset state first on the UI thread, then start the download.
                // Both actions are queued together so the download never starts
                // before the item is fully reset.
                RunOnUi(() =>
                {
                    item!.State            = DownloadState.Queued;
                    item!.Progress         = 0;
                    item!.SpeedBytesPerSec = 0;
                    item!.EtaSeconds       = null;
                    item!.ErrorMessage     = null;
                    item!.FilePath         = null;
                    item!.FilePaths        = new List<string>();

                    // Re-use existing selections; never re-show the format picker on retry.
                    if (item!.Type == DownloadType.YtDlp)
                    {
                        if (string.IsNullOrEmpty(item.SelectedVideoFormatId) && string.IsNullOrEmpty(item.SelectedAudioFormatId))
                            _ = FetchYtDlpMetadataAsync(item);
                        else _ = StartYtDlpDownloadAsync(item);
                    }
                    else
                        _ = RestartAfterPauseAsync(item!);
                });
            }),
            deleteCommand: new RelayCommand(() =>
            {
                if (DeleteConfirmRequested is null)
                {
                    CancelItem(item!);
                    return;
                }
                DeleteConfirmRequested.Invoke(item!, choice =>
                {
                    if (choice == DeleteChoice.Cancel) return;
                    if (choice == DeleteChoice.TaskAndFile)
                        ConfirmAndDeleteFile(item!);
                    else
                        CancelItem(item!);
                });
            }),
            openFolderCommand: new RelayCommand(() =>
            {
                try
                {
                    // Highlight the first known file in Explorer; fall back to directory
                    var firstFile = item!.FilePaths.FirstOrDefault(File.Exists)
                                 ?? (File.Exists(item!.FilePath) ? item!.FilePath : null);
                    if (firstFile is not null)
                        Process.Start("explorer.exe", $"/select,\"{firstFile}\"");
                    else
                    {
                        var dir = item!.ItemOutputDirectory ?? OutputDirectory;
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        Process.Start("explorer.exe", $"\"{dir}\"");
                    }
                }
                catch { /* explorer unavailable — ignore */ }
            }),
            playCommand: new RelayCommand(() =>
            {
                try
                {
                    var firstFile = item!.FilePaths.FirstOrDefault(File.Exists)
                                 ?? (File.Exists(item!.FilePath ?? "") ? item!.FilePath : null);
                    if (firstFile is not null)
                        Process.Start(new ProcessStartInfo(firstFile) { UseShellExecute = true });
                }
                catch { /* no associated app or file locked — ignore */ }
            }),
            startCommand: new RelayCommand(() => _ = RestartAfterPauseAsync(item!)),
            pickOutputDirCommand: new RelayCommand(() =>
            {
                PickItemOutputDirRequested?.Invoke(item!, dir =>
                {
                    if (dir is not null) RunOnUi(() => item!.ItemOutputDirectory = dir);
                });
            }))
        {
            SourcePlatform = platform,
            Id             = existingId         ?? Guid.NewGuid(),
            CreatedAt      = existingCreatedAt  ?? DateTimeOffset.Now,
        };

        return item;
    }

    private async Task RestartAfterPauseAsync(DownloadItemModel item)
    {
        if (item.Type == DownloadType.YtDlp)
        {
            // If we have a selected format, resume the yt-dlp download directly.
            // yt-dlp's --continue flag will resume from the .part file on disk.
            if (!string.IsNullOrEmpty(item.SelectedVideoFormatId) || !string.IsNullOrEmpty(item.SelectedAudioFormatId))
                await StartYtDlpDownloadAsync(item);
            else
                await FetchYtDlpMetadataAsync(item); // re-fetch for fresh format selection
        }
        else if (item.Type == DownloadType.Torrent)
        {
            // Rebuild selected-file indices from the saved TorrentFiles list.
            // aria2c's --continue=true will resume from whatever was already downloaded.
            var selectedIndices = item.TorrentFiles
                .Where(f => f.IsSelected)
                .Select(f => f.Index)
                .ToList();
            await StartTorrentDownloadAsync(item,
                new TorrentSelection(selectedIndices, item.ItemOutputDirectory ?? OutputDirectory));
        }
        else
        {
            await StartDirectDownloadAsync(item);
        }
    }

    // ─────────────────────────────────────────────────────
    // File deletion — two-stage, file-only (NEVER directory)
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Initiates safe file deletion:
    /// - Collects all actual files linked to this task (from FilePaths and/or FilePath).
    /// - If any files exist → raises FileDeleteConfirmRequested so the View can show a
    ///   second confirmation dialog listing every file path before deleting.
    /// - If no files exist (or path is a directory) → removes the task only.
    /// Never deletes directories or uses recursive deletion.
    /// </summary>
    private void ConfirmAndDeleteFile(DownloadItemModel item)
    {
        // Collect every real file path linked to this task.
        // Never include directory paths — they may contain unrelated user files.
        var filesToDelete = new List<string>();

        // Multi-file (separate video+audio)
        foreach (var p in item.FilePaths)
        {
            if (File.Exists(p))
                filesToDelete.Add(p);
        }

        // Single file (merged / torrent / HTTP) — only if FilePaths produced nothing
        if (filesToDelete.Count == 0 && File.Exists(item.FilePath))
            filesToDelete.Add(item.FilePath!);

        if (filesToDelete.Count == 0)
        {
            // No deletable files — just remove the task row
            CancelItem(item);
            return;
        }

        // Files exist: ask for second confirmation listing every path
        if (FileDeleteConfirmRequested is null)
        {
            // No UI wired — delete immediately (shouldn't happen in normal flow)
            foreach (var f in filesToDelete) SafeDeleteFile(f);
            CancelItem(item);
            return;
        }

        FileDeleteConfirmRequested.Invoke(filesToDelete.AsReadOnly(), confirmed =>
        {
            if (!confirmed) return;   // user cancelled second dialog
            foreach (var f in filesToDelete) SafeDeleteFile(f);
            CancelItem(item);
        });
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            // Never touch directories — only ever delete a single file.
        }
        catch { /* best-effort; non-fatal if file is locked */ }
    }

    // ─────────────────────────────────────────────────────
    // Magnet URI normalisation — Base32 BTIH → hex BTIH
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts Base32-encoded info-hashes in magnet URIs to hex so aria2c can parse them.
    /// A 32-char Base32 hash (alphabet A-Z 2-7) represents the same 160 bits as a 40-char
    /// lowercase hex hash.  Both forms are valid in the BitTorrent spec, but aria2c only
    /// accepts hex — so we normalise Base32 to hex on the way in.
    /// </summary>
    private static string NormalizeMagnetUri(string input)
    {
        if (!input.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            return input;

        // Locate xt=urn:btih:<hash> in the query string
        const string prefix = "urn:btih:";
        var xtIdx = input.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (xtIdx < 0) return input;

        var hashStart = xtIdx + prefix.Length;
        // Hash ends at next '&' or end-of-string
        var hashEnd = input.IndexOf('&', hashStart);
        var hash = hashEnd < 0
            ? input[hashStart..]
            : input[hashStart..hashEnd];

        // 32 chars → Base32 (5 bits/char → 160 bits); 40 chars → hex (already correct)
        if (hash.Length == 32)
        {
            var hexHash = Base32ToHex(hash);
            if (hexHash is not null)
                input = input[..hashStart] + hexHash + (hashEnd < 0 ? "" : input[hashEnd..]);
        }

        return input;
    }

    private static string? Base32ToHex(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var upper = base32.ToUpperInvariant();

        // 32 Base32 chars × 5 bits = 160 bits = 20 bytes
        var bytes = new byte[20];
        int bitBuf = 0, bitsLeft = 0, byteIdx = 0;

        foreach (var ch in upper)
        {
            var val = alphabet.IndexOf(ch);
            if (val < 0) return null; // invalid character

            bitBuf   = (bitBuf << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                bytes[byteIdx++] = (byte)(bitBuf >> bitsLeft);
                if (byteIdx >= 20) break;
            }
        }

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ─────────────────────────────────────────────────────
    // UI thread helper
    // ─────────────────────────────────────────────────────

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
            action();
        else
            _dispatcher.TryEnqueue(() => action());
    }

    /// <summary>
    /// Awaitable UI dispatch.  Unlike <see cref="RunOnUi"/> (fire-and-forget TryEnqueue),
    /// awaiting this Task guarantees the action has fully completed before the caller
    /// continues — safe to read item properties for persistence immediately after.
    /// </summary>
    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        })) tcs.SetException(new InvalidOperationException("The application window is closing."));
        return tcs.Task;
    }
}

// FilterTab is defined in Models/DownloadEnums.cs
