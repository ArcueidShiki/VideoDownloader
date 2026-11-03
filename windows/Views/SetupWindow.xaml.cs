using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using WinGraphics = global::Windows.Graphics;
using Microsoft.UI.Xaml.Media;
using AnyDownloader.Windows.Services;

namespace AnyDownloader.Windows.Views;

/// <summary>
/// First-launch window shown when required tools (yt-dlp, ffmpeg, aria2c) are missing.
/// Detects CPU architecture, downloads the correct binaries, then opens MainWindow.
/// </summary>
public sealed partial class SetupWindow : Window
{
    private CancellationTokenSource _cts = new();

    public SetupWindow()
    {
        InitializeComponent();
        Title = "AnyDownloader — First-Time Setup";
        AppWindow.Resize(new WinGraphics.SizeInt32(640, 500));
        _ = ApplyIconAsync();

        SystemInfoText.Text = $"Architecture: {ToolsInstaller.ArchLabel}  ·  {ToolsInstaller.OsDescription}";

        BuildToolRows();
        _ = RunInstallAsync();
    }

    private async Task ApplyIconAsync()
    {
        try
        {
            var iconPath = await AppIconService.GetOrCreateAsync();
            if (iconPath is not null)
                AppWindow.SetIcon(iconPath);
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────
    // Tool row view models
    // ─────────────────────────────────────────────────────

    private readonly ObservableCollection<ToolRowViewModel> _rows = new();

    private void BuildToolRows()
    {
        _rows.Clear();
        foreach (var info in ToolsInstaller.GetStatus())
            _rows.Add(new ToolRowViewModel(info.Name, info.IsInstalled));

        ToolList.ItemsSource = _rows;
    }

    private ToolRowViewModel? FindRow(string toolName)
    {
        foreach (var r in _rows)
            if (r.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)) return r;
        return null;
    }

    // ─────────────────────────────────────────────────────
    // Install flow
    // ─────────────────────────────────────────────────────

    private async Task RunInstallAsync()
    {
        ContinueButton.IsEnabled = false;
        RetryButton.Visibility   = Visibility.Collapsed;
        StatusText.Text          = "Preparing download…";

        try
        {
            var progress = new Progress<ToolInstallProgress>(OnProgress);
            await ToolsInstaller.InstallAllAsync(progress, _cts.Token);

            StatusText.Text           = "All tools installed successfully.";
            ContinueButton.IsEnabled  = true;
            SubtitleText.Text         = "Setup complete. Click Continue to start AnyDownloader.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text         = "Setup was cancelled.";
            RetryButton.Visibility  = Visibility.Visible;
        }
        catch (Exception ex)
        {
            StatusText.Text         = $"Error: {ex.Message}";
            RetryButton.Visibility  = Visibility.Visible;
            SubtitleText.Text       = "Something went wrong. Please retry or check your internet connection.";
        }
    }

    private void OnProgress(ToolInstallProgress p)
    {
        // Progress<T> may fire on a ThreadPool thread if the DispatcherQueue
        // SynchronizationContext wasn't active at construction time.
        // Always marshal explicitly so we never touch UI objects off-thread.
        DispatcherQueue.TryEnqueue(() =>
        {
            var row = FindRow(p.ToolName);
            if (row is null) return;

            row.ProgressPercent = (int)(p.Progress * 100);

            row.StatusText = p.State switch
            {
                ToolInstallState.AlreadyInstalled => "✓ Ready",
                ToolInstallState.ResolvingVersion => "Resolving…",
                ToolInstallState.Downloading      => $"{row.ProgressPercent}%",
                ToolInstallState.Extracting       => "Extracting…",
                ToolInstallState.Done             => "✓ Done",
                ToolInstallState.Failed           => "✗ Failed",
                _                                 => string.Empty,
            };

            row.StatusColor = p.State is ToolInstallState.Done or ToolInstallState.AlreadyInstalled
                ? new SolidColorBrush(Colors.LimeGreen)
                : p.State is ToolInstallState.Failed
                    ? new SolidColorBrush(Colors.OrangeRed)
                    : Application.Current.Resources["TextFillColorSecondaryBrush"] as SolidColorBrush;
        });
    }

    // ─────────────────────────────────────────────────────
    // Button handlers
    // ─────────────────────────────────────────────────────

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        // Tell App to open MainWindow and close this window
        if (Application.Current is App app)
            app.CompleteSetup();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        BuildToolRows();
        _ = RunInstallAsync();
    }
}

// ─────────────────────────────────────────────────────
// Row view model (simple INotifyPropertyChanged)
// ─────────────────────────────────────────────────────

internal sealed class ToolRowViewModel : INotifyPropertyChanged
{
    public string Name { get; }

    private int _progressPercent;
    public int ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    private string _statusText;
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private SolidColorBrush? _statusColor;
    public SolidColorBrush? StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public ToolRowViewModel(string name, bool alreadyInstalled)
    {
        Name           = name;
        _progressPercent = alreadyInstalled ? 100 : 0;
        _statusText      = alreadyInstalled ? "✓ Ready" : "Waiting…";
        _statusColor     = alreadyInstalled ? new SolidColorBrush(Colors.LimeGreen) : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
