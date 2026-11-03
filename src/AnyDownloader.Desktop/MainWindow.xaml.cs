using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AnyDownloader.Core;
using AnyDownloader.Core.ResourceLocator;
using AnyDownloader.Core.Downloader;
using AnyDownloader.Engine;
using Microsoft.Extensions.Logging;

namespace AnyDownloader.Desktop;

public partial class MainWindow : Window
{
    private readonly IDownloadQueueManager _queueManager;
    private readonly UrlValidator _validator;
    private readonly Dictionary<string, DownloadCard> _activeDownloads = new();

    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize NATIVE download services
        var nativeService = new NativeDownloadService();
        var torrentService = new TorrentDownloadService(null);
        
        _queueManager = new DownloadQueueManager(
            new IDownloadService[] { nativeService, torrentService },
            null
        );
        
        _validator = new UrlValidator();
        
        // Start queue processing
        _ = _queueManager.StartProcessingAsync();
        
        // Show welcome message
        ShowMessage("Ready! HTTP + Torrent download engine active. Add a URL or magnet link.", MessageType.Info);
    }

    private void AddDownloadCard(DownloadQueueItem item)
    {
        if (NoDownloadsText != null)
            NoDownloadsText.Visibility = Visibility.Collapsed;

        var card = new DownloadCard(item, _queueManager);
        _activeDownloads[item.Id] = card;
        ActiveDownloadsPanel.Children.Add(card);
        
        // Update button state
        card.CancelClicked += async (id) =>
        {
            await _queueManager.CancelDownloadAsync(id);
            ActiveDownloadsPanel.Children.Remove(_activeDownloads[id]);
            _activeDownloads.Remove(id);

            if (_activeDownloads.Count == 0 && NoDownloadsText != null)
                NoDownloadsText.Visibility = Visibility.Visible;
        };
        
        // Handle delete
        card.DeleteClicked += (id) =>
        {
            ActiveDownloadsPanel.Children.Remove(_activeDownloads[id]);
            _activeDownloads.Remove(id);

            if (_activeDownloads.Count == 0 && NoDownloadsText != null)
                NoDownloadsText.Visibility = Visibility.Visible;
        };
    }

    private void ShowMessage(string message, MessageType type)
    {
        MessageText.Text = message;
        MessageBar.Background = type switch
        {
            MessageType.Success => new SolidColorBrush(Color.FromRgb(16, 124, 16)),
            MessageType.Error => new SolidColorBrush(Color.FromRgb(209, 52, 56)),
            MessageType.Warning => new SolidColorBrush(Color.FromRgb(255, 140, 0)),
            _ => new SolidColorBrush(Color.FromRgb(0, 120, 212))
        };
        MessageBar.Visibility = Visibility.Visible;
        
        // Auto-hide after 5 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            Dispatcher.Invoke(() => MessageBar.Visibility = Visibility.Collapsed);
        });
    }

    /// <summary>
    /// Called from App.xaml.cs when a URL arrives via protocol or named pipe.
    /// Brings the window to the foreground and pre-fills the URL input.
    /// </summary>
    public void AddDownloadFromUrl(string url)
    {
        Dispatcher.Invoke(() =>
        {
            // Restore and focus the window
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;

            UrlTextBox.Text = url;
        });
    }

    private async void AddDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var url = UrlTextBox.Text.Trim();
        
        if (string.IsNullOrEmpty(url))
        {
            ShowMessage("Please enter a URL or magnet link", MessageType.Warning);
            return;
        }

        // Validate URL or magnet link
        bool isValidUrl = _validator.IsValid(url) || MagnetLinkParser.IsValidMagnetLink(url);
        if (!isValidUrl)
        {
            ShowMessage($"Invalid URL or magnet link: {url}", MessageType.Error);
            return;
        }

        // Show confirmation dialog
        var dialog = new DownloadConfirmDialog();
        dialog.Owner = this;
        dialog.SetDownloadInfo(url);
        
        if (dialog.ShowDialog() != true)
        {
            // User cancelled
            return;
        }

        try
        {
            // Get selected options
            var quality = GetSelectedQuality();
            var format = GetSelectedFormat();
            var subtitles = SubtitlesCheckBox.IsChecked == true;

            var item = new DownloadQueueItem
            {
                Url = url,
                Options = new DownloadOptions
                {
                    Quality = quality,
                    Format = format,
                    DownloadSubtitles = subtitles,
                    OutputPath = dialog.DownloadLocation
                },
                UserId = "desktop-user"
            };

            // Add to queue manager for REAL download
            await _queueManager.EnqueueAsync(item);
            
            // Add to UI
            AddDownloadCard(item);
            
            // Clear input
            UrlTextBox.Text = string.Empty;
            
            ShowMessage($"Download started: {dialog.FileName}", MessageType.Success);
        }
        catch (Exception ex)
        {
            ShowMessage($"Error: {ex.Message}", MessageType.Error);
        }
    }

    private QualityLevel GetSelectedQuality()
    {
        if (QualityComboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content.ToString() switch
            {
                "1080p" => QualityLevel.Q1080p,
                "720p" => QualityLevel.Q720p,
                "480p" => QualityLevel.Q480p,
                _ => QualityLevel.Auto
            };
        }
        return QualityLevel.Auto;
    }

    private VideoFormat GetSelectedFormat()
    {
        if (FormatComboBox.SelectedItem is ComboBoxItem item)
        {
            return item.Content.ToString() switch
            {
                "MKV" => VideoFormat.Mkv,
                "WebM" => VideoFormat.WebM,
                "MP3" => VideoFormat.Mp3,
                _ => VideoFormat.Mp4
            };
        }
        return VideoFormat.Mp4;
    }
}

