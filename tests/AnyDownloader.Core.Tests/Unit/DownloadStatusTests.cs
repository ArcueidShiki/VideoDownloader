namespace AnyDownloader.Core.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Tests for DownloadStatus model - TDD approach
/// </summary>
public class DownloadStatusTests
{
    [Fact]
    public void DownloadStatus_DefaultConstructor_ShouldSetDefaults()
    {
        // Arrange & Act
        var status = new DownloadStatus();
        
        // Assert
        status.State.Should().Be(DownloadState.Pending);
        status.Progress.Should().Be(0);
        status.DownloadedBytes.Should().Be(0);
        status.TotalBytes.Should().Be(0);
        status.Speed.Should().Be(0);
    }

    [Fact]
    public void DownloadStatus_WithState_ShouldSetState()
    {
        // Arrange & Act
        var status = new DownloadStatus { State = DownloadState.Downloading };
        
        // Assert
        status.State.Should().Be(DownloadState.Downloading);
    }

    [Fact]
    public void DownloadStatus_WithProgress_ShouldSetProgress()
    {
        // Arrange
        var status = new DownloadStatus { Progress = 75.5 };
        
        // Assert
        status.Progress.Should().Be(75.5);
    }

    [Fact]
    public void DownloadStatus_WithBytes_ShouldSetBytes()
    {
        // Arrange
        var status = new DownloadStatus
        {
            DownloadedBytes = 5000000,
            TotalBytes = 10000000
        };
        
        // Assert
        status.DownloadedBytes.Should().Be(5000000);
        status.TotalBytes.Should().Be(10000000);
    }

    [Fact]
    public void DownloadStatus_CalculateProgress_ShouldBeCorrect()
    {
        // Arrange
        var status = new DownloadStatus
        {
            DownloadedBytes = 2500000,
            TotalBytes = 10000000
        };
        
        // Act
        status.CalculateProgress();
        
        // Assert
        status.Progress.Should().Be(25.0);
    }

    [Fact]
    public void DownloadStatus_WithSpeed_ShouldSetSpeed()
    {
        // Arrange
        var status = new DownloadStatus { Speed = 1024 * 1024 }; // 1 MB/s
        
        // Assert
        status.Speed.Should().Be(1024 * 1024);
    }

    [Fact]
    public void DownloadStatus_CalculateETA_WithSpeed_ShouldReturnETA()
    {
        // Arrange
        var status = new DownloadStatus
        {
            DownloadedBytes = 5000000,
            TotalBytes = 10000000,
            Speed = 1000000 // 1 MB/s
        };
        
        // Act
        var eta = status.CalculateETA();
        
        // Assert
        eta.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DownloadStatus_CalculateETA_NoSpeed_ShouldReturnNull()
    {
        // Arrange
        var status = new DownloadStatus
        {
            DownloadedBytes = 5000000,
            TotalBytes = 10000000,
            Speed = 0
        };
        
        // Act
        var eta = status.CalculateETA();
        
        // Assert
        eta.Should().BeNull();
    }

    [Theory]
    [InlineData(DownloadState.Pending, false)]
    [InlineData(DownloadState.Downloading, true)]
    [InlineData(DownloadState.Paused, false)]
    [InlineData(DownloadState.Completed, false)]
    [InlineData(DownloadState.Failed, false)]
    public void DownloadStatus_IsActive_ShouldReturnCorrectly(DownloadState state, bool expected)
    {
        // Arrange
        var status = new DownloadStatus { State = state };
        
        // Act
        var isActive = status.IsActive();
        
        // Assert
        isActive.Should().Be(expected);
    }

    [Theory]
    [InlineData(DownloadState.Pending, "Pending")]
    [InlineData(DownloadState.Downloading, "Downloading")]
    [InlineData(DownloadState.Paused, "Paused")]
    [InlineData(DownloadState.Completed, "Completed")]
    [InlineData(DownloadState.Failed, "Failed")]
    public void DownloadStatus_ToString_ShouldIncludeState(DownloadState state, string expected)
    {
        // Arrange
        var status = new DownloadStatus { State = state };
        
        // Act
        var result = status.ToString();
        
        // Assert
        result.Should().Contain(expected);
    }
}
