using System.Diagnostics;

namespace AnyDownloader.Windows.Services;

/// <summary>Owns process handles until their output drains; serializes kill and disposal.</summary>
public sealed class DownloadProcessRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Process> _processes = new();
    public int Count { get { lock (_gate) return _processes.Count; } }

    public void Track(Guid id, Process process)
    {
        lock (_gate) _processes[id] = process;
    }

    public async Task<int> WaitAndReleaseAsync(Guid id, Process process, Task<int> exit)
    {
        try { return await exit; }
        finally
        {
            lock (_gate)
            {
                if (_processes.TryGetValue(id, out var current) && ReferenceEquals(current, process))
                    _processes.Remove(id);
                process.Dispose();
            }
        }
    }

    public void Kill(Guid id)
    {
        lock (_gate)
        {
            if (!_processes.TryGetValue(id, out var process)) return;
            // Leave the entry until WaitAndReleaseAsync finishes: a stopped writer may
            // still be draining, so restarting the app is not safe yet.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                try { process.Kill(); }
                catch (Exception fallback) when (fallback is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }
}
