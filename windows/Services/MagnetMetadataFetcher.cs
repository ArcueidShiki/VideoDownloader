using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Windows.Models;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Fetches torrent file metadata from a magnet link by running aria2c with
/// <c>--bt-metadata-only --bt-save-metadata</c>, then parsing the saved .torrent
/// with <see cref="BencodeParser"/>.
///
/// Mirrors macOS MagnetMetadataFetcher in AnyDownloaderCore.
/// </summary>
internal static class MagnetMetadataFetcher
{
    /// <summary>
    /// Resolves torrent metadata from a magnet link.
    /// Returns null if the peer-discovery times out.
    /// </summary>
    public static async Task<TorrentMetadata?> FetchAsync(
        string magnetUrl,
        string aria2cPath,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(45);
        var tmpDir = Path.Combine(Path.GetTempPath(),
            $"anydownloader-meta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);

        try
        {
            // Run aria2c in metadata-only mode:
            // --bt-metadata-only  → download only the .torrent file, not the actual content
            // --bt-save-metadata  → write the resolved .torrent to --dir
            var psi = new ProcessStartInfo(aria2cPath)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            foreach (var arg in BuildMetadataArgs(magnetUrl, tmpDir))
                psi.ArgumentList.Add(arg);

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Poll for the .torrent file to appear (aria2c writes it once DHT resolves)
            var deadline = DateTime.UtcNow.Add(effectiveTimeout);
            string? torrentFile = null;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                torrentFile = Directory.GetFiles(tmpDir, "*.torrent").FirstOrDefault();
                if (torrentFile is not null) break;
                await Task.Delay(500, ct).ConfigureAwait(false);
            }

            try { proc.Kill(entireProcessTree: true); } catch { try { proc.Kill(); } catch { } }

            if (torrentFile is null) return null;

            var data = await File.ReadAllBytesAsync(torrentFile, ct).ConfigureAwait(false);
            return TorrentMetadata.Parse(data);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }

    // ─────────────────────────────────────────────────────
    // Aria2c argument list (mirrors macOS TorrentEngine.buildArguments)
    // ─────────────────────────────────────────────────────

    private static string[] BuildMetadataArgs(string magnetUrl, string tmpDir) =>
    [
        "--bt-metadata-only",
        "--bt-save-metadata",
        $"--dir={tmpDir}",
        "--console-log-level=notice",
        "--enable-dht=true",
        "--enable-dht6=false",
        "--enable-peer-exchange=true",
        "--bt-enable-lpd=true",
        "--check-certificate=false",
        "--dht-entry-point=router.bittorrent.com:6881",
        "--dht-entry-point=router.utorrent.com:6881",
        "--dht-entry-point=dht.transmissionbt.com:6881",
        "--bt-tracker=udp://tracker.opentrackr.org:1337/announce",
        "--bt-tracker=udp://open.tracker.cl:1337/announce",
        "--bt-tracker=udp://tracker.openbittorrent.com:6969/announce",
        "--bt-tracker=udp://9.rarbg.com:2810/announce",
        "--bt-tracker=udp://tracker.torrent.eu.org:451/announce",
        "--bt-tracker-connect-timeout=20",
        "--bt-tracker-timeout=60",
        "--bt-max-peers=500",
        magnetUrl,
    ];
}

// ─────────────────────────────────────────────────────
// Parsed torrent structure (bencode → model)
// ─────────────────────────────────────────────────────

internal sealed record TorrentMetadata(string Name, IReadOnlyList<TorrentFileEntry> Files)
{
    /// <summary>Parses the raw bytes of a .torrent file into a <see cref="TorrentMetadata"/>.</summary>
    public static TorrentMetadata? Parse(byte[] data)
    {
        try
        {
            var parser = new BencodeParser(data);
            var root   = parser.Parse();
            if (root is not BDict rootDict) return null;

            var info = rootDict["info"] as BDict;
            if (info is null) return null;

            var name = (info["name"] as BBytes)?.AsString ?? "Unknown";

            // Multi-file torrent: info["files"] is a list of {path:[...], length:N}
            if (info["files"] is BList fileList)
            {
                var entries = new List<TorrentFileEntry>();
                int idx = 1;
                foreach (var fileVal in fileList.Items)
                {
                    if (fileVal is not BDict fileDict) continue;

                    var pathComponents = new List<string>();
                    if (fileDict["path"] is BList pathList)
                        foreach (var part in pathList.Items)
                            if (part is BBytes pb && pb.AsString is string seg)
                                pathComponents.Add(seg);

                    var filePath = pathComponents.Count > 0
                        ? string.Join("/", pathComponents)
                        : name;

                    long length = (fileDict["length"] as BInteger)?.Value ?? 0;

                    entries.Add(new TorrentFileEntry
                    {
                        Index         = idx++,
                        Filename      = filePath,
                        FilesizeBytes = length,
                        IsSelected    = true,
                    });
                }
                return new TorrentMetadata(name, entries);
            }

            // Single-file torrent: info["length"] is the total size
            {
                long length = (info["length"] as BInteger)?.Value ?? 0;
                return new TorrentMetadata(name, [new TorrentFileEntry
                {
                    Index         = 1,
                    Filename      = name,
                    FilesizeBytes = length,
                    IsSelected    = true,
                }]);
            }
        }
        catch
        {
            return null;
        }
    }
}
