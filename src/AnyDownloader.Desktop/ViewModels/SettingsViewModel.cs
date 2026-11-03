namespace AnyDownloader.Desktop.ViewModels;

using AnyDownloader.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

/// <summary>
/// Settings page ViewModel
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDownloadQueueManager _queueManager;

    [ObservableProperty]
    private int _maxConcurrentDownloadsFree = 2;

    [ObservableProperty]
    private int _maxConcurrentDownloadsPremium = 5;

    [ObservableProperty]
    private int _maxConcurrentDownloadsVip = 10;

    [ObservableProperty]
    private int _globalMaxConcurrentDownloads = 20;

    [ObservableProperty]
    private long _rateLimitFree;

    [ObservableProperty]
    private long _rateLimitPremium;

    [ObservableProperty]
    private long _rateLimitVip;

    [ObservableProperty]
    private string _downloadPath = string.Empty;

    [ObservableProperty]
    private bool _enableQueueProcessing = true;

    [ObservableProperty]
    private bool _darkMode;

    [ObservableProperty]
    private string _selectedTheme = "Light";

    /// <summary>
    /// Gets available themes
    /// </summary>
    public string[] Themes { get; } = { "Light", "Dark", "System" };

    /// <summary>
    /// Initializes a new instance of the SettingsViewModel
    /// </summary>
    public SettingsViewModel(IDownloadQueueManager queueManager)
    {
        _queueManager = queueManager;
        LoadSettings();
    }

    /// <summary>
    /// Loads current settings
    /// </summary>
    private void LoadSettings()
    {
        // In a real app, load from configuration file
        DownloadPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "AnyDownloader");
    }

    /// <summary>
    /// Command to save settings
    /// </summary>
    [RelayCommand]
    private void SaveSettings()
    {
        var config = new QueueConfiguration
        {
            MaxConcurrentDownloadsFree = MaxConcurrentDownloadsFree,
            MaxConcurrentDownloadsPremium = MaxConcurrentDownloadsPremium,
            MaxConcurrentDownloadsVip = MaxConcurrentDownloadsVip,
            GlobalMaxConcurrentDownloads = GlobalMaxConcurrentDownloads,
            RateLimitFree = RateLimitFree,
            RateLimitPremium = RateLimitPremium,
            RateLimitVip = RateLimitVip,
            EnableQueueProcessing = EnableQueueProcessing
        };

        _queueManager.UpdateConfiguration(config);
        
        // In a real app, save to configuration file
        
        ClearError();
    }

    /// <summary>
    /// Command to reset settings to defaults
    /// </summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        MaxConcurrentDownloadsFree = 2;
        MaxConcurrentDownloadsPremium = 5;
        MaxConcurrentDownloadsVip = 10;
        GlobalMaxConcurrentDownloads = 20;
        RateLimitFree = 0;
        RateLimitPremium = 0;
        RateLimitVip = 0;
        EnableQueueProcessing = true;
    }

    /// <summary>
    /// Command to browse for download path
    /// </summary>
    private void BrowseDownloadPath()
    {
        // In a real app, this would open a folder browser dialog
        // For now, just use a default path
        DownloadPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }
}
