namespace AnyDownloader.Core.ResourceLocator;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;

/// <summary>
/// Result of video resource location
/// </summary>
public class VideoResourceInfo
{
    public string VideoUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public long? FileSize { get; set; }
    public string? ThumbnailUrl { get; set; }
    public TimeSpan? Duration { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public bool IsStream { get; set; }
    public string? StreamType { get; set; } // HLS, DASH, progressive
}

/// <summary>
/// Locates video resources from web pages
/// </summary>
public class VideoResourceLocator
{
    private readonly HttpClient _httpClient;

    public VideoResourceLocator(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });
        
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    /// <summary>
    /// Locates video resources from a URL
    /// </summary>
    public async Task<VideoResourceInfo?> LocateAsync(string url, CancellationToken ct = default)
    {
        var uri = new Uri(url);
        
        return uri.Host.ToLower().Contains("youtube.com") || uri.Host.ToLower().Contains("youtu.be") 
            ? await LocateYouTubeAsync(url, ct)
            : uri.Host.ToLower().Contains("tiktok.com") 
                ? await LocateTikTokAsync(url, ct)
                : uri.Host.ToLower().Contains("bilibili.com")
                    ? await LocateBilibiliAsync(url, ct)
                    : uri.Host.ToLower().Contains("douyin.com")
                        ? await LocateDouyinAsync(url, ct)
                        : await LocateGenericAsync(url, ct);
    }

    private async Task<VideoResourceInfo?> LocateYouTubeAsync(string url, CancellationToken ct)
    {
        // Fetch the page
        var html = await _httpClient.GetStringAsync(url, ct);
        
        // Extract video info from YouTube player response
        var playerResponse = ExtractJsonFromHtml(html, "ytInitialPlayerResponse");
        if (playerResponse != null)
        {
            return ParseYouTubePlayerResponse(playerResponse);
        }
        
        // Fallback: Look for video URLs in the page
        return await LocateGenericAsync(url, ct);
    }

    private async Task<VideoResourceInfo?> LocateTikTokAsync(string url, CancellationToken ct)
    {
        var html = await _httpClient.GetStringAsync(url, ct);

        // Look for video URL in TikTok's data
        var videoUrlMatch = Regex.Match(html, @"""downloadAddr"":\s*""(https?://[^""]+)""");
        if (videoUrlMatch.Success)
        {
            return new VideoResourceInfo
            {
                VideoUrl = videoUrlMatch.Groups[1].Value.Replace("\\/", "/"),
                Title = "TikTok Video",
                Format = "mp4",
                Headers = new Dictionary<string, string>
                {
                    { "Referer", "https://www.tiktok.com/" }
                }
            };
        }
        
        return await LocateGenericAsync(url, ct);
    }

    private async Task<VideoResourceInfo?> LocateBilibiliAsync(string url, CancellationToken ct)
    {
        var html = await _httpClient.GetStringAsync(url, ct);
        
        // Extract Bilibili video data
        var playInfo = ExtractJsonFromHtml(html, "window.__playinfo__");
        if (playInfo != null)
        {
            return ParseBilibiliPlayInfo(playInfo);
        }
        
        return await LocateGenericAsync(url, ct);
    }

    private async Task<VideoResourceInfo?> LocateDouyinAsync(string url, CancellationToken ct)
    {
        // Similar to TikTok
        return await LocateGenericAsync(url, ct);
    }

    private async Task<VideoResourceInfo?> LocateGenericAsync(string url, CancellationToken ct)
    {
        var html = await _httpClient.GetStringAsync(url, ct);
        
        // Look for video tags
        var videoMatch = Regex.Match(html, @"<video[^>]*src=[""']([^""']+)[""']");
        if (videoMatch.Success)
        {
            return new VideoResourceInfo
            {
                VideoUrl = MakeAbsoluteUrl(videoMatch.Groups[1].Value, url),
                Title = ExtractTitle(html),
                Format = GetFormatFromUrl(videoMatch.Groups[1].Value)
            };
        }
        
        // Look for HLS streams (.m3u8)
        var hlsMatch = Regex.Match(html, @"(https?://[^\s""']+\.m3u8[^\s""']*)");
        if (hlsMatch.Success)
        {
            return new VideoResourceInfo
            {
                VideoUrl = hlsMatch.Groups[1].Value,
                Title = ExtractTitle(html),
                Format = "m3u8",
                IsStream = true,
                StreamType = "HLS"
            };
        }
        
        // Look for DASH streams (.mpd)
        var dashMatch = Regex.Match(html, @"(https?://[^\s""']+\.mpd[^\s""']*)");
        if (dashMatch.Success)
        {
            return new VideoResourceInfo
            {
                VideoUrl = dashMatch.Groups[1].Value,
                Title = ExtractTitle(html),
                Format = "mpd",
                IsStream = true,
                StreamType = "DASH"
            };
        }
        
        // Look for direct mp4/webm links
        var directMatch = Regex.Match(html, @"(https?://[^\s""']+\.(mp4|webm|mkv|avi|mov)[^\s""']*)", RegexOptions.IgnoreCase);
        if (directMatch.Success)
        {
            return new VideoResourceInfo
            {
                VideoUrl = directMatch.Groups[1].Value,
                Title = ExtractTitle(html),
                Format = directMatch.Groups[2].Value.ToLower()
            };
        }
        
        return null;
    }

    private static string? ExtractJsonFromHtml(string html, string variableName)
    {
        var pattern = $@"{variableName}\s*=\s*(\{{.*?\}});";
        var match = Regex.Match(html, pattern, RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static VideoResourceInfo ParseYouTubePlayerResponse(string json)
    {
        // Simplified parsing - in production, use proper JSON parser
        var videoUrl = Regex.Match(json, @"""url"":\s*""(https?://[^\\""]+)""");
        var title = Regex.Match(json, @"""title"":\s*""([^""]+)""");
        
        return new VideoResourceInfo
        {
            VideoUrl = videoUrl.Success ? videoUrl.Groups[1].Value.Replace("\\/", "/") : "",
            Title = title.Success ? title.Groups[1].Value : "YouTube Video",
            Format = "mp4"
        };
    }

    private static VideoResourceInfo ParseBilibiliPlayInfo(string json)
    {
        // Extract video URL from Bilibili play info
        var videoMatch = Regex.Match(json, @"""baseUrl"":\s*""(https?://[^\\""]+)""");
        
        return new VideoResourceInfo
        {
            VideoUrl = videoMatch.Success ? videoMatch.Groups[1].Value.Replace("\\/", "/") : "",
            Title = "Bilibili Video",
            Format = "mp4",
            Headers = new Dictionary<string, string>
            {
                { "Referer", "https://www.bilibili.com/" }
            }
        };
    }

    private static string MakeAbsoluteUrl(string relativeUrl, string baseUrl)
    {
        if (relativeUrl.StartsWith("http"))
            return relativeUrl;
        
        try
        {
            var baseUri = new Uri(baseUrl);
            return new Uri(baseUri, relativeUrl).ToString();
        }
        catch
        {
            return relativeUrl;
        }
    }

    private static string ExtractTitle(string html)
    {
        var match = Regex.Match(html, @"<title>([^<]+)</title>");
        return match.Success ? match.Groups[1].Value.Trim() : "Video";
    }

    private static string GetFormatFromUrl(string url)
    {
        var ext = System.IO.Path.GetExtension(url).ToLower().TrimStart('.');
        return string.IsNullOrEmpty(ext) ? "mp4" : ext;
    }
}
