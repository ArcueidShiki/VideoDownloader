using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using AnyDownloader.Core;

namespace AnyDownloader.Desktop;

public partial class DownloadConfirmDialog : Window
{
    public string DownloadUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string DownloadLocation { get; set; } = string.Empty;
    public bool Confirmed { get; set; } = false;

    public DownloadConfirmDialog()
    {
        InitializeComponent();
    }

    public void SetDownloadInfo(string url, string? fileName = null, string? fileType = null)
    {
        DownloadUrl = url;
        UrlText.Text = url.Length > 60 ? url.Substring(0, 60) + "..." : url;
        
        // Parse magnet link or use provided info
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            var magnetInfo = MagnetLinkParser.Parse(url);
            FileName = !string.IsNullOrEmpty(magnetInfo?.DisplayName) 
                ? magnetInfo.DisplayName 
                : "Torrent Download";
            FileType = "BitTorrent";
        }
        else
        {
            FileName = fileName ?? Path.GetFileName(url) ?? "download";
            FileType = fileType ?? "HTTP/HTTPS";
        }
        
        FileNameText.Text = FileName;
        FileTypeText.Text = FileType;
        
        // Default download location
        DownloadLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "AnyDownloader");
        LocationTextBox.Text = DownloadLocation;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select download location",
            SelectedPath = DownloadLocation,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            DownloadLocation = dialog.SelectedPath;
            LocationTextBox.Text = DownloadLocation;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DownloadLocation = LocationTextBox.Text;
        DialogResult = true;
        Close();
    }
}
