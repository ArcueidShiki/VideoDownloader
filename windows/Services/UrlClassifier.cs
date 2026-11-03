using System;
using System.Collections.Generic;
using System.IO;
using AnyDownloader.Windows.Models;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Pure-logic URL classifier with zero WinUI3 dependencies.
/// Extracted from DownloadViewModel so it can be unit-tested without the UI runtime.
/// Mirrors macOS URLClassifier.swift + platform-detection logic.
/// </summary>
public static class UrlClassifier
{
    // ─────────────────────────────────────────────────────
    // Supported yt-dlp hosts
    // ─────────────────────────────────────────────────────

    private static readonly HashSet<string> _ytDlpHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com",
        "music.youtube.com",
        "tiktok.com", "www.tiktok.com", "vm.tiktok.com", "vt.tiktok.com",
        "bilibili.com", "www.bilibili.com",
        "douyin.com", "www.douyin.com",
        "vimeo.com", "www.vimeo.com", "player.vimeo.com",
        "twitch.tv", "www.twitch.tv", "clips.twitch.tv",
        "twitter.com", "x.com", "www.twitter.com",
        "instagram.com", "www.instagram.com",
        "facebook.com", "www.facebook.com", "fb.watch",
        "dailymotion.com", "www.dailymotion.com",
        "nicovideo.jp", "www.nicovideo.jp",
        "weibo.com", "weibo.cn",
        "reddit.com", "www.reddit.com", "v.redd.it",
        "rumble.com", "www.rumble.com",
        "odysee.com", "www.odysee.com",
    };

    // ─────────────────────────────────────────────────────
    // Classify
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Classifies a URL or file path into the appropriate download engine type.
    /// </summary>
    public static DownloadType Classify(string input)
    {
        var t = input?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(t)) return DownloadType.Http;

        // Magnet links
        if (t.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            return DownloadType.Torrent;

        // Local .torrent file
        if (t.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            return DownloadType.Torrent;

        // Try parsing as URI
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
            return DownloadType.Http; // assume HTTP for bare domains

        // .torrent URL
        if (uri.AbsolutePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            return DownloadType.Torrent;

        // Known video platforms
        if (_ytDlpHosts.Contains(uri.Host))
            return DownloadType.YtDlp;

        return DownloadType.Http;
    }

    // ─────────────────────────────────────────────────────
    // Platform detection
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lowercase platform identifier for badge display.
    /// Returns an empty string for unknown / HTTP URLs.
    /// </summary>
    public static string DetectPlatform(string input)
    {
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri))
            return string.Empty;

        // Normalize host: strip leading "m.", "www.", etc.
        var host = uri.Host.ToLowerInvariant();
        host = host.TrimStart('m', '.');

        if (host.Contains("youtube.com") || host == "youtu.be") return "youtube";
        if (host.Contains("tiktok.com"))                         return "tiktok";
        if (host.Contains("bilibili.com"))                       return "bilibili";
        if (host.Contains("douyin.com"))                         return "douyin";
        if (host.Contains("vimeo.com"))                          return "vimeo";
        if (host.Contains("twitch.tv"))                          return "twitch";
        if (host.Contains("twitter.com") || host == "x.com")    return "twitter";
        if (host.Contains("instagram.com"))                      return "instagram";
        if (host.Contains("facebook.com") || host == "fb.watch") return "facebook";
        if (host.Contains("dailymotion.com"))                    return "dailymotion";
        if (host.Contains("reddit.com") || host == "v.redd.it") return "reddit";
        if (host.Contains("rumble.com"))                         return "rumble";

        return string.Empty;
    }

    // ─────────────────────────────────────────────────────
    // Validation
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the input can be processed as a download source.
    /// Accepts absolute HTTP/HTTPS URLs, magnet URIs, and local .torrent paths.
    /// </summary>
    public static bool IsValid(string input)
    {
        var t = input?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(t)) return false;

        if (t.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(t)) return true;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "http" or "https" or "ftp";
    }
}
