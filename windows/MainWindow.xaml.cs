using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinGraphics = global::Windows.Graphics;
using WinSystem   = global::Windows.System;
using AnyDownloader.Windows.Models;
using AnyDownloader.Windows.Services;
using AnyDownloader.Windows.ViewModels;
using AnyDownloader.Windows.Views;

namespace AnyDownloader.Windows;

/// <summary>
/// Main application window.
/// Delegates all download logic to <see cref="DownloadViewModel"/>.
/// Handles dialog display for format picker and torrent file picker
/// (ContentDialog requires XamlRoot from the view, not the ViewModel).
/// </summary>
public sealed partial class MainWindow : Window
{
    public DownloadViewModel ViewModel { get; } = new DownloadViewModel();

    // Track which tab button is "selected" for visual styling
    private Button? _selectedTabButton;
    private Microsoft.UI.Xaml.Style? _defaultButtonStyle;

    public MainWindow()
    {
        InitializeComponent();
        Title = "AnyDownloader";

        // Set minimum window size
        AppWindow.Resize(new WinGraphics.SizeInt32(920, 620));

        // Apply icon (title bar + taskbar). Icon is generated on first run.
        _ = SetIconAsync();

        // Populate output dir label from settings
        OutputDirLabel.Text = ViewModel.OutputDirectory;

        // Wire up dialog events from ViewModel
        ViewModel.FormatPickerRequested      += OnFormatPickerRequested;
        ViewModel.TorrentPickerRequested     += OnTorrentPickerRequested;
        ViewModel.PickItemOutputDirRequested += OnPickItemOutputDirRequested;
        ViewModel.DeleteConfirmRequested     += OnDeleteConfirmRequested;
        ViewModel.DuplicateDownloadRequested += OnDuplicateDownloadRequested;
        ViewModel.FileDeleteConfirmRequested += OnFileDeleteConfirmRequested;

        // Capture the default button style, then highlight the initial tab
        _defaultButtonStyle = TabAll.Style;
        SelectTab(TabAll);
    }

    private async Task SetIconAsync()
    {
        try
        {
            // WinAppSDK 1.5 AppWindow.SetIcon() supports PNG directly — try it first.
            var pngPath = AppIconService.FindSourcePng();
            if (pngPath is not null)
            {
                try { AppWindow.SetIcon(pngPath); } catch { /* fall through to ICO path */ }
            }

            // Build/cache an ICO (PNG-in-ICO) for the Win32 taskbar HICON.
            var icoPath = await AppIconService.GetOrCreateAsync();
            if (icoPath is null) return;

            // If the PNG path above didn't work, also try ICO for AppWindow
            if (pngPath is null)
                try { AppWindow.SetIcon(icoPath); } catch { }

            // Set via Win32 SetClassLongPtr + WM_SETICON for reliable taskbar icon.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            AppIconService.SetHwndIcon(hwnd, icoPath);
        }
        catch { /* icon failure is non-fatal */ }
    }

    // ─────────────────────────────────────────────────────
    // URL input handlers
    // ─────────────────────────────────────────────────────