public enum MessageType
{
    Success,
    Error,
    Warning,
    Info
}

public class DownloadCard : Border
{
    private readonly TextBlock _progressText;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private readonly Button _openFolderButton;
    private readonly Button _deleteButton;
    private readonly StackPanel _buttonPanel;
    private readonly IDownloadQueueManager? _queueManager;

    public event Action<string>? CancelClicked;
    public event Action<string>? DeleteClicked;
    public string Id { get; }
    public string? FilePath { get; private set; }

    public DownloadCard(DownloadQueueItem item, IDownloadQueueManager? queueManager = null)
    {
        Id = item.Id;
        _queueManager = queueManager;
        
        Background = new SolidColorBrush(Color.FromRgb(249, 249, 249));
        BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(15);
        Margin = new Thickness(0, 0, 0, 10);

        var stackPanel = new StackPanel();
        
        // URL
        var urlText = new TextBlock
        {
            Text = item.Url,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        stackPanel.Children.Add(urlText);
        
        // Progress Bar
        _progressBar = new ProgressBar
        {
            Height = 6,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 0, 0, 10)
        };
        stackPanel.Children.Add(_progressBar);
        
        // Progress Text and Buttons
        _buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
        
        _progressText = new TextBlock
        {
            Text = "Starting...",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0),
            MinWidth = 100
        };
        _buttonPanel.Children.Add(_progressText);
        
        _cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromRgb(209, 52, 56)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = item.Id,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _cancelButton.Click += (s, e) => CancelClicked?.Invoke(Id);
        _buttonPanel.Children.Add(_cancelButton);
        
        // Open Folder Button (hidden initially)
        _openFolderButton = new Button
        {
            Content = "📁 Open Folder",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = item.Id,
            Margin = new Thickness(0, 0, 10, 0),
            Visibility = Visibility.Collapsed
        };
        _openFolderButton.Click += OpenFolderButton_Click;
        _buttonPanel.Children.Add(_openFolderButton);
        
        // Delete Button (hidden initially)
        _deleteButton = new Button
        {
            Content = "🗑 Delete",
            Padding = new Thickness(15, 8, 15, 8),
            Background = new SolidColorBrush(Color.FromRgb(107, 105, 106)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = item.Id,
            Visibility = Visibility.Collapsed
        };
        _deleteButton.Click += DeleteButton_Click;
        _buttonPanel.Children.Add(_deleteButton);
        
        stackPanel.Children.Add(_buttonPanel);
        Child = stackPanel;
        
        // Start polling for real progress
        _ = PollProgress();
    }

    private async Task PollProgress()
    {
        while (true)
        {
            await Task.Delay(1000);
            
            if (_queueManager != null)
            {
                var item = _queueManager.GetItem(Id);
                if (item != null)
                {
                    _progressBar.Value = item.Status.Progress;
                    _progressText.Text = $"{item.Status.Progress:F1}% - {item.Status.GetFormattedSpeed()}";
                    
                    if (item.Status.State == DownloadState.Completed)
                    {
                        _progressText.Text = "✅ Completed";
                        _progressText.Foreground = new SolidColorBrush(Color.FromRgb(16, 124, 16));
                        
                        // Set file path
                        FilePath = item.Result?.FilePath;
                        
                        // Show Open Folder and Delete buttons
                        _cancelButton.Visibility = Visibility.Collapsed;
                        _openFolderButton.Visibility = Visibility.Visible;
                        _deleteButton.Visibility = Visibility.Visible;
                        break;
                    }
                    else if (item.Status.State == DownloadState.Failed)
                    {
                        _progressText.Text = $"❌ Failed: {item.Status.ErrorMessage}";
                        _progressText.Foreground = new SolidColorBrush(Color.FromRgb(209, 52, 56));
                        _cancelButton.Visibility = Visibility.Collapsed;
                        break;
                    }
                }
            }
        }
    }

    private void OpenFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Always open Downloads folder for demo
            var downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloadDir = System.IO.Path.Combine(downloadsPath, "Downloads");
            
            if (System.IO.Directory.Exists(downloadDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{downloadDir}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                // If Downloads folder doesn't exist, open user profile
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{downloadsPath}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            if (_progressText != null)
            {
                _progressText.Text = $"Error: {ex.Message}";
                _progressText.Foreground = new SolidColorBrush(Color.FromRgb(209, 52, 56));
            }
        }
    }

    private void DeleteButton_Click(object? sender, RoutedEventArgs e)
    {
        DeleteClicked?.Invoke(Id);
    }

    private async Task SimulateProgress()
    {
        var random = new Random();
        var progress = 0.0;
        
        while (progress < 100)
        {
            await Task.Delay(500);
            progress += random.Next(5, 15);
            progress = Math.Min(progress, 100);
            
            _progressBar.Value = progress;
            _progressText.Text = $"{progress:F1}%";
            
            if (progress >= 100)
            {
                _progressText.Text = "✅ Completed";
                _progressText.Foreground = new SolidColorBrush(Color.FromRgb(16, 124, 16));
                
                // Show Open Folder and Delete buttons, hide Cancel
                _cancelButton.Visibility = Visibility.Collapsed;
                _openFolderButton.Visibility = Visibility.Visible;
                _deleteButton.Visibility = Visibility.Visible;
                
                // Set simulated file path
                FilePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    $"download_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
                );
            }
        }
    }
}
