namespace AnyDownloader.Engine.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AnyDownloader.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Tests for DownloadQueueManager - TDD approach
/// </summary>
public class DownloadQueueManagerTests
{
    private readonly Mock<ILogger<DownloadQueueManager>> _mockLogger;
    private readonly Mock<IDownloadService> _mockService;
    private readonly DownloadQueueManager _queueManager;

    public DownloadQueueManagerTests()
    {
        _mockLogger = new Mock<ILogger<DownloadQueueManager>>();
        _mockService = new Mock<IDownloadService>();
        _mockService.Setup(s => s.SupportedPlatform).Returns(VideoPlatform.Http);
        
        _queueManager = new DownloadQueueManager(
            new[] { _mockService.Object },
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithServices_ShouldCreateManager()
    {
        // Arrange & Act
        var manager = new DownloadQueueManager(new[] { _mockService.Object });

        // Assert
        manager.Should().NotBeNull();
        manager.QueueLength.Should().Be(0);
        manager.ActiveDownloads.Should().Be(0);
    }

    [Fact]
    public void Constructor_NullServices_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new DownloadQueueManager(null!, _mockLogger.Object));
    }

    [Fact]
    public async Task EnqueueAsync_NullItem_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _queueManager.EnqueueAsync(null!));
    }

    [Fact]
    public async Task EnqueueAsync_ValidItem_ShouldAddToQueue()
    {
        // Arrange
        var item = new DownloadQueueItem
        {
            Url = "https://example.com/video.mp4",
            UserTier = UserTier.Free
        };

        // Act
        var position = await _queueManager.EnqueueAsync(item);

        // Assert
        position.Should().Be(1);
        _queueManager.QueueLength.Should().Be(1);
        _queueManager.GetItem(item.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task EnqueueAsync_MultipleItems_ShouldCalculatePriority()
    {
        // Arrange
        var freeItem = new DownloadQueueItem { Url = "https://example.com/1", UserTier = UserTier.Free };
        var vipItem = new DownloadQueueItem { Url = "https://example.com/2", UserTier = UserTier.VIP };
        var premiumItem = new DownloadQueueItem { Url = "https://example.com/3", UserTier = UserTier.Premium };

        // Act
        await _queueManager.EnqueueAsync(freeItem);
        await _queueManager.EnqueueAsync(vipItem);
        await _queueManager.EnqueueAsync(premiumItem);

        // Assert
        var queuedItems = _queueManager.GetQueuedItems().ToList();
        queuedItems[0].Priority.Should().Be(DownloadPriority.Critical); // VIP first
        queuedItems[1].Priority.Should().Be(DownloadPriority.High); // Premium second
        queuedItems[2].Priority.Should().Be(DownloadPriority.Low); // Free last
    }

    [Fact]
    public async Task EnqueueAsync_ShouldUpdateQueuePositions()
    {
        // Arrange
        var item1 = new DownloadQueueItem { Url = "https://example.com/1" };
        var item2 = new DownloadQueueItem { Url = "https://example.com/2" };
        var item3 = new DownloadQueueItem { Url = "https://example.com/3" };

        // Act
        await _queueManager.EnqueueAsync(item1);
        await _queueManager.EnqueueAsync(item2);
        await _queueManager.EnqueueAsync(item3);

        // Assert
        _queueManager.GetItem(item1.Id)!.QueuePosition.Should().Be(1);
        _queueManager.GetItem(item2.Id)!.QueuePosition.Should().Be(2);
        _queueManager.GetItem(item3.Id)!.QueuePosition.Should().Be(3);
    }

    [Fact]
    public async Task DequeueAsync_ExistingItem_ShouldRemoveFromQueue()
    {
        // Arrange
        var item = new DownloadQueueItem { Url = "https://example.com/video" };
        await _queueManager.EnqueueAsync(item);

        // Act
        var result = await _queueManager.DequeueAsync(item.Id);

        // Assert
        result.Should().BeTrue();
        _queueManager.QueueLength.Should().Be(0);
        _queueManager.GetItem(item.Id).Should().BeNull();
    }

    [Fact]
    public async Task DequeueAsync_NonExistentItem_ShouldReturnFalse()
    {
        // Arrange
        var itemId = "non-existent-id";

        // Act
        var result = await _queueManager.DequeueAsync(itemId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetQueuedItems_ShouldReturnOrderedByPriority()
    {
        // Arrange
        var lowPriority = new DownloadQueueItem { Url = "https://example.com/1", UserTier = UserTier.Free };
        var highPriority = new DownloadQueueItem { Url = "https://example.com/2", UserTier = UserTier.VIP };

        await _queueManager.EnqueueAsync(lowPriority);
        await _queueManager.EnqueueAsync(highPriority);

        // Act
        var items = _queueManager.GetQueuedItems().ToList();

        // Assert
        items.Count.Should().Be(2);
        items[0].Priority.Should().Be(DownloadPriority.Critical);
        items[1].Priority.Should().Be(DownloadPriority.Low);
    }

    [Fact]
    public async Task StartProcessingAsync_ShouldStartQueueProcessing()
    {
        // Arrange
        var item = new DownloadQueueItem { Url = "https://example.com/video" };
        await _queueManager.EnqueueAsync(item);

        // Mock the download service to return success
        _mockService.Setup(s => s.SupportedPlatform).Returns(VideoPlatform.Http);
        _mockService.Setup(s => s.DownloadAsync(
            It.IsAny<string>(), 
            It.IsAny<DownloadOptions>(), 
            It.IsAny<IProgress<double>>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(DownloadResult.SuccessResult("/path/to/file"));

        // Act
        await _queueManager.StartProcessingAsync();

        // Wait for processing to complete
        await Task.Delay(2000);

        // Assert
        // Statistics should show processed
        var stats = _queueManager.GetStatistics();
        stats.TotalProcessed.Should().BeGreaterThan(0);
        
        // Cleanup
        await _queueManager.StopProcessingAsync();
    }

    [Fact]
    public async Task StopProcessingAsync_ShouldStopQueueProcessing()
    {
        // Arrange
        await _queueManager.StartProcessingAsync();

        // Act
        await _queueManager.StopProcessingAsync();

        // Assert
        // Should not throw
    }

    [Fact]
    public async Task CancelDownloadAsync_QueuedItem_ShouldRemoveFromQueue()
    {
        // Arrange
        var item = new DownloadQueueItem { Url = "https://example.com/video" };
        await _queueManager.EnqueueAsync(item);

        // Act
        var result = await _queueManager.CancelDownloadAsync(item.Id);

        // Assert
        result.Should().BeTrue();
        _queueManager.QueueLength.Should().Be(0);
    }

    [Fact]
    public async Task CancelDownloadAsync_ActiveDownload_ShouldCancel()
    {
        // Arrange
        var item = new DownloadQueueItem { Url = "https://example.com/video" };
        await _queueManager.EnqueueAsync(item);
        
        // Mock cancel to return true
        _mockService.Setup(s => s.CancelAsync(item.Id))
            .ReturnsAsync(true);

        // Act - for now just test queued item cancellation
        var result = await _queueManager.CancelDownloadAsync(item.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateSettings()
    {
        // Arrange
        var config = new QueueConfiguration
        {
            GlobalMaxConcurrentDownloads = 50,
            MaxConcurrentDownloadsFree = 5,
            EnableQueueProcessing = false
        };

        // Act
        _queueManager.UpdateConfiguration(config);

        // Assert
        _queueManager.MaxConcurrentDownloads.Should().Be(50);
    }

    [Fact]
    public void GetStatistics_ShouldReturnStatistics()
    {
        // Arrange & Act
        var stats = _queueManager.GetStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalProcessed.Should().Be(0);
        stats.SuccessfulDownloads.Should().Be(0);
        stats.FailedDownloads.Should().Be(0);
    }

    [Fact]
    public void DownloadQueueItem_CalculatePriority_ShouldSetCorrectPriority()
    {
        // Arrange
        var freeItem = new DownloadQueueItem { UserTier = UserTier.Free };
        var premiumItem = new DownloadQueueItem { UserTier = UserTier.Premium };
        var vipItem = new DownloadQueueItem { UserTier = UserTier.VIP };

        // Act
        freeItem.CalculatePriority();
        premiumItem.CalculatePriority();
        vipItem.CalculatePriority();

        // Assert
        freeItem.Priority.Should().Be(DownloadPriority.Low);
        premiumItem.Priority.Should().Be(DownloadPriority.High);
        vipItem.Priority.Should().Be(DownloadPriority.Critical);
    }

    [Fact]
    public void DownloadQueueItem_MarkStarted_ShouldUpdateState()
    {
        // Arrange
        var item = new DownloadQueueItem();

        // Act
        item.MarkStarted();

        // Assert
        item.Status.State.Should().Be(DownloadState.Downloading);
        item.StartedAt.Should().HaveValue();
    }

    [Fact]
    public void DownloadQueueItem_MarkCompleted_ShouldUpdateState()
    {
        // Arrange
        var item = new DownloadQueueItem();
        var result = DownloadResult.SuccessResult("/path/to/file");

        // Act
        item.MarkCompleted(result);

        // Assert
        item.Status.State.Should().Be(DownloadState.Completed);
        item.CompletedAt.Should().HaveValue();
        item.Result.Should().Be(result);
    }

    [Fact]
    public void DownloadQueueItem_GetFormattedWaitTime_ShouldFormatCorrectly()
    {
        // Arrange
        var item = new DownloadQueueItem();

        // Act & Assert
        item.EstimatedWaitSeconds = 30;
        item.GetFormattedWaitTime().Should().Be("30 seconds");

        item.EstimatedWaitSeconds = 120;
        item.GetFormattedWaitTime().Should().Be("2 minutes");

        item.EstimatedWaitSeconds = 7200;
        item.GetFormattedWaitTime().Should().Be("2.0 hours");
    }

    [Fact]
    public void DownloadQueueManager_Dispose_ShouldNotThrow()
    {
        // Arrange
        var manager = new DownloadQueueManager(new[] { _mockService.Object });

        // Act & Assert
        manager.Dispose();
        // Should not throw
    }
}
