// Headless adapters for the real ViewModel/model sources. These tests exercise
// command/state transitions without WinUI activation or the user's history DB.
using AnyDownloader.Windows.Models;

namespace Microsoft.UI.Xaml { public enum Visibility { Visible, Collapsed } }
namespace Microsoft.UI.Xaml.Media
{
    public abstract class Brush { }
    public sealed class SolidColorBrush(global::Windows.UI.Color color) : Brush { public global::Windows.UI.Color Color { get; } = color; }
}
namespace Microsoft.UI.Xaml.Media.Imaging { public sealed class BitmapImage(Uri uri) { public Uri UriSource { get; } = uri; } }
namespace Microsoft.UI { public static class Colors { public static global::Windows.UI.Color White => global::Windows.UI.Color.FromArgb(255, 255, 255, 255); } }
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public static DispatcherQueue GetForCurrentThread() => new();
        public bool HasThreadAccess => true;
        public bool TryEnqueue(Action action) { action(); return true; }
        public DispatcherQueueTimer CreateTimer() => new();
    }
    public sealed class DispatcherQueueTimer
    {
        public TimeSpan Interval { get; set; }
        public event Action<object, object>? Tick { add { } remove { } }
        public void Start() { }
    }
}
namespace AnyDownloader.Windows.Services
{
    public sealed class DownloadPersistenceService
    {
        public Dictionary<Guid, FormatSelection> SavedSelections { get; } = new();
        public Task<List<PersistedDownloadData>> LoadAllAsync() => Task.FromResult(new List<PersistedDownloadData>());
        public Task SaveAsync(DownloadItemModel item)
        {
            SavedSelections[item.Id] = new(item.SelectedVideoFormatId, item.SelectedAudioFormatId,
                true, true, item.MergeToMp4, item.ItemOutputDirectory, item.FilenameOverride);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id) { SavedSelections.Remove(id); return Task.CompletedTask; }
    }
    public sealed record PersistedDownloadData(Guid Id, string Url, DownloadType Type, string SourcePlatform,
        DownloadState State, double Progress, string? FilePath, List<string>? FilePaths, string? ErrorMessage,
        string? Title, DateTimeOffset CreatedAt, List<TorrentFileEntry>? TorrentFiles, VideoInfoModel? VideoInfo,
        string? SelectedVideoFormatId, string? SelectedAudioFormatId, bool MergeToMp4, string? ItemOutputDirectory);
    internal sealed record TorrentMetadata(string Name, List<TorrentFileEntry> Files);
    internal static class MagnetMetadataFetcher
    {
        public static Task<TorrentMetadata?> FetchAsync(string url, string path, TimeSpan timeout)
            => throw new NotSupportedException("Network magnet resolution is outside the headless ViewModel tests.");
    }
}
