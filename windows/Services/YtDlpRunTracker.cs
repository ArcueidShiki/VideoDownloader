namespace AnyDownloader.Windows.Services;

/// <summary>Invalidates old callbacks and waits for stopped writers before a retry.</summary>
public sealed class YtDlpRunTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, (Guid Token, Task Exit)> _runs = new();

    public async Task<Guid> BeginAsync(Guid id)
    {
        var token = Guid.NewGuid();
        Task previous;
        lock (_lock)
        {
            previous = _runs.TryGetValue(id, out var run) ? run.Exit : Task.CompletedTask;
            _runs[id] = (token, previous);
        }
        try { await previous; } catch { /* previous error must not prevent retry */ }
        return token;
    }

    public bool IsCurrent(Guid id, Guid token)
    {
        lock (_lock) return _runs.TryGetValue(id, out var run) && run.Token == token;
    }

    public void Track(Guid id, Guid token, Task exit)
    {
        lock (_lock)
            if (_runs.TryGetValue(id, out var run) && run.Token == token) _runs[id] = (token, exit);
    }

    public void Cancel(Guid id)
    {
        lock (_lock)
            if (_runs.TryGetValue(id, out var run)) _runs[id] = (Guid.Empty, run.Exit);
    }
}
