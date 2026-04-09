namespace AnyDownloader.Core;

/// <summary>
/// User subscription tiers for download prioritization
/// </summary>
public enum UserTier
{
    /// <summary>
    /// Free tier - lowest priority, rate limited
    /// </summary>
    Free = 0,

    /// <summary>
    /// Premium tier - medium priority, higher limits
    /// </summary>
    Premium = 1,

    /// <summary>
    /// VIP tier - highest priority, no limits
    /// </summary>
    VIP = 2
}

/// <summary>
/// Download priority levels
/// </summary>
public enum DownloadPriority
{
    /// <summary>
    /// Lowest priority (free users)
    /// </summary>
    Low = 0,

    /// <summary>
    /// Normal priority
    /// </summary>
    Normal = 1,

    /// <summary>
    /// High priority (premium users)
    /// </summary>
    High = 2,

    /// <summary>
    /// Highest priority (VIP users)
    /// </summary>
    Critical = 3
}
