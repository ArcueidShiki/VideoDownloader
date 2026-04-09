using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using AnyDownloader.Windows.Models;
using AnyDownloader.Windows; // for App.MainWindow

namespace AnyDownloader.Windows.Views;

/// <summary>
/// Torrent file selection dialog.
/// Mirrors macOS TorrentFilePickerSheet.
/// </summary>
public sealed partial class TorrentFilePickerDialog : ContentDialog
{
    private readonly List<TorrentFileEntry> _entries;
    private string _outputDirectory;

    public TorrentFilePickerDialog(IReadOnlyList<TorrentFileEntry> entries, string defaultOutputDirectory)
    {
        _entries         = new List<TorrentFileEntry>(entries);
        _outputDirectory = defaultOutputDirectory;
        InitializeComponent();
        Loaded += (_, _) => Populate();
    }

    private void Populate()
    {
        FileList.ItemsSource = _entries;
        OutputDirText.Text   = _outputDirectory;
        UpdateTotalSize();
    }

    // ─────────────────────────────────────────────────────
    // Select / deselect all
    // ─────────────────────────────────────────────────────

    private void SelectAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        foreach (var entry in _entries) entry.IsSelected = true;
        RefreshList();
    }

    private void DeselectAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        foreach (var entry in _entries) entry.IsSelected = false;
        RefreshList();
    }

    private void RefreshList()
    {
        // Force ListView to re-render checkboxes after bulk change
        FileList.ItemsSource = null;
        FileList.ItemsSource = _entries;
        UpdateTotalSize();
    }

    private void UpdateTotalSize()
    {
        var selected   = _entries.Where(e => e.IsSelected).ToArray();
        var totalBytes = selected.Sum(e => e.FilesizeBytes);

        TotalSizeText.Text = selected.Length == _entries.Count
            ? $"All {_entries.Count} files · {FormatSize(totalBytes)}"
            : $"{selected.Length} of {_entries.Count} files · {FormatSize(totalBytes)}";
    }

    // ─────────────────────────────────────────────────────
    // Output folder picker
    // ─────────────────────────────────────────────────────

    private async void ChangeOutputDir_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(
            ((App)Microsoft.UI.Xaml.Application.Current).MainWindow!);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            _outputDirectory = folder.Path;
            DispatcherQueue.TryEnqueue(() => OutputDirText.Text = folder.Path);
        }
    }

    // ─────────────────────────────────────────────────────
    // Result accessor
    // ─────────────────────────────────────────────────────

    public TorrentSelection GetSelection()
    {
        var selectedIndices = _entries
            .Where(e => e.IsSelected)
            .Select(e => e.Index)
            .ToList();

        // If all selected, pass empty list (aria2c downloads all by default)
        if (selectedIndices.Count == _entries.Count)
            selectedIndices = new List<int>();

        return new TorrentSelection(selectedIndices, _outputDirectory);
    }

    private static string FormatSize(long b) =>
        b >= 1_073_741_824 ? $"{b / 1_073_741_824.0:F2} GB" :
        b >= 1_048_576     ? $"{b / 1_048_576.0:F1} MB" :
        b >= 1_024         ? $"{b / 1_024.0:F0} KB" :
                             $"{b} B";
}
