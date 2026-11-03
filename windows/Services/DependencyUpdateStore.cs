using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AnyDownloader.Windows.Services;

public sealed record StoredDependency(string Exe, string Version, string Sha256);
public sealed record DependencyGeneration(string Id, StoredDependency[] Tools);
public sealed record DependencyActivation(string? Generation, string? Previous, bool Healthy);
public sealed record PendingDependencies(string? Generation, bool Required, bool Rollback = false);

/// <summary>Immutable generations plus one atomic pointer. Bundled tools are never overwritten.</summary>
public sealed class DependencyUpdateStore : IDisposable
{
    public string Root { get; }
    private readonly IDependencyProbe _probe;
    private FileStream? _session;
    public bool OwnsSession => _session is not null;
    public string? StateWarning { get; private set; }
    public DependencyActivation? Active => Read<DependencyActivation>("active.json");
    public PendingDependencies? Pending => Read<PendingDependencies>("pending.json");

    public DependencyUpdateStore(string root, IDependencyProbe probe) { Root = Path.GetFullPath(root); _probe = probe; }

    public bool AcquireSession(TimeSpan retryFor = default)
    {
        Directory.CreateDirectory(Root);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                _session ??= new FileStream(Path.Combine(Root, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                if (elapsed.Elapsed >= retryFor) return false;
                Thread.Sleep(50); // bounded startup handoff; never relax exclusive sharing
            }
        }
    }

    public string GenerationPath(string id)
    {
        if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new InvalidDataException("Invalid dependency generation.");
        return Path.Combine(Root, "generations", id);
    }

    public Dictionary<string, string> ActivePaths()
    {
        var active = Active;
        if (active?.Generation is not string id) return new();
        var directory = GenerationPath(id);
        var generation = JsonSerializer.Deserialize<DependencyGeneration>(File.ReadAllText(Path.Combine(directory, "manifest.json")))
            ?? throw new InvalidDataException("Invalid dependency manifest.");
        ValidateManifest(generation, id);
        return generation.Tools.ToDictionary(t => t.Exe, t => Path.Combine(directory, t.Exe));
    }

