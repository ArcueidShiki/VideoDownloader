namespace AnyDownloader.Core;

using System;
using System.Collections.Generic;
using System.Web;

/// <summary>
/// Magnet link parser
/// </summary>
public class MagnetLinkParser
{
    /// <summary>
    /// Parsed magnet link information
    /// </summary>
    public class MagnetInfo
    {
        public string InfoHash { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<string> Trackers { get; set; } = new();
        public string OriginalUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Parses a magnet link URL
    /// </summary>
    public static MagnetInfo? Parse(string magnetUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(magnetUrl) || !magnetUrl.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return null;

            var info = new MagnetInfo { OriginalUrl = magnetUrl };

            // Parse query parameters
            var queryStart = magnetUrl.IndexOf('?');
            if (queryStart < 0)
                return null;

            var queryString = magnetUrl.Substring(queryStart + 1);
            var parameters = ParseQueryString(queryString);

            // Extract info hash (xt parameter)
            if (parameters.TryGetValue("xt", out var xt))
            {
                // Format: urn:btih:HASH
                if (xt.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
                {
                    info.InfoHash = xt.Substring(9).Trim();
                }
            }

            if (string.IsNullOrEmpty(info.InfoHash))
                return null;

            // Extract display name (dn parameter)
            if (parameters.TryGetValue("dn", out var dn))
            {
                info.DisplayName = dn;
            }

            // Extract trackers (tr parameter, can be multiple)
            foreach (var kvp in parameters)
            {
                if (kvp.Key == "tr")
                {
                    // URL decode the tracker URL
                    var trackerUrl = Uri.UnescapeDataString(kvp.Value);
                    if (!string.IsNullOrEmpty(trackerUrl))
                    {
                        info.Trackers.Add(trackerUrl);
                    }
                }
            }

            return info;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseQueryString(string queryString)
    {
        var parameters = new Dictionary<string, string>();
        
        if (string.IsNullOrEmpty(queryString))
            return parameters;

        var pairs = queryString.Split('&');
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=');
            if (kv.Length == 2)
            {
                var key = kv[0].Trim();
                var value = kv[1].Trim();
                
                if (!string.IsNullOrEmpty(key))
                {
                    if (parameters.ContainsKey(key))
                    {
                        // Handle multiple values (like trackers)
                        parameters[key] = parameters[key] + "|" + value;
                    }
                    else
                    {
                        parameters[key] = value;
                    }
                }
            }
        }

        return parameters;
    }

    /// <summary>
    /// Validates if a string is a valid magnet link
    /// </summary>
    public static bool IsValidMagnetLink(string url)
    {
        return !string.IsNullOrWhiteSpace(url) && 
               url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) &&
               Parse(url) != null;
    }
}