    private void AddButton_Click(object sender, RoutedEventArgs e) => SubmitUrl();

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == WinSystem.VirtualKey.Enter)
            SubmitUrl();
    }

    private void SubmitUrl()
    {
        var url = UrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(url)) return;

        ViewModel.AddDownload(url);
        UrlTextBox.Text = string.Empty;
    }

    // ─────────────────────────────────────────────────────
    // Browse .torrent file
    // ─────────────────────────────────────────────────────

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add(".torrent");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ViewModel.AddDownload(file.Path);
    }

    // ─────────────────────────────────────────────────────
    // Play / Pause All toggle
    // ─────────────────────────────────────────────────────

    private void PlayPauseAll_Click(object sender, RoutedEventArgs e)
    {
        bool anyActive = ViewModel.Downloads.Any(d => d.State == DownloadState.Active);
        if (anyActive)
        {
            ViewModel.PauseAllCommand.Execute(null);
            PlayPauseIcon.Glyph = "\uE768"; // play
        }
        else
        {
            ViewModel.StartAllCommand.Execute(null);
            PlayPauseIcon.Glyph = "\uE769"; // pause
        }
    }

    // ─────────────────────────────────────────────────────
    // Output directory picker (global)
    // ─────────────────────────────────────────────────────

    private async void OutputDirButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.OutputDirectory = folder.Path;
            DispatcherQueue.TryEnqueue(() => OutputDirLabel.Text = folder.Path);
        }
    }

    // ─────────────────────────────────────────────────────
    // Tab filter buttons
    // ─────────────────────────────────────────────────────

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;

        ViewModel.SelectedFilter = btn.Tag?.ToString() switch
        {
            "Active"   => FilterTab.Active,
            "Paused"   => FilterTab.Paused,
            "Done"     => FilterTab.Done,
            "Torrents" => FilterTab.Torrents,
            _          => FilterTab.All,
        };

        SelectTab(btn);
    }

    private void SelectTab(Button btn)
    {
        // Always restore the previous button — setting Style=null is valid and
        // resets to the implicit default style from the resource dictionary.
        // The old check `_defaultButtonStyle is not null` was wrong: since the
        // tab buttons have no explicit Style in XAML, _defaultButtonStyle is null,
        // so the condition was always false and the accent was never removed.
        if (_selectedTabButton is not null)
            _selectedTabButton.Style = _defaultButtonStyle; // null → implicit style

        _selectedTabButton = btn;
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accentStyle)
            && accentStyle is Microsoft.UI.Xaml.Style style)
            btn.Style = style;
    }

    // ─────────────────────────────────────────────────────
    // Dialog handlers (called by ViewModel events on UI thread)
    // ─────────────────────────────────────────────────────

    // All dialog handlers are invoked via ViewModel events, which are always raised
    // through RunOnUi (DispatcherQueue). No await before ContentDialog construction —
    // this guarantees we stay on the UI (COM STA) thread. SemaphoreSlim.WaitAsync()
    // was removed because its continuations run on ThreadPool and break WinUI3 dialogs.

    private async void OnFormatPickerRequested(DownloadItemModel item)
    {
        var dialog = new FormatPickerDialog(item, ViewModel.OutputDirectory)
        {
            XamlRoot = Content.XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 680.0;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
            ViewModel.ConfirmFormat(item.Id, dialog.GetSelection());
        else
            ViewModel.CancelFormatPicker(item.Id);
    }

    private async void OnTorrentPickerRequested(DownloadItemModel item)
    {
        var dialog = new TorrentFilePickerDialog(item.TorrentFiles, ViewModel.OutputDirectory)
        {
            XamlRoot = Content.XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
            ViewModel.ConfirmTorrentFiles(item.Id, dialog.GetSelection());
        else
            ViewModel.CancelTorrentPicker(item.Id);
    }

    private async void OnDuplicateDownloadRequested(DownloadItemModel existing, Action<DuplicateChoice> callback)
    {
        var dialog = new ContentDialog
        {
            Title               = "Already in queue",
            Content             = $"\"{existing.DisplayTitle}\" is already in the download queue.\nWhat would you like to do?",
            PrimaryButtonText   = "Download again",
            SecondaryButtonText = "Keep both",
            CloseButtonText     = "Cancel",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        callback(result switch
        {
            ContentDialogResult.Primary   => DuplicateChoice.DownloadAgain,
            ContentDialogResult.Secondary => DuplicateChoice.KeepBoth,
            _                             => DuplicateChoice.Cancel,
        });
    }

    private async void OnDeleteConfirmRequested(DownloadItemModel item, Action<DeleteChoice> callback)
    {
        var dialog = new ContentDialog
        {
            Title               = "Remove download",
            Content             = $"Remove \"{item.DisplayTitle}\" from the queue?",
            PrimaryButtonText   = "Task only",
            SecondaryButtonText = "Task + file",
            CloseButtonText     = "Cancel",
            DefaultButton       = ContentDialogButton.Primary,
            XamlRoot            = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        callback(result switch
        {
            ContentDialogResult.Primary   => DeleteChoice.TaskOnly,
            ContentDialogResult.Secondary => DeleteChoice.TaskAndFile,
            _                             => DeleteChoice.Cancel,
        });
    }

    /// <summary>
    /// Second-stage delete confirmation: lists every file that will be permanently
    /// deleted.  Callback receives true = confirmed, false = cancelled.
    /// </summary>
    private async void OnFileDeleteConfirmRequested(IReadOnlyList<string> filePaths, Action<bool> callback)
    {
        var fileWord    = filePaths.Count == 1 ? "file" : $"{filePaths.Count} files";
        var pathDisplay = string.Join("\n", filePaths);

        var dialog = new ContentDialog
        {
            Title             = "Delete file — are you sure?",
            Content           = $"Permanently delete {fileWord}?\n\n{pathDisplay}\n\nThis cannot be undone.",
            PrimaryButtonText = filePaths.Count == 1 ? "Delete file" : "Delete files",
            CloseButtonText   = "Cancel",
            DefaultButton     = ContentDialogButton.Close,   // safe default
            XamlRoot          = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        callback(result == ContentDialogResult.Primary);
    }

    private async void OnPickItemOutputDirRequested(DownloadItemModel item, Action<string?> callback)
    {
        var picker = new FolderPicker();
        var hwnd   = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        callback(folder?.Path);
    }
}