    public async Task ValidateGenerationAsync(string id, CancellationToken ct)
    {
        var directory = GenerationPath(id);
        var manifest = JsonSerializer.Deserialize<DependencyGeneration>(File.ReadAllText(Path.Combine(directory, "manifest.json")))
            ?? throw new InvalidDataException("Invalid dependency manifest.");
        ValidateManifest(manifest, id);
        foreach (var tool in manifest.Tools)
        {
            var path = Path.Combine(directory, tool.Exe);
            if (!string.Equals(await HashAsync(path, ct), tool.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{tool.Exe} changed after verification.");
            await _probe.ValidateAsync(DependencySpec.All.Single(s => s.Exe == tool.Exe), path, tool.Version, ct);
        }
    }

    private static void ValidateManifest(DependencyGeneration manifest, string id)
    {
        if (manifest.Id != id || manifest.Tools.Length != DependencySpec.All.Length ||
            !manifest.Tools.Select(t => t.Exe).Order().SequenceEqual(DependencySpec.All.Select(s => s.Exe).Order()))
            throw new InvalidDataException("Incomplete dependency generation.");
    }

    public void SavePending(string id, bool required)
    {
        EnsureOwner();
        GenerationPath(id);
        Write("pending.json", new PendingDependencies(id, required));
    }

    public void RequestRollback()
    {
        EnsureOwner();
        if (Active?.Generation is null) throw new InvalidOperationException("Bundled dependencies are already active.");
        RejectGeneration(Active.Generation);
        Write("pending.json", new PendingDependencies(Active.Previous, false, true));
    }

    public bool WasRejected(string exe, string version)
        => Read<StoredDependency[]>("rejected.json")?.Any(t => t.Exe == exe && t.Version == version) == true;

    private void RejectGeneration(string? id)
    {
        if (id is null) return;
        try
        {
            var manifest = JsonSerializer.Deserialize<DependencyGeneration>(File.ReadAllText(Path.Combine(GenerationPath(id), "manifest.json")));
            if (manifest is null) return;
            ValidateManifest(manifest, id);
            Write("rejected.json", (Read<StoredDependency[]>("rejected.json") ?? []).Concat(manifest.Tools).Distinct().ToArray());
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { /* rollback still takes priority */ }
    }

    public async Task<string?> ActivateAtStartupAsync(bool hasActiveJobs, CancellationToken ct = default)
    {
        if (!OwnsSession || hasActiveJobs) return "Dependencies will switch after all app instances and downloads have stopped.";
        CleanupInterruptedStaging();
        var active = Active;
        if (active is null)
        {
            // Only restore a recorded healthy generation after validating it again.
            // If no usable record survives, retain the bundled install.
            var lastHealthy = Read<DependencyActivation>("last-healthy.json");
            if (lastHealthy is not null || StateWarning is not null)
            {
                await RestoreAsync(lastHealthy is { Healthy: true } ? lastHealthy.Generation : null, ct);
                StateWarning ??= "Recovered missing dependency state from the last healthy install.";
                active = Active;
            }
        }
        // A crash between pointer replacement and window creation leaves an unhealthy trial.
        if (active is { Healthy: false })
        {
            RejectGeneration(active.Generation);
            await RestoreAsync(active.Previous, ct);
            File.Delete(Path.Combine(Root, "pending.json"));
            return "Restored previous dependencies after an interrupted activation.";
        }
        var pending = Pending;
        if (pending is null)
        {
            if (active?.Generation is string current)
            {
                try { await ValidateGenerationAsync(current, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    RejectGeneration(current);
                    await RestoreAsync(active.Previous, ct);
                    return "Dependency validation failed; restored the previous version. " + ex.Message;
                }
            }
            return StateWarning;
        }
        try
        {
            if (pending.Generation is string id) await ValidateGenerationAsync(id, ct);
            else if (!pending.Rollback) throw new InvalidDataException("Invalid pending update.");
            Write("active.json", new DependencyActivation(pending.Generation, active?.Generation, false));
            File.Delete(Path.Combine(Root, "pending.json"));
            return pending.Rollback ? "Previous dependencies restored." : "Dependency update installed.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!pending.Rollback) RejectGeneration(pending.Generation);
            // Leave the last active pointer intact if validation or atomic replacement failed.
            File.Delete(Path.Combine(Root, "pending.json"));
            return "Update could not be installed; existing dependencies retained. " + ex.Message;
        }
    }

    private void CleanupInterruptedStaging()
    {
        var staging = Path.Combine(Root, "staging");
        if (!Directory.Exists(staging)) return;
        foreach (var directory in Directory.EnumerateDirectories(staging))
        {
            if (!Regex.IsMatch(Path.GetFileName(directory), "^[a-f0-9]{32}$") ||
                File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private async Task RestoreAsync(string? previous, CancellationToken ct)
    {
        if (previous is not null)
        {
            try { await ValidateGenerationAsync(previous, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { previous = null; }
        }
        Write("active.json", new DependencyActivation(previous, null, true));
        Write("last-healthy.json", new DependencyActivation(previous, null, true));
    }

    public void MarkHealthy()
    {
        if (OwnsSession && Active is { } active)
        {
            var healthy = active with { Healthy = true };
            Write("last-healthy.json", healthy);
            if (!active.Healthy) Write("active.json", healthy);
        }
    }

    private T? Read<T>(string name) where T : class
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                ?? throw new JsonException("Empty dependency state.");
        }
        catch (JsonException)
        {
            StateWarning = $"Ignored corrupt dependency state ({name}); retained a verified or bundled install.";
            if (OwnsSession)
            {
                try { File.Move(path, path + ".invalid-" + Guid.NewGuid().ToString("N")); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return null;
        }
    }

    private void Write<T>(string name, T value)
    {
        var path = Path.Combine(Root, name);
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, value);
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    private void EnsureOwner() { if (!OwnsSession) throw new InvalidOperationException("Another app instance manages dependency updates."); }
    public static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
    public void Dispose() { _session?.Dispose(); _session = null; }
}
