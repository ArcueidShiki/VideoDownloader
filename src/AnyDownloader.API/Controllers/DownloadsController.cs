namespace AnyDownloader.API.Controllers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

/// <summary>
/// API Controller for download management.
///
/// Identity and tier are derived on the server only: from an authenticated
/// principal when one exists, otherwise the caller is an anonymous Free user.
/// Every per-item operation requires ownership — either the same authenticated
/// user, or the secret owner token returned when the item was created
/// (sent back in the <c>X-Download-Token</c> header).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DownloadsController : ControllerBase
{
    /// <summary>Header carrying the owner token of an anonymous download.</summary>
    public const string OwnerTokenHeader = "X-Download-Token";

    /// <summary>Claim type that carries the server-issued subscription tier.</summary>
    public const string TierClaimType = "tier";

    private const int OwnerTokenBytes = 32;

    /// <summary>Language codes, commas and yt-dlp's regex shorthand only.</summary>
    private static readonly Regex SubtitleLanguagePattern = new(@"^[A-Za-z0-9,.*_-]{1,64}$", RegexOptions.Compiled);

    private readonly IDownloadQueueManager _queueManager;
    private readonly ILogger<DownloadsController> _logger;

    /// <summary>
    /// Initializes a new instance of the DownloadsController
    /// </summary>
    public DownloadsController(
        IDownloadQueueManager queueManager,
        ILogger<DownloadsController> logger)
    {
        _queueManager = queueManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets the caller's downloads (queued, active). Anonymous callers have no
    /// listable identity and receive empty lists; aggregate statistics are public.
    /// </summary>
    [HttpGet]
    public IActionResult GetAllDownloads()
    {
        var userId = GetAuthenticatedUserId();
        var queued = userId == null
            ? new List<DownloadQueueItem>()
            : _queueManager.GetQueuedItems().Where(i => i.UserId == userId).ToList();
        var active = userId == null
            ? new List<DownloadQueueItem>()
            : _queueManager.GetActiveDownloads().Where(i => i.UserId == userId).ToList();

        return Ok(new
        {
            queued,
            active,
            statistics = _queueManager.GetStatistics()
        });
    }

    /// <summary>
    /// Adds a new download to the queue
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("downloads_post")]
    public async Task<IActionResult> AddDownload([FromBody] DownloadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "URL is required" });
        }

        var validator = new UrlValidator();
        if (!validator.IsValid(request.Url))
        {
            return BadRequest(new { error = "Invalid URL" });
        }

        if (request.SubtitleLanguage != null && !SubtitleLanguagePattern.IsMatch(request.SubtitleLanguage))
        {
            return BadRequest(new { error = "Invalid subtitle language" });
        }

        if (!await IsPublicTargetAsync(request.Url, cancellationToken))
        {
            return BadRequest(new { error = "URL must point to a public internet host" });
        }

        try
        {
            var userId = GetAuthenticatedUserId();
            var ownerToken = userId == null ? CreateOwnerToken() : null;
            var item = new DownloadQueueItem
            {
                Url = request.Url,
                Options = new DownloadOptions
                {
                    Quality = request.Quality,
                    Format = request.Format,
                    DownloadSubtitles = request.DownloadSubtitles,
                    SubtitleLanguage = request.SubtitleLanguage
                },
                UserId = userId,
                UserTier = GetServerSideTier(),
                OwnerToken = ownerToken
            };

            var position = await _queueManager.EnqueueAsync(item);

            _logger.LogInformation("Download added: {Id}, Position: {Position}", item.Id, position);

            return Ok(new
            {
                id = item.Id,
                url = item.Url,
                queuePosition = position,
                estimatedWait = item.EstimatedWaitSeconds,
                ownerToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add download");
            return StatusCode(500, new { error = "Failed to add download" });
        }
    }

    /// <summary>
    /// Cancels a download
    /// </summary>
    [HttpDelete("{id}")]
    public Task<IActionResult> CancelDownload(string id) =>
        RunOwnedOperationAsync(id, "cancel", _queueManager.CancelDownloadAsync,
            ok: "Download cancelled", rejected: () => NotFound(new { error = "Download not found" }));

    /// <summary>
    /// Pauses a download
    /// </summary>
    [HttpPost("{id}/pause")]
    public Task<IActionResult> PauseDownload(string id) =>
        RunOwnedOperationAsync(id, "pause", _queueManager.PauseDownloadAsync,
            ok: "Download paused", rejected: () => BadRequest(new { error = "Cannot pause this download" }));

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    [HttpPost("{id}/resume")]
    public Task<IActionResult> ResumeDownload(string id) =>
        RunOwnedOperationAsync(id, "resume", _queueManager.ResumeDownloadAsync,
            ok: "Download resumed", rejected: () => BadRequest(new { error = "Cannot resume this download" }));

    /// <summary>
    /// Gets download status
    /// </summary>
    [HttpGet("{id}/status")]
    public IActionResult GetDownloadStatus(string id)
    {
        var item = _queueManager.GetItem(id);

        // Unknown and not-owned items look identical so ids cannot be probed.
        if (item == null || !CallerOwns(item))
        {
            return NotFound(new { error = "Download not found" });
        }

        return Ok(item);
    }

    /// <summary>
    /// Gets queue statistics
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStatistics()
    {
        var stats = _queueManager.GetStatistics();
        return Ok(new
        {
            queueLength = _queueManager.QueueLength,
            activeDownloads = _queueManager.ActiveDownloads,
            statistics = stats
        });
    }

    private async Task<IActionResult> RunOwnedOperationAsync(
        string id,
        string operation,
        Func<string, Task<bool>> action,
        string ok,
        Func<IActionResult> rejected)
    {
        var item = _queueManager.GetItem(id);
        if (item == null || !CallerOwns(item))
        {
            return NotFound(new { error = "Download not found" });
        }

        try
        {
            return await action(id) ? Ok(new { message = ok }) : rejected();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} download: {Id}", operation, id);
            return StatusCode(500, new { error = $"Failed to {operation} download" });
        }
    }

    private bool CallerOwns(DownloadQueueItem item)
    {
        var userId = GetAuthenticatedUserId();
        if (userId != null && item.UserId == userId)
        {
            return true;
        }
        if (string.IsNullOrEmpty(item.OwnerToken))
        {
            return false;
        }
        var presented = Request.Headers[OwnerTokenHeader].ToString();
        return presented.Length > 0 && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(item.OwnerToken));
    }

    private string? GetAuthenticatedUserId() =>
        User.Identity?.IsAuthenticated == true
            ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;

    /// <summary>
    /// Tier comes only from a claim issued by the server's authentication, never
    /// from the request body. Unauthenticated callers are always Free.
    /// </summary>
    private UserTier GetServerSideTier()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return UserTier.Free;
        }
        return User.FindFirst(TierClaimType)?.Value?.ToLowerInvariant() switch
        {
            "vip" => UserTier.VIP,
            "premium" => UserTier.Premium,
            _ => UserTier.Free
        };
    }

    private static string CreateOwnerToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(OwnerTokenBytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Resolves http(s) hosts and rejects any that map to loopback, private,
    /// link-local or metadata addresses, including every magnet tracker/seed/peer.
    /// </summary>
    private static async Task<bool> IsPublicTargetAsync(string url, CancellationToken cancellationToken)
    {
        if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            // Trackers, web seeds and peers named in the link are contacted too.
            return await MagnetLinkSafety.AllSourcesPublicAsync(url, cancellationToken);
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        return await HostSafety.ResolvesToPublicAddressesAsync(uri.IdnHost, cancellationToken);
    }
}

/// <summary>
/// Request model for adding a download. Identity and tier are intentionally
/// absent: the server derives them and ignores any such fields a client sends.
/// </summary>
public class DownloadRequest
{
    /// <summary>
    /// URL to download
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Video quality
    /// </summary>
    public QualityLevel Quality { get; set; } = QualityLevel.Auto;

    /// <summary>
    /// Output format
    /// </summary>
    public VideoFormat Format { get; set; } = VideoFormat.Mp4;

    /// <summary>
    /// Download subtitles
    /// </summary>
    public bool DownloadSubtitles { get; set; }

    /// <summary>
    /// Subtitle language
    /// </summary>
    public string? SubtitleLanguage { get; set; }
}
