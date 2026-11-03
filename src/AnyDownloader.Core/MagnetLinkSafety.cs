namespace AnyDownloader.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Magnet links can name trackers (tr), web seeds (ws), sources (xs, as) and
/// direct peers (x.pe) that the torrent engine contacts. Every such host must
/// pass the same public-address check as an ordinary http(s) URL (SSRF).
/// </summary>
public static class MagnetLinkSafety
{
    private static readonly HashSet<string> ExternalSourceKeys =
        new(StringComparer.OrdinalIgnoreCase) { "tr", "ws", "xs", "as", "x.pe" };

    /// <summary>
    /// Returns the hosts of every external source in the magnet link, or null when
    /// a source cannot be parsed (callers must then reject the link).
    /// </summary>
    public static IReadOnlyList<string>? ExternalSourceHosts(string magnet)
    {
        var queryStart = magnet.IndexOf('?');
        if (queryStart < 0)
        {
            return Array.Empty<string>();
        }

        var hosts = new List<string>();
        foreach (var pair in magnet[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            if (!ExternalSourceKeys.Contains(BaseKey(key)))
            {
                continue;
            }
            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            var host = ExtractHost(value);
            if (host == null)
            {
                return null;
            }
            hosts.Add(host);
        }
        return hosts;
    }

    /// <summary>
    /// True when every tracker, web seed, source and peer host resolves only to
    /// public addresses. Unparseable sources make the whole link unsafe.
    /// </summary>
    public static async Task<bool> AllSourcesPublicAsync(string magnet, CancellationToken cancellationToken = default)
    {
        var hosts = ExternalSourceHosts(magnet);
        if (hosts == null)
        {
            return false;
        }
        foreach (var host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!await HostSafety.ResolvesToPublicAddressesAsync(host, cancellationToken))
            {
                return false;
            }
        }
        return true;
    }

    // "tr.1" / "tr.2" are numbered variants of "tr"; "x.pe" is its own key.
    private static string BaseKey(string key) =>
        key.Equals("x.pe", StringComparison.OrdinalIgnoreCase) ? key : key.Split('.')[0];

    private static string? ExtractHost(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.IdnHost;
        }
        // x.pe peers are plain "host:port" or "[ipv6]:port".
        var colon = value.LastIndexOf(':');
        var host = (colon > 0 ? value[..colon] : value).Trim('[', ']');
        return host.Length == 0 || host.Any(c => char.IsWhiteSpace(c) || c == '/') ? null : host;
    }
}
