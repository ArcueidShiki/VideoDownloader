namespace AnyDownloader.Desktop.ViewModels;

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AnyDownloader.Core;
using AnyDownloader.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

/// <summary>
/// Main window ViewModel
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IDownloadQueueManager _queueManager;
    private readonly UrlValidator _urlValidator;

    [ObservableProperty]
    private string _newDownloadUrl = string.Empty;

    [ObservableProperty]
    private QualityLevel _selectedQuality = QualityLevel.Auto;

    [ObservableProperty]
    private VideoFormat _selectedFormat = VideoFormat.Mp4;

    [ObservableProperty]
    private bool _downloadSubtitles;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private QueueStatistics _statistics = new();

    /// <summary>
    /// Gets the collection of queued downloads
    /// </summary>
    public ObservableCollection<DownloadQueueItem> QueuedDownloads { get; } = new();

    /// <summary>
    /// Gets the collection of active downloads
    /// </summary>
    public ObservableCollection<DownloadQueueItem> ActiveDownloads { get; } = new();

    /// <summary>
    /// Gets the collection of completed downloads
    /// </summary>
    public ObservableCollection<DownloadQueueItem> CompletedDownloads { get; } = new();

    /// <summary>
    /// Gets available quality options
    /// </summary>
    public QualityLevel[] QualityOptions { get; } = Enum.GetValues<QualityLevel>();

    /// <summary>
    /// Gets available format options
    /// </summary>
    public VideoFormat[] FormatOptions { get; } = Enum.GetValues<VideoFormat>();

    /// <summary>
    /// Initializes a new instance of the MainViewModel
    /// </summary>
    public MainViewModel(IDownloadQueueManager queueManager)
    {
        _queueManager = queueManager;
        _urlValidator = new UrlValidator();
        
        // Start queue processing
        _ = StartQueueProcessingAsync();
        
        // Start statistics refresh
        RefreshStatistics();
    }

    /// <summary>
    /// Command to add a new download
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddDownload))]
    private async Task AddDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDownloadUrl))
            return;

        var urls = NewDownloadUrl.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var url in urls)
        {
            var trimmedUrl = url.Trim();
            if (!_urlValidator.IsValid(trimmedUrl))
            {
                SetError($"Invalid URL: {trimmedUrl}");
                continue;
            }

            var item = new DownloadQueueItem
            {
                Url = trimmedUrl,
                Options = new DownloadOptions
                {
                    Quality = SelectedQuality,
                    Format = SelectedFormat,
                    DownloadSubtitles = DownloadSubtitles
                },
                UserId = "local-user" // Local desktop user
            };

            await _queueManager.EnqueueAsync(item);
        }

        NewDownloadUrl = string.Empty;
        ClearError();
        RefreshDownloads();
    }

    private bool CanAddDownload()
    {
        return !string.IsNullOrWhiteSpace(NewDownloadUrl);
    }

    /// <summary>
    /// Command to pause a download
    /// </summary>
    [RelayCommand]
    private async Task PauseDownloadAsync(DownloadQueueItem item)
    {
        if (item == null) return;
        
        await _queueManager.PauseDownloadAsync(item.Id);
        RefreshDownloads();
    }

    /// <summary>
    /// Command to resume a download
    /// </summary>
    [RelayCommand]
    private async Task ResumeDownloadAsync(DownloadQueueItem item)
    {
        if (item == null) return;
        
        await _queueManager.ResumeDownloadAsync(item.Id);
        RefreshDownloads();
    }

    /// <summary>
    /// Command to cancel a download
    /// </summary>
    [RelayCommand]
    private async Task CancelDownloadAsync(DownloadQueueItem item)
    {
        if (item == null) return;
        
        await _queueManager.CancelDownloadAsync(item.Id);
        RefreshDownloads();
    }

    /// <summary>
    /// Command to open download location
    /// </summary>
    [RelayCommand]
    private void OpenDownloadLocation(DownloadQueueItem item)
    {
        if (item?.Result?.FilePath == null) return;
        
        try
        {
            var filePath = item.Result.FilePath;
            var directory = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory))
            {
                System.Diagnostics.Process.Start("explorer.exe", directory);
            }
        }
        catch
        {
            SetError("Failed to open download location");
        }
    }

    /// <summary>
    /// Command to clear completed downloads
    /// </summary>
    [RelayCommand]
    private void ClearCompleted()
    {
        CompletedDownloads.Clear();
    }

    /// <summary>
    /// Starts the queue processing
    /// </summary>
    private async Task StartQueueProcessingAsync()
    {
        await _queueManager.StartProcessingAsync();
        
        // Start refresh loop
        Device.StartTimer(TimeSpan.FromMilliseconds(500), OnRefreshTimerTick);
    }

    /// <summary>
    /// Timer tick handler for refreshing downloads
    /// </summary>
    private bool OnRefreshTimerTick()
    {
        RefreshDownloads();
        RefreshStatistics();
        return true; // Continue timer
    }

    /// <summary>
    /// Refreshes the download lists
    /// </summary>
    private void RefreshDownloads()
    {
        // Update queued downloads
        QueuedDownloads.Clear();
        foreach (var item in _queueManager.GetQueuedItems())
        {
            QueuedDownloads.Add(item);
        }

        // Update active downloads
        ActiveDownloads.Clear();
        foreach (var item in _queueManager.GetActiveDownloads())
        {
            ActiveDownloads.Add(item);
        }

        // Update completed downloads (from statistics or history)
        // In a real app, this would come from persistent storage
    }

    /// <summary>
    /// Refreshes statistics
    /// </summary>
    private void RefreshStatistics()
    {
        Statistics = _queueManager.GetStatistics();
    }
}

/// <summary>
/// Helper for UI thread dispatching
/// </summary>
internal static class Device
{
    public static bool StartTimer(TimeSpan interval, Func<bool> callback)
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = interval
        };
        
        timer.Tick += (s, e) =>
        {
            if (!callback())
            {
                timer.Stop();
            }
        };
        
        timer.Start();
        return true;
    }
}
