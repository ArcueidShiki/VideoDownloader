using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using AnyDownloader.Windows.Models;
using AnyDownloader.Windows.Services;

namespace AnyDownloader.Windows.Views;

/// <summary>
/// Format selection dialog shown when a yt-dlp URL is added.
///
/// Layout: Download Options (top) → Video quality → Audio quality → Save settings.
/// Sections show/hide based on the Video/Audio/Merge checkboxes so the user
/// never sees irrelevant controls.
/// </summary>
public sealed partial class FormatPickerDialog : ContentDialog
{
    private readonly DownloadItemModel _item;
    private string _outputDirectory;

    private VideoFormat? _selectedVideoFormat;
    private AudioFormat? _selectedAudioFormat;

    public FormatPickerDialog(DownloadItemModel item, string defaultOutputDirectory)
    {
        _item            = item;
        _outputDirectory = defaultOutputDirectory;
        InitializeComponent();
        Loaded += (_, _) => Populate();
    }

    // ─────────────────────────────────────────────────────
    // Populate with video info
    // ─────────────────────────────────────────────────────

    private void Populate()
    {
        var info = _item.VideoInfo;
        if (info is null) return;

        TitleText.Text    = info.Title;
        UploaderText.Text = info.Uploader ?? string.Empty;
        DurationText.Text = info.DurationFormatted ?? string.Empty;

        if (_item.Thumbnail is not null)
            ThumbnailImage.Source = _item.Thumbnail;

        VideoFormatList.ItemsSource = info.VideoFormats;
        AudioFormatList.ItemsSource = info.AudioFormats;

        // Pre-select best options
        if (info.VideoFormats.Length > 0)
        {
            VideoFormatList.SelectedIndex = 0;
            _selectedVideoFormat = info.VideoFormats[0];
        }
        if (info.AudioFormats.Length > 0)
        {
            AudioFormatList.SelectedIndex = 0;
            _selectedAudioFormat = info.AudioFormats[0];
        }

        OutputDirText.Text = _outputDirectory;

        // Default state: video + audio + merge. Hide audio section when no separate streams.
        IncludeAudioCheck.IsChecked = info.HasSeparateStreams && info.AudioFormats.Length > 0;
        RefreshSectionVisibility();
    }

    // ─────────────────────────────────────────────────────
    // Checkbox change handlers
    // ─────────────────────────────────────────────────────

    private void Options_Changed(object sender, RoutedEventArgs e) => RefreshSectionVisibility();

    private void RefreshSectionVisibility()
    {
        // Guard: Checked/Unchecked can fire during InitializeComponent before
        // the named controls below this element in the XAML tree are created.
        if (VideoSection is null) return;

        bool video = IncludeVideoCheck.IsChecked == true;
        bool audio = IncludeAudioCheck.IsChecked == true;

        VideoSection.Visibility     = video          ? Visibility.Visible : Visibility.Collapsed;
        AudioSection.Visibility     = audio          ? Visibility.Visible : Visibility.Collapsed;
        MergeOptionPanel.Visibility = video && audio ? Visibility.Visible : Visibility.Collapsed;

        if (!(video && audio))
            MergeToMp4Check.IsChecked = false;
        // When both are selected, leave the checkbox in whatever state the user set it —
        // only auto-uncheck when the constraint (both must be selected) is broken.
    }

    // ─────────────────────────────────────────────────────
    // Format list click handlers
    // ─────────────────────────────────────────────────────

    private void VideoFormatList_ItemClick(object sender, ItemClickEventArgs e)
        => _selectedVideoFormat = e.ClickedItem as VideoFormat;

    private void AudioFormatList_ItemClick(object sender, ItemClickEventArgs e)
        => _selectedAudioFormat = e.ClickedItem as AudioFormat;

    // ─────────────────────────────────────────────────────
    // Output folder picker
    // ─────────────────────────────────────────────────────

    private async void ChangeOutputDir_Click(object sender, RoutedEventArgs e)
    {
        try
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
        catch { /* picker dismissed or access denied */ }
    }

    // ─────────────────────────────────────────────────────
    // Result accessor
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Call after ShowAsync() returns ContentDialogResult.Primary to get the user's choices.
    /// </summary>
    public FormatSelection GetSelection()
    {
        bool includeVideo = IncludeVideoCheck.IsChecked == true;
        bool includeAudio = IncludeAudioCheck.IsChecked == true;
        bool merge        = includeVideo && includeAudio && MergeToMp4Check.IsChecked == true;

        var filenameOverride = FilenameBox.Text.Trim();
        if (string.IsNullOrEmpty(filenameOverride)) filenameOverride = null;

        return new FormatSelection(
            VideoFormatId:    includeVideo ? (_selectedVideoFormat?.FormatId ?? "bestvideo") : null,
            AudioFormatId:    includeAudio ? (_selectedAudioFormat?.FormatId ?? "bestaudio") : null,
            IncludeVideo:     includeVideo,
            IncludeAudio:     includeAudio,
            MergeToMp4:       merge,
            OutputDirectory:  _outputDirectory,
            FilenameOverride: filenameOverride);
    }
}
