using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AnyDownloader.Windows.Services;

public static class DependencyUpdateCoordinator
{
    private static readonly IDependencyProbe Probe = new DependencyProbe();
    private static readonly DependencyUpdateStore Store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnyDownloader", "dependency-updates"), Probe);
    private static readonly SemaphoreSlim CheckLock = new(1, 1);
    private static Dictionary<string, string> _activePaths = new();
    public static string? StartupMessage { get; private set; }
    private static string? _videoBlockReason;
    public static string? VideoBlockReason
    {
        get => Volatile.Read(ref _videoBlockReason);
        private set => Volatile.Write(ref _videoBlockReason, value);
    }
    public static bool CanRollback { get { try { return Store.OwnsSession && Store.Active?.Generation is not null; } catch { return false; } } }
    public static bool Ready { get { try { return Store.Pending is not null; } catch { return false; } } }
    public static string? ResolveActive(string exe) => _activePaths.GetValueOrDefault(exe);

    // Run before creating a window or allowing any downloader process to start.
    public static void Initialize()
    {
        try
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                StartupMessage = "Automatic dependency updates currently support Windows x64. Use an app release for this architecture.";
                return;
            }
            if (OtherAppIsRunning())
                StartupMessage = "Another AnyDownloader window is open. Close it and restart to manage dependency updates.";
            else if (Store.AcquireSession(TimeSpan.FromSeconds(1)))
                StartupMessage = Store.ActivateAtStartupAsync(false).GetAwaiter().GetResult();
            else StartupMessage = "Dependency update state is still in use. Close and reopen the app to manage updates.";
            _activePaths = Store.ActivePaths();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Store.Dispose();
        }
        catch (Exception ex)
        {
            // Corrupt state or a locked disk must not prevent use of bundled dependencies.
            _activePaths = new();
            StartupMessage = "Using bundled dependencies; update state could not be loaded. " + ex.Message;
        }
    }

    public static void MarkHealthy()
    {
        try { Store.MarkHealthy(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { StartupMessage = "Dependencies are usable, but update health state could not be saved. " + ex.Message; }
    }

    public static async Task<DependencyUpdateResult> CheckAsync(CancellationToken ct)
    {
        if (!await CheckLock.WaitAsync(0, ct)) return new("An update check is already running.");
        try
        {
            using var http = DependencyUpdater.CreateHttpClient();
            var paths = new Dictionary<string, string>();
            if (BinaryResolver.FindYtDlp() is string yt) paths["yt-dlp.exe"] = yt;
            if (BinaryResolver.FindDeno() is string deno) paths["deno.exe"] = deno;
            var result = await new DependencyUpdater(http, Probe, Store).CheckAndStageAsync(paths, ct,
                local => VideoBlockReason = local.Necessary ? local.Message : null);
            VideoBlockReason = result.Necessary ? result.Message : null;
            return result;
        }
        finally { CheckLock.Release(); }
    }

    public static bool CanRestart(bool hasActiveJobs) => !hasActiveJobs && !OtherAppIsRunning();
    public static void RequestRollback() => Store.RequestRollback();

    private static bool OtherAppIsRunning()
    {
        foreach (var process in Process.GetProcessesByName("AnyDownloader.Windows"))
        {
            using (process) if (IsLiveOtherProcess(process, Environment.ProcessId)) return true;
        }
        return false;
    }

    internal static bool IsLiveOtherProcess(Process process, int currentId)
    {
        try { return process.Id != currentId && !process.HasExited; }
        catch (InvalidOperationException) { return false; } // exited during enumeration/disposed handle
        catch (System.ComponentModel.Win32Exception) { return true; } // uncertain: keep the restart guard
    }
}
