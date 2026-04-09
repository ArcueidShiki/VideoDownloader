namespace AnyDownloader.Core;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Validates and parses URLs for supported video platforms
/// </summary>
public class UrlValidator
{
    private static readonly Regex YouTubeRegex = new(
        @"^(https?://)?(www\.)?(youtube\.com|youtu\.be)/.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TikTokRegex = new(
        @"^(https?://)?(www\.)?(tiktok\.com|vm\.tiktok\.com|vt\.tiktok\.com)/.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BilibiliRegex = new(
        @"^(https?://)?(www\.)?(bilibili\.com)/.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DouyinRegex = new(
        @"^(https?://)?(www\.)?(douyin\.com)/.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NetflixRegex = new(
        @"^(https?://)?(www\.)?(netflix\.com)/.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MagnetRegex = new(
        @"^magnet:\?.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HttpRegex = new(
        @"^https?://.+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // List of known video platform domains
    private static readonly HashSet<string> KnownVideoDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "youtu.be", "www.youtube.com",
        "tiktok.com", "www.tiktok.com", "vm.tiktok.com", "vt.tiktok.com",
        "bilibili.com", "www.bilibili.com",
        "douyin.com", "www.douyin.com",
        "netflix.com", "www.netflix.com"
    };

    /// <summary>
    /// Validates if the given URL is a supported video platform URL
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <returns>True if the URL is valid and supported, false otherwise</returns>
    public bool IsValid(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Check for specific platforms first
        if (IsYouTubeUrl(url) || IsTikTokUrl(url) || IsBilibiliUrl(url) ||
            IsDouyinUrl(url) || IsNetflixUrl(url) || IsMagnetLink(url))
            return true;

        // For generic HTTP URLs, only accept if they appear to be video URLs
        if (IsHttpUrl(url))
        {
            return IsLikelyVideoUrl(url);
        }

        return false;
    }

    /// <summary>
    /// Checks if an HTTP URL is likely to be a video URL based on domain or path patterns
    /// </summary>
    private static bool IsLikelyVideoUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();

            // Check if it's a known video platform domain
            foreach (var domain in KnownVideoDomains)
            {
                if (host.Contains(domain))
                    return true;
            }

            // Check for video-related path patterns
            var path = uri.AbsolutePath.ToLowerInvariant();
            if (path.Contains("/video/") || path.Contains("/watch") ||
                path.Contains("/v/") || path.Contains("/embed/"))
            {
                return true;
            }

            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the platform for the given URL
    /// </summary>
    /// <param name="url">The URL to analyze</param>
    /// <returns>The detected VideoPlatform</returns>
    public VideoPlatform GetPlatform(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return VideoPlatform.Unknown;

        if (IsYouTubeUrl(url))
            return VideoPlatform.YouTube;

        if (IsTikTokUrl(url))
            return VideoPlatform.TikTok;

        if (IsBilibiliUrl(url))
            return VideoPlatform.Bilibili;

        if (IsDouyinUrl(url))
            return VideoPlatform.Douyin;

        if (IsNetflixUrl(url))
            return VideoPlatform.Netflix;

        if (IsMagnetLink(url))
            return VideoPlatform.Magnet;

        // For HTTP URLs, check if they're from known video platforms
        if (IsHttpUrl(url))
        {
            return IsLikelyVideoUrl(url) ? VideoPlatform.Http : VideoPlatform.Unknown;
        }

        return VideoPlatform.Unknown;
    }

    /// <summary>
    /// Extracts the video ID from a platform URL
    /// </summary>
    /// <param name="url">The URL to extract the ID from</param>
    /// <returns>The video ID, or null if extraction fails</returns>
    public string? ExtractVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var platform = GetPlatform(url);

