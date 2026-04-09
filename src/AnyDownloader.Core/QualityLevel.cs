namespace AnyDownloader.Core;

/// <summary>
/// Video quality levels
/// </summary>
public enum QualityLevel
{
    /// <summary>
    /// Auto-select best available
    /// </summary>
    Auto = 0,

    /// <summary>
    /// 144p
    /// </summary>
    Q144p = 144,

    /// <summary>
    /// 240p
    /// </summary>
    Q240p = 240,

    /// <summary>
    /// 360p
    /// </summary>
    Q360p = 360,

    /// <summary>
    /// 480p
    /// </summary>
    Q480p = 480,

    /// <summary>
    /// 720p (HD)
    /// </summary>
    Q720p = 720,

    /// <summary>
    /// 1080p (Full HD)
    /// </summary>
    Q1080p = 1080,

    /// <summary>
    /// 1440p (2K)
    /// </summary>
    Q1440p = 1440,

    /// <summary>
    /// 2160p (4K)
    /// </summary>
    Q4K = 2160,

    /// <summary>
    /// 4320p (8K)
    /// </summary>
    Q8K = 4320
}
