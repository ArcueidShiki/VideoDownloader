using System.Diagnostics;

namespace AnyDownloader.Windows.Services;

/// <summary>Owns process handles until their output drains; serializes kill and disposal.</summary>
public sealed class DownloadProcessRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Process> _processes = new();
    private readonly Action<Process> _killTree;
    private readonly Action<Process> _killRoot;
    public int Count { get { lock (_gate) return _processes.Count; } }

    public DownloadProcessRegistry() : this(p => p.Kill(entireProcessTree: true), p => p.Kill()) { }
    internal DownloadProcessRegistry(Action<Process> killTree, Action<Process> killRoot)
        { _killTree = killTree; _killRoot = killRoot; }

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
            try { if (!process.HasExited) _killTree(process); }
            catch (Exception ex) when (IsTerminationFailure(ex))
            {
                try { _killRoot(process); }
                catch (Exception fallback) when (fallback is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }

    private static bool IsTerminationFailure(Exception error) => error switch
    {
        InvalidOperationException or System.ComponentModel.Win32Exception => true,
        // .NET reports partial process-tree termination failures as an aggregate.
        // Only handle expected process errors, not unrelated failures wrapped in one.
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.Flatten().InnerExceptions.All(
            e => e is InvalidOperationException or System.ComponentModel.Win32Exception),
        _ => false
    };
}