        return platform switch
        {
            VideoPlatform.YouTube => ExtractYouTubeVideoId(url),
            VideoPlatform.TikTok => ExtractTikTokVideoId(url),
            VideoPlatform.Bilibili => ExtractBilibiliVideoId(url),
            VideoPlatform.Douyin => ExtractDouyinVideoId(url),
            _ => null
        };
    }

    private bool IsYouTubeUrl(string url) => YouTubeRegex.IsMatch(url);

    private bool IsTikTokUrl(string url) => TikTokRegex.IsMatch(url);

    private bool IsBilibiliUrl(string url) => BilibiliRegex.IsMatch(url);

    private bool IsDouyinUrl(string url) => DouyinRegex.IsMatch(url);

    private bool IsNetflixUrl(string url) => NetflixRegex.IsMatch(url);

    private bool IsMagnetLink(string url) => MagnetRegex.IsMatch(url);

    private bool IsHttpUrl(string url) => HttpRegex.IsMatch(url);

    private string? ExtractYouTubeVideoId(string url)
    {
        try
        {
            var uri = new Uri(url);

            // Short URL format: https://youtu.be/VIDEO_ID
            if (uri.Host.Contains("youtu.be"))
            {
                return uri.AbsolutePath.TrimStart('/');
            }

            // Standard format: https://www.youtube.com/watch?v=VIDEO_ID
            if (uri.Host.Contains("youtube.com"))
            {
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var videoId = query["v"];
                if (!string.IsNullOrEmpty(videoId))
                    return videoId;

                // Embed format: https://www.youtube.com/embed/VIDEO_ID
                var pathParts = uri.AbsolutePath.Split('/');
                if (pathParts.Length >= 3 && pathParts[1] == "embed")
                {
                    return pathParts[2];
                }
            }
        }
        catch (UriFormatException)
        {
            // Invalid URI format
        }

        return null;
    }

    private string? ExtractTikTokVideoId(string url)
    {
        try
        {
            var uri = new Uri(url);
            var pathParts = uri.AbsolutePath.Split('/');

            // Format: https://www.tiktok.com/@username/video/VIDEO_ID
            var videoIndex = Array.IndexOf(pathParts, "video");
            if (videoIndex >= 0 && videoIndex < pathParts.Length - 1)
            {
                return pathParts[videoIndex + 1];
            }

            // Short format: https://vm.tiktok.com/SHORT_CODE/
            // The actual video ID needs to be resolved through the redirect
            if (uri.Host.Contains("vm.tiktok.com") || uri.Host.Contains("vt.tiktok.com"))
            {
                return pathParts.LastOrDefault(p => !string.IsNullOrEmpty(p));
            }
        }
        catch (UriFormatException)
        {
            // Invalid URI format
        }

        return null;
    }

    private string? ExtractBilibiliVideoId(string url)
    {
        try
        {
            var uri = new Uri(url);

            // Format: https://www.bilibili.com/video/BV1234567890
            // or: https://www.bilibili.com/video/av1234567890
            var pathParts = uri.AbsolutePath.Split('/');
            if (pathParts.Length >= 3 && pathParts[1] == "video")
            {
                return pathParts[2];
            }

            // Bangumi format: https://www.bilibili.com/bangumi/play/ep123456
            if (pathParts.Length >= 4 && pathParts[1] == "bangumi" && pathParts[2] == "play")
            {
                return pathParts[3];
            }
        }
        catch (UriFormatException)
        {
            // Invalid URI format
        }

        return null;
    }

    private string? ExtractDouyinVideoId(string url)
    {
        try
        {
            var uri = new Uri(url);
            var pathParts = uri.AbsolutePath.Split('/');

            // Format: https://www.douyin.com/video/VIDEO_ID
            var videoIndex = Array.IndexOf(pathParts, "video");
            if (videoIndex >= 0 && videoIndex < pathParts.Length - 1)
            {
                return pathParts[videoIndex + 1];
            }

            // Share format: https://www.douyin.com/share/video/VIDEO_ID
            var shareIndex = Array.IndexOf(pathParts, "share");
            if (shareIndex >= 0 && shareIndex < pathParts.Length - 2 && pathParts[shareIndex + 1] == "video")
            {
                return pathParts[shareIndex + 2];
            }
        }
        catch (UriFormatException)
        {
            // Invalid URI format
        }

        return null;
    }
}
