namespace AnyDownloader.Core;

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Options for configuring a download
/// </summary>
public class DownloadOptions : ICloneable
{
    private QualityLevel _quality = QualityLevel.Auto;
    private VideoFormat _format = VideoFormat.Mp4;
    private bool _downloadSubtitles;
    private bool _encryptDownload;
    private string? _subtitleLanguage;
    private int _maxConcurrentDownloads = 3;
    private long? _speedLimitBytesPerSecond;
    private string? _outputPath;
    private string? _cookieFile;
    private bool _noWatermark;

    /// <summary>
    /// Video quality to download
    /// </summary>
    public QualityLevel Quality
    {
        get => _quality;
        set => SetProperty(ref _quality, value);
    }

    /// <summary>
    /// Output video format
    /// </summary>
    public VideoFormat Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    /// <summary>
    /// Whether to download subtitles
    /// </summary>
    public bool DownloadSubtitles
    {
        get => _downloadSubtitles;
        set => SetProperty(ref _downloadSubtitles, value);
    }

    /// <summary>
    /// Whether to encrypt the downloaded file
    /// </summary>
    public bool EncryptDownload
    {
        get => _encryptDownload;
        set => SetProperty(ref _encryptDownload, value);
    }

    /// <summary>
    /// Subtitle language code (e.g., "en", "es", "zh-Hans")
    /// </summary>
    public string? SubtitleLanguage
    {
        get => _subtitleLanguage;
        set => SetProperty(ref _subtitleLanguage, value);
    }

    /// <summary>
    /// Maximum number of concurrent downloads
    /// </summary>
    public int MaxConcurrentDownloads
    {
        get => _maxConcurrentDownloads;
        set => SetProperty(ref _maxConcurrentDownloads, value);
    }

    /// <summary>
    /// Speed limit in bytes per second (null = no limit)
    /// </summary>
    public long? SpeedLimitBytesPerSecond
    {
        get => _speedLimitBytesPerSecond;
        set => SetProperty(ref _speedLimitBytesPerSecond, value);
    }

    /// <summary>
    /// Output path for the downloaded file (null = default location)
    /// </summary>
    public string? OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    /// <summary>
    /// Path to cookie file for authenticated downloads
    /// </summary>
    public string? CookieFile
    {
        get => _cookieFile;
        set => SetProperty(ref _cookieFile, value);
    }

    /// <summary>
    /// Whether to download without watermark (for platforms like TikTok)
    /// </summary>
    public bool NoWatermark
    {
        get => _noWatermark;
        set => SetProperty(ref _noWatermark, value);
    }

    /// <summary>
    /// Creates a copy of this DownloadOptions instance
    /// </summary>
    public DownloadOptions Clone()
    {
        return new DownloadOptions
        {
            Quality = _quality,
            Format = _format,
            DownloadSubtitles = _downloadSubtitles,
            EncryptDownload = _encryptDownload,
            SubtitleLanguage = _subtitleLanguage,
            MaxConcurrentDownloads = _maxConcurrentDownloads,
            SpeedLimitBytesPerSecond = _speedLimitBytesPerSecond,
            OutputPath = _outputPath,
            CookieFile = _cookieFile,
            NoWatermark = _noWatermark
        };
    }

    object ICloneable.Clone()
    {
        return Clone();
    }

    /// <summary>
    /// Property changed event for MVVM pattern
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sets a property and raises PropertyChanged event
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Raises the PropertyChanged event
    /// </summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
