namespace AnyDownloader.Core;

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Host checks that keep URL handling from reaching loopback, private,
/// link-local or cloud-metadata addresses (SSRF protection).
/// </summary>
public static class HostSafety
{
    /// <summary>
    /// True when <paramref name="host"/> equals <paramref name="domain"/> or is a
    /// proper subdomain of it. "youtube.com.evil.com" does not match "youtube.com".
    /// </summary>
    public static bool MatchesDomain(string host, string domain)
    {
        var h = host.TrimEnd('.').ToLowerInvariant();
        var d = domain.TrimEnd('.').ToLowerInvariant();
        return h == d || h.EndsWith("." + d, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rejects host names and IP literals that obviously point inside the
    /// machine or network, without performing DNS lookups.
    /// </summary>
    public static bool IsObviouslyNonPublicHost(string host)
    {
        var h = host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (h.Length == 0 || h == "localhost" || h.EndsWith(".localhost", StringComparison.Ordinal)
            || h.EndsWith(".local", StringComparison.Ordinal) || h.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }
        return IPAddress.TryParse(h, out var address) && IsNonPublicAddress(address);
    }

    /// <summary>
    /// Resolves <paramref name="host"/> and returns true only when every
    /// resolved address is public. Resolution failures count as unsafe.
    /// </summary>
    public static async Task<bool> ResolvesToPublicAddressesAsync(string host, CancellationToken cancellationToken = default)
    {
        if (IsObviouslyNonPublicHost(host))
        {
            return false;
        }
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host.Trim('[', ']'), cancellationToken);
            return addresses.Length > 0 && addresses.All(a => !IsNonPublicAddress(a));
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// True for loopback, private (RFC 1918 / ULA), link-local (incl. the
    /// 169.254.169.254 metadata endpoint), CGNAT, multicast and unspecified addresses.
    /// </summary>
    public static bool IsNonPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var first = address.GetAddressBytes()[0];
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (first & 0xFE) == 0xFC; // fc00::/7 unique local
        }
        var b = address.GetAddressBytes();
        return b[0] == 0                                   // 0.0.0.0/8
            || b[0] == 10                                  // 10.0.0.0/8
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)  // 100.64/10 carrier-grade NAT
            || b[0] == 127                                 // 127.0.0.0/8
            || (b[0] == 169 && b[1] == 254)                // 169.254.0.0/16 link-local / metadata
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)   // 172.16.0.0/12
            || (b[0] == 192 && b[1] == 168)                // 192.168.0.0/16
            || b[0] >= 224;                                // multicast + reserved
    }
}
