namespace AnyDownloader.Core;

/// <summary>
/// Supported video platforms
/// </summary>
public enum VideoPlatform
{
    /// <summary>
    /// Unknown or unsupported platform
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// YouTube
    /// </summary>
    YouTube = 1,

    /// <summary>
    /// TikTok
    /// </summary>
    TikTok = 2,

    /// <summary>
    /// Bilibili
    /// </summary>
    Bilibili = 3,

    /// <summary>
    /// Douyin
    /// </summary>
    Douyin = 4,

    /// <summary>
    /// Netflix
    /// </summary>
    Netflix = 5,

    /// <summary>
    /// Magnet link / Torrent
    /// </summary>
    Magnet = 6,

    /// <summary>
    /// Generic HTTP/HTTPS download
    /// </summary>
    Http = 7
}
