using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Relay.Models;
using Relay.Services;
using Xunit;

namespace Relay.Tests.Services;

public class ImageCheckerServiceTests
{
    private readonly Mock<IDockerService> _dockerServiceMock;
    private readonly Mock<IRegistryService> _registryServiceMock;
    private readonly Mock<IVersionService> _versionServiceMock;
    private readonly Mock<ILogger<ImageCheckerService>> _loggerMock;
    private readonly ImageCheckerService _sut;

    public ImageCheckerServiceTests()
    {
        _dockerServiceMock = new Mock<IDockerService>();
        _registryServiceMock = new Mock<IRegistryService>();
        _versionServiceMock = new Mock<IVersionService>();
        _loggerMock = new Mock<ILogger<ImageCheckerService>>();

        _sut = new ImageCheckerService(
            _dockerServiceMock.Object,
            _registryServiceMock.Object,
            _versionServiceMock.Object,
            _loggerMock.Object);
    }

    #region Digest Strategy Tests

    [Fact]
    public async Task CheckForUpdateAsync_DigestStrategy_PullsImage()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Digest);
        
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(container.ImageId); // Same image ID

        // Act
        await _sut.CheckForUpdateAsync(container);

        // Assert
        _dockerServiceMock.Verify(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DigestStrategy_SameImageId_ReturnsNoUpdate()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Digest);
        
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(container.ImageId); // Same image ID

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.Success.Should().BeTrue();
        result.UpdateAvailable.Should().BeFalse();
        result.LatestImageId.Should().Be(container.ImageId);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DigestStrategy_DifferentImageId_ReturnsUpdateAvailable()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Digest);
        var newImageId = "sha256:newimageid123";
        
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newImageId);

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.Success.Should().BeTrue();
        result.UpdateAvailable.Should().BeTrue();
        result.LatestImageId.Should().Be(newImageId);
        result.NewImageName.Should().Be(container.ImageName); // Same tag
    }

    [Fact]
    public async Task CheckForUpdateAsync_DigestStrategy_PullFails_ReturnsFailure()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Digest);
        
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Pull failed"));

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to pull image");
    }

    #endregion

    #region Version Strategy Tests

    [Theory]
    [InlineData(UpdateStrategy.Patch)]
    [InlineData(UpdateStrategy.Minor)]
    [InlineData(UpdateStrategy.Major)]
    public async Task CheckForUpdateAsync_VersionStrategy_QueriesRegistryForTags(UpdateStrategy strategy)
    {
        // Arrange
        var container = CreateTestContainer(strategy, "nginx:1.25.0");
        
        _registryServiceMock.Setup(x => x.GetTagsAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "1.25.0", "1.25.1", "1.26.0" });

        _versionServiceMock.Setup(x => x.FindNewestVersion(
            container.ImageTag, It.IsAny<IEnumerable<string>>(), strategy))
            .Returns((string?)null);

        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(container.ImageId);

        // Act
        await _sut.CheckForUpdateAsync(container);

        // Assert
        _registryServiceMock.Verify(x => x.GetTagsAsync(container.ImageName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckForUpdateAsync_VersionStrategy_NewerVersionFound_ReturnsUpdate()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Minor, "nginx:1.25.0");
        var newerTag = "1.26.0";
        var newImageId = "sha256:newversion";
        
        _registryServiceMock.Setup(x => x.GetTagsAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "1.25.0", "1.25.1", "1.26.0" });

        _versionServiceMock.Setup(x => x.FindNewestVersion(
            container.ImageTag, It.IsAny<IEnumerable<string>>(), UpdateStrategy.Minor))
            .Returns(newerTag);

        _dockerServiceMock.Setup(x => x.PullImageAsync($"nginx:{newerTag}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newImageId);

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.Success.Should().BeTrue();
        result.UpdateAvailable.Should().BeTrue();
        result.NewImageName.Should().Be($"nginx:{newerTag}");
        result.LatestImageId.Should().Be(newImageId);
    }

    [Fact]
    public async Task CheckForUpdateAsync_VersionStrategy_NoNewerVersion_FallsBackToDigestCheck()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Patch, "nginx:1.25.0");
        
        _registryServiceMock.Setup(x => x.GetTagsAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "1.25.0" });

        _versionServiceMock.Setup(x => x.FindNewestVersion(
            container.ImageTag, It.IsAny<IEnumerable<string>>(), UpdateStrategy.Patch))
            .Returns((string?)null);

        // Fallback to digest check - same image
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(container.ImageId);

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.UpdateAvailable.Should().BeFalse();
        _dockerServiceMock.Verify(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckForUpdateAsync_VersionStrategy_NoTagsFound_FallsBackToDigestCheck()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Minor, "nginx:1.25.0");
        
        _registryServiceMock.Setup(x => x.GetTagsAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>()); // No tags found

        // Fallback to digest check
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(container.ImageId);

        // Act
        await _sut.CheckForUpdateAsync(container);

        // Assert
        _dockerServiceMock.Verify(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckForUpdateAsync_VersionStrategy_PullNewVersionFails_ReturnsFailure()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Minor, "nginx:1.25.0");
        var newerTag = "1.26.0";
        
        _registryServiceMock.Setup(x => x.GetTagsAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "1.25.0", "1.26.0" });

        _versionServiceMock.Setup(x => x.FindNewestVersion(
            container.ImageTag, It.IsAny<IEnumerable<string>>(), UpdateStrategy.Minor))
            .Returns(newerTag);

        _dockerServiceMock.Setup(x => x.PullImageAsync($"nginx:{newerTag}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Pull failed"));

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to pull image");
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task CheckForUpdateAsync_UnexpectedException_ReturnsFailure()
    {
        // Arrange
        var container = CreateTestContainer(UpdateStrategy.Digest);
        
        _dockerServiceMock.Setup(x => x.PullImageAsync(container.ImageName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        // Act
        var result = await _sut.CheckForUpdateAsync(container);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Helper Methods

    private static MonitoredContainer CreateTestContainer(UpdateStrategy strategy, string imageName = "nginx:latest")
    {
        var labels = new Dictionary<string, string>();
        
        if (strategy != UpdateStrategy.Digest)
        {
            labels["relay.update"] = strategy.ToString().ToLowerInvariant();
        }

        return new MonitoredContainer
        {
            Id = "container-123",
            Name = "test-container",
            ImageName = imageName,
            ImageId = "sha256:oldimage123",
            State = "running",
            Labels = labels
        };
    }

    #endregion
}
