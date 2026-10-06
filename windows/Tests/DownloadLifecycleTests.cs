using System.Diagnostics;
using System.Reflection;
using AnyDownloader.Windows.Models;
using AnyDownloader.Windows.Services;
using AnyDownloader.Windows.ViewModels;
using Xunit;

namespace AnyDownloader.Windows.Tests;

[CollectionDefinition("ViewModel process tests", DisableParallelization = true)]
public class ViewModelProcessCollection { }

[Collection("ViewModel process tests")]
public class DownloadLifecycleTests
{
    private static readonly string Cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static object Invoke(object target, string name, params object?[] args)
        => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args)!;
    private static DownloadItemModel Item(DownloadViewModel vm, DownloadType type)
    {
        var item = (DownloadItemModel)Invoke(vm, "CreateItem", "https://example.com/sample.mp4", type, "", null, null);
        vm.Downloads.Add(item);
        return item;
    }
    private static Task Run(DownloadViewModel vm, DownloadItemModel item, params string[] args)
        => (Task)Invoke(vm, "RunAria2cAsync", item, Cmd, args);

    [Theory]
    [InlineData(DownloadType.Http, 0, DownloadState.Completed)]
    [InlineData(DownloadType.Torrent, 0, DownloadState.Completed)]
    [InlineData(DownloadType.Http, 7, DownloadState.Failed)]
    public async Task Aria2TerminalStatesReleaseRestartGateEvenAfterRowsAreCleared(DownloadType type, int exit, DownloadState state)
    {
        var vm = new DownloadViewModel();
        var item = Item(vm, type);
        await Run(vm, item, "/d", "/c", $"exit {exit}").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(state, item.State);
        Assert.False(vm.HasActiveOperations);
        vm.ClearCompletedCommand.Execute(null);
        Assert.Empty(vm.Downloads);
        Assert.False(vm.HasActiveOperations);
    }

    [Fact]
    public async Task Aria2LaunchErrorReleasesRestartGate()
    {
        var vm = new DownloadViewModel();
        var item = Item(vm, DownloadType.Http);
        await ((Task)Invoke(vm, "RunAria2cAsync", item, Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".exe"), Array.Empty<string>()));
        Assert.Equal(DownloadState.Failed, item.State);
        Assert.False(vm.HasActiveOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrPauseThenImmediateRetryIgnoresOldAria2Exit(bool cancel)
    {
        var vm = new DownloadViewModel();
        var item = Item(vm, DownloadType.Http);
        var first = Run(vm, item, "/d", "/c", "ping -n 30 127.0.0.1 >nul");
        Assert.True(vm.HasActiveOperations);
        if (cancel) vm.CancelItem(item); else item.PauseCommand.Execute(null);
        var retry = Run(vm, item, "/d", "/c", "exit 0");
        await Task.WhenAll(first, retry).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(DownloadState.Completed, item.State);
        Assert.False(vm.HasActiveOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilenameProbeFailureRetainsConfirmedSelectionForRetryAndAudioResume(bool audioOnly)
    {
        var toolDir = Path.Combine(AppContext.BaseDirectory, "tools");
        var tool = Path.Combine(toolDir, "yt-dlp.exe");
        Directory.CreateDirectory(toolDir);
        Assert.False(File.Exists(tool));
        // A deterministic failed subprocess, without contacting a media site.
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), tool);
        try
        {
            var vm = new DownloadViewModel();
            var item = Item(vm, DownloadType.YtDlp);
            var info = new VideoInfoModel("sample", null, null, null,
                [new("v1", "1080p", "mp4", "H.264", null, false)], [new("a1", "128 kbps", "m4a", 128, null)], true);
            ((Dictionary<string, VideoInfoModel>)Field(vm, "_videoInfoCache"))[item.Url] = info;
            var selection = new FormatSelection(audioOnly ? null : "v1", "a1", !audioOnly, true, false,
                Path.Combine(Path.GetTempPath(), "chosen-output"));
            int pickerCount = 0;
            vm.FormatPickerRequested += row => { pickerCount++; vm.ConfirmFormat(row.Id, selection); };
            await ((Task)Invoke(vm, "FetchYtDlpMetadataAsync", item)).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(DownloadState.Failed, item.State);
            Assert.Equal(selection.VideoFormatId, item.SelectedVideoFormatId);
            Assert.Equal(selection.AudioFormatId, item.SelectedAudioFormatId);
            Assert.Equal(selection.OutputDirectory, item.ItemOutputDirectory);
            Assert.Equal(selection.FilenameOverride, item.FilenameOverride);
            Assert.False(item.MergeToMp4);
            var saved = ((DownloadPersistenceService)Field(vm, "_persistence")).SavedSelections[item.Id];
            Assert.Equal(selection.OutputDirectory, saved.OutputDirectory);
            Assert.Equal(selection.AudioFormatId, saved.AudioFormatId);
            item.RetryCommand.Execute(null);
            await ((Task)Invoke(vm, "RestartAfterPauseAsync", item));
            Assert.Equal(1, pickerCount);
            Assert.Equal(selection.OutputDirectory, item.ItemOutputDirectory);
        }
        finally { File.Delete(tool); }
    }

    [Fact]
    public async Task OldCompletionDoesNotRemoveReplacementAndFaultAlsoReleasesHandle()
    {
        var registry = new DownloadProcessRegistry();
        var id = Guid.NewGuid();
        var old = new Process();
        var current = new Process();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.Track(id, old);
        var oldExit = registry.WaitAndReleaseAsync(id, old, gate.Task);
        registry.Track(id, current);
        gate.SetResult(0);
        await oldExit;
        Assert.Equal(1, registry.Count);
        await Assert.ThrowsAsync<IOException>(() => registry.WaitAndReleaseAsync(id, current, Task.FromException<int>(new IOException("pipe failure"))));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task CancelCannotReadAHandleWhileCompletionDisposesIt()
    {
        var registry = new DownloadProcessRegistry();
        var id = Guid.NewGuid();
        var proc = new BlockingDisposeProcess();
        registry.Track(id, proc);
        var release = Task.Run(() => registry.WaitAndReleaseAsync(id, proc, Task.FromResult(0)));
        Assert.True(proc.Disposing.Wait(TimeSpan.FromSeconds(5)));
        var killing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = Task.Run(() => { killing.SetResult(); registry.Kill(id); });
        await killing.Task;
        Assert.False(cancel.IsCompleted);
        proc.Continue.Set();
        await Task.WhenAll(release, cancel).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public async Task PartialTreeKillFailureFallsBackToRootAndRetainsHandleUntilDrain()
    {
        var psi = new ProcessStartInfo(Cmd) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("/d"); psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("pause >nul");
        psi.RedirectStandardInput = true; // keep pause waiting without a visible console
        using var process = Process.Start(psi)!;
        var rootCalls = 0;
        var registry = new DownloadProcessRegistry(
            _ => throw new AggregateException(new System.ComponentModel.Win32Exception(5)),
            p => { rootCalls++; p.Kill(); });
        var id = Guid.NewGuid();
        registry.Track(id, process);
        try
        {
            registry.Kill(id);
            Assert.Equal(1, rootCalls);
            Assert.Equal(1, registry.Count);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await registry.WaitAndReleaseAsync(id, process, Task.FromResult(process.ExitCode));
            Assert.Equal(0, registry.Count);
        }
        finally { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }
    }

    [Fact]
    public async Task TreeKillDoesNotSwallowUnrelatedAggregatedFailures()
    {
        using var process = Process.GetCurrentProcess();
        var rootCalls = 0;
        var failure = new AggregateException(new InvalidDataException("unrelated"));
        var registry = new DownloadProcessRegistry(_ => throw failure, _ => rootCalls++);
        var id = Guid.NewGuid();
        registry.Track(id, process);
        Assert.Same(failure, Assert.Throws<AggregateException>(() => registry.Kill(id)));
        Assert.Equal(0, rootCalls);
        await registry.WaitAndReleaseAsync(id, process, Task.FromResult(0));
    }

    [Fact]
    public async Task ExitedProcessFromAnEnumerationSnapshotDoesNotBlockRestart()
    {
        var psi = new ProcessStartInfo(Cmd) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("/d"); psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("exit 0");
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        Assert.False(DependencyUpdateCoordinator.IsLiveOtherProcess(process, Environment.ProcessId));
        process.Dispose();
        Assert.False(DependencyUpdateCoordinator.IsLiveOtherProcess(process, Environment.ProcessId));
        using var self = Process.GetCurrentProcess();
        Assert.False(DependencyUpdateCoordinator.IsLiveOtherProcess(self, Environment.ProcessId));
        Assert.True(DependencyUpdateCoordinator.IsLiveOtherProcess(self, -1));
    }

    private sealed class BlockingDisposeProcess : Process
    {
        public ManualResetEventSlim Disposing { get; } = new();
        public ManualResetEventSlim Continue { get; } = new();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Disposing.Set(); Continue.Wait(TimeSpan.FromSeconds(5)); }
            base.Dispose(disposing);
        }
    }
}
