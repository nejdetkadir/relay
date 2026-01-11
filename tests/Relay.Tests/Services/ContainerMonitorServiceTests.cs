using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Relay.Models;
using Relay.Services;
using Xunit;

namespace Relay.Tests.Services;

public class ContainerMonitorServiceTests
{
    private readonly Mock<IDockerService> _dockerServiceMock;
    private readonly Mock<IImageCheckerService> _imageCheckerMock;
    private readonly Mock<IContainerUpdaterService> _containerUpdaterMock;
    private readonly Mock<ILogger<ContainerMonitorService>> _loggerMock;
    private readonly ContainerMonitorService _sut;

    public ContainerMonitorServiceTests()
    {
        _dockerServiceMock = new Mock<IDockerService>();
        _imageCheckerMock = new Mock<IImageCheckerService>();
        _containerUpdaterMock = new Mock<IContainerUpdaterService>();
        _loggerMock = new Mock<ILogger<ContainerMonitorService>>();

        _sut = new ContainerMonitorService(
            _dockerServiceMock.Object,
            _imageCheckerMock.Object,
            _containerUpdaterMock.Object,
            _loggerMock.Object);
    }

    #region RunCheckCycleAsync Tests

    [Fact]
    public async Task RunCheckCycleAsync_NoContainers_ReturnsZeroCounts()
    {
        // Arrange
        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer>());

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(0);
        updated.Should().Be(0);
        failed.Should().Be(0);
    }

    [Fact]
    public async Task RunCheckCycleAsync_WithContainers_ChecksEachContainer()
    {
        // Arrange
        var containers = new List<MonitoredContainer>
        {
            CreateTestContainer("container-1"),
            CreateTestContainer("container-2"),
            CreateTestContainer("container-3")
        };

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);

        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(It.IsAny<MonitoredContainer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoredContainer c, CancellationToken _) => 
                ImageUpdateResult.NoUpdate(c, c.ImageId));

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(3);
        _imageCheckerMock.Verify(x => x.CheckForUpdateAsync(It.IsAny<MonitoredContainer>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task RunCheckCycleAsync_NoUpdatesAvailable_ReturnsZeroUpdated()
    {
        // Arrange
        var container = CreateTestContainer("test-container");
        
        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer> { container });

        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.NoUpdate(container, container.ImageId));

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(1);
        updated.Should().Be(0);
        failed.Should().Be(0);
    }

    [Fact]
    public async Task RunCheckCycleAsync_UpdateAvailable_CallsUpdater()
    {
        // Arrange
        var container = CreateTestContainer("test-container");
        var newImageName = "nginx:1.26.0";
        var newImageId = "sha256:newimageid";

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer> { container });

        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.UpdateFound(container, container.ImageId, newImageId, newImageName));

        _containerUpdaterMock.Setup(x => x.UpdateContainerAsync(
            container, newImageName, newImageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(1);
        updated.Should().Be(1);
        failed.Should().Be(0);
        
        _containerUpdaterMock.Verify(x => x.UpdateContainerAsync(
            container, newImageName, newImageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunCheckCycleAsync_UpdateFails_IncrementsFailedCount()
    {
        // Arrange
        var container = CreateTestContainer("test-container");
        var newImageName = "nginx:1.26.0";
        var newImageId = "sha256:newimageid";

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer> { container });

        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.UpdateFound(container, container.ImageId, newImageId, newImageName));

        _containerUpdaterMock.Setup(x => x.UpdateContainerAsync(
            container, newImageName, newImageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(1);
        updated.Should().Be(0);
        failed.Should().Be(1);
    }

    [Fact]
    public async Task RunCheckCycleAsync_CheckFails_IncrementsFailedCount()
    {
        // Arrange
        var container = CreateTestContainer("test-container");

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer> { container });

        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.Failed(container, container.ImageId, "Check failed"));

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(1);
        updated.Should().Be(0);
        failed.Should().Be(1);
        
        // Updater should NOT be called
        _containerUpdaterMock.Verify(x => x.UpdateContainerAsync(
            It.IsAny<MonitoredContainer>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task RunCheckCycleAsync_MixedResults_CountsCorrectly()
    {
        // Arrange
        var container1 = CreateTestContainer("container-1");
        var container2 = CreateTestContainer("container-2");
        var container3 = CreateTestContainer("container-3");

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer> { container1, container2, container3 });

        // Container 1: No update
        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.NoUpdate(container1, container1.ImageId));

        // Container 2: Update available and successful
        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.UpdateFound(container2, container2.ImageId, "sha256:new", "image:new"));
        
        _containerUpdaterMock.Setup(x => x.UpdateContainerAsync(
            container2, "image:new", "sha256:new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Container 3: Check failed
        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.Failed(container3, container3.ImageId, "Error"));

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(3);
        updated.Should().Be(1);
        failed.Should().Be(1);
    }

    [Fact]
    public async Task RunCheckCycleAsync_ProcessingThrows_ContinuesWithOtherContainers()
    {
        // Arrange
        var container1 = CreateTestContainer("container-1");
        var container2 = CreateTestContainer("container-2");

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MonitoredContainer> { container1, container2 });

        // Container 1: Throws exception
        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container1, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected error"));

        // Container 2: No update
        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(container2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ImageUpdateResult.NoUpdate(container2, container2.ImageId));

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(2);
        failed.Should().Be(1);
        
        // Both containers should be checked
        _imageCheckerMock.Verify(x => x.CheckForUpdateAsync(container2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunCheckCycleAsync_CancellationRequested_StopsProcessing()
    {
        // Arrange
        var containers = new List<MonitoredContainer>
        {
            CreateTestContainer("container-1"),
            CreateTestContainer("container-2"),
            CreateTestContainer("container-3")
        };

        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(containers);

        var cts = new CancellationTokenSource();
        var callCount = 0;

        _imageCheckerMock.Setup(x => x.CheckForUpdateAsync(It.IsAny<MonitoredContainer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MonitoredContainer c, CancellationToken _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    cts.Cancel(); // Cancel after first container
                }
                return ImageUpdateResult.NoUpdate(c, c.ImageId);
            });

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync(cts.Token);

        // Assert
        checked_.Should().BeLessOrEqualTo(2); // Should stop early
    }

    #endregion

    #region Docker Service Error Tests

    [Fact]
    public async Task RunCheckCycleAsync_GetContainersFails_ReturnsZeroCounts()
    {
        // Arrange
        _dockerServiceMock.Setup(x => x.GetMonitoredContainersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Docker API error"));

        // Act
        var (checked_, updated, failed) = await _sut.RunCheckCycleAsync();

        // Assert
        checked_.Should().Be(0);
        updated.Should().Be(0);
        failed.Should().Be(0);
    }

    #endregion

    #region Helper Methods

    private static MonitoredContainer CreateTestContainer(string name)
    {
        return new MonitoredContainer
        {
            Id = $"id-{name}",
            Name = name,
            ImageName = "nginx:latest",
            ImageId = $"sha256:image-{name}",
            State = "running",
            Labels = new Dictionary<string, string>()
        };
    }

    #endregion
}
