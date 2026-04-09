namespace AnyDownloader.API.Controllers;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// API Controller for download management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DownloadsController : ControllerBase
{
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
    /// Gets all downloads (queued, active, completed)
    /// </summary>
    [HttpGet]
    public IActionResult GetAllDownloads()
    {
        var queued = _queueManager.GetQueuedItems();
        var active = _queueManager.GetActiveDownloads();
        
        return Ok(new
        {
            queued = queued,
            active = active,
            statistics = _queueManager.GetStatistics()
        });
    }

    /// <summary>
    /// Adds a new download to the queue
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> AddDownload([FromBody] DownloadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { error = "URL is required" });
        }

        // Validate URL
        var validator = new UrlValidator();
        if (!validator.IsValid(request.Url))
        {
            return BadRequest(new { error = "Invalid URL" });
        }

        try
        {
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
                UserId = request.UserId ?? "anonymous",
                UserTier = GetUserTierFromRequest(request)
            };

            var position = await _queueManager.EnqueueAsync(item);

            _logger.LogInformation("Download added: {Url}, Position: {Position}", request.Url, position);

            return Ok(new
            {
                id = item.Id,
                url = item.Url,
                queuePosition = position,
                estimatedWait = item.EstimatedWaitSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add download: {Url}", request.Url);
            return StatusCode(500, new { error = "Failed to add download" });
        }
    }

    /// <summary>
    /// Cancels a download
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> CancelDownload(string id)
    {
        try
        {
            var result = await _queueManager.CancelDownloadAsync(id);
            
            if (result)
            {
                return Ok(new { message = "Download cancelled" });
            }

            return NotFound(new { error = "Download not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel download: {Id}", id);
            return StatusCode(500, new { error = "Failed to cancel download" });
        }
    }

    /// <summary>
    /// Pauses a download
    /// </summary>
    [HttpPost("{id}/pause")]
    public async Task<IActionResult> PauseDownload(string id)
    {
        try
        {
            var result = await _queueManager.PauseDownloadAsync(id);
            
            if (result)
            {
                return Ok(new { message = "Download paused" });
            }

            return BadRequest(new { error = "Cannot pause this download" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause download: {Id}", id);
            return StatusCode(500, new { error = "Failed to pause download" });
        }
    }

    /// <summary>
    /// Resumes a paused download
    /// </summary>
    [HttpPost("{id}/resume")]
    public async Task<IActionResult> ResumeDownload(string id)
    {
        try
        {
            var result = await _queueManager.ResumeDownloadAsync(id);
            
            if (result)
            {
                return Ok(new { message = "Download resumed" });
            }

            return BadRequest(new { error = "Cannot resume this download" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume download: {Id}", id);
            return StatusCode(500, new { error = "Failed to resume download" });
        }
    }

    /// <summary>
    /// Gets download status
    /// </summary>
    [HttpGet("{id}/status")]
    public IActionResult GetDownloadStatus(string id)
    {
        var item = _queueManager.GetItem(id);
        
        if (item == null)
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

    private static UserTier GetUserTierFromRequest(DownloadRequest request)
    {
        return request.UserTier?.ToLower() switch
        {
            "vip" => UserTier.VIP,
            "premium" => UserTier.Premium,
            _ => UserTier.Free
        };
    }
}

/// <summary>
/// Request model for adding a download
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

    /// <summary>
    /// User ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// User tier (free, premium, vip)
    /// </summary>
    public string? UserTier { get; set; }
}
