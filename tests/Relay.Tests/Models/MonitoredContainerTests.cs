using FluentAssertions;
using Relay.Models;
using Xunit;

namespace Relay.Tests.Models;

public class MonitoredContainerTests
{
    #region ImageTag Tests

    [Theory]
    [InlineData("nginx:latest", "latest")]
    [InlineData("nginx:1.25.0", "1.25.0")]
    [InlineData("nginx:alpine", "alpine")]
    [InlineData("myuser/myapp:v1.0.0", "v1.0.0")]
    public void ImageTag_StandardImageName_ExtractsTag(string imageName, string expectedTag)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageTag.Should().Be(expectedTag);
    }

    [Theory]
    [InlineData("nginx", "latest")]
    [InlineData("myuser/myapp", "latest")]
    public void ImageTag_NoTagSpecified_ReturnsLatest(string imageName, string expectedTag)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageTag.Should().Be(expectedTag);
    }

    [Theory]
    [InlineData("registry.example.com:5000/myapp:v1.0.0", "v1.0.0")]
    [InlineData("localhost:5000/myapp:latest", "latest")]
    [InlineData("gcr.io/project/image:1.0.0", "1.0.0")]
    public void ImageTag_RegistryWithPort_ExtractsTagCorrectly(string imageName, string expectedTag)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageTag.Should().Be(expectedTag);
    }

    [Theory]
    [InlineData("registry.example.com:5000/myapp", "latest")]
    [InlineData("localhost:5000/myapp", "latest")]
    public void ImageTag_RegistryWithPortNoTag_ReturnsLatest(string imageName, string expectedTag)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageTag.Should().Be(expectedTag);
    }

    #endregion

    #region ImageRepository Tests

    [Theory]
    [InlineData("nginx:latest", "nginx")]
    [InlineData("nginx:1.25.0", "nginx")]
    [InlineData("myuser/myapp:v1.0.0", "myuser/myapp")]
    [InlineData("nginx", "nginx")]
    public void ImageRepository_StandardImageName_ExtractsRepository(string imageName, string expectedRepo)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageRepository.Should().Be(expectedRepo);
    }

    [Theory]
    [InlineData("registry.example.com:5000/myapp:v1.0.0", "registry.example.com:5000/myapp")]
    [InlineData("localhost:5000/myapp:latest", "localhost:5000/myapp")]
    [InlineData("gcr.io/project/image:1.0.0", "gcr.io/project/image")]
    public void ImageRepository_RegistryWithPort_ExtractsRepositoryCorrectly(string imageName, string expectedRepo)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageRepository.Should().Be(expectedRepo);
    }

    [Theory]
    [InlineData("registry.example.com:5000/myapp", "registry.example.com:5000/myapp")]
    [InlineData("localhost:5000/myapp", "localhost:5000/myapp")]
    public void ImageRepository_RegistryWithPortNoTag_ReturnsFullName(string imageName, string expectedRepo)
    {
        // Arrange
        var container = CreateContainer(imageName);

        // Act & Assert
        container.ImageRepository.Should().Be(expectedRepo);
    }

    #endregion

    #region UpdateStrategy Tests

    [Fact]
    public void UpdateStrategy_NoLabels_ReturnsDigest()
    {
        // Arrange
        var container = CreateContainer("nginx:latest");

        // Act & Assert
        container.UpdateStrategy.Should().Be(UpdateStrategy.Digest);
    }

    [Fact]
    public void UpdateStrategy_EmptyLabels_ReturnsDigest()
    {
        // Arrange
        var container = CreateContainer("nginx:latest", new Dictionary<string, string>());

        // Act & Assert
        container.UpdateStrategy.Should().Be(UpdateStrategy.Digest);
    }

    [Theory]
    [InlineData("patch", UpdateStrategy.Patch)]
    [InlineData("minor", UpdateStrategy.Minor)]
    [InlineData("major", UpdateStrategy.Major)]
    [InlineData("digest", UpdateStrategy.Digest)]
    public void UpdateStrategy_WithLabel_ReturnsCorrectStrategy(string labelValue, UpdateStrategy expected)
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["relay.update"] = labelValue
        };
        var container = CreateContainer("nginx:latest", labels);

        // Act & Assert
        container.UpdateStrategy.Should().Be(expected);
    }

    #endregion

    #region Required Properties Tests

    [Fact]
    public void MonitoredContainer_RequiredPropertiesSet_CreatesSuccessfully()
    {
        // Arrange & Act
        var container = new MonitoredContainer
        {
            Id = "abc123",
            Name = "test-container",
            ImageName = "nginx:latest",
            ImageId = "sha256:abc123",
            State = "running"
        };

        // Assert
        container.Id.Should().Be("abc123");
        container.Name.Should().Be("test-container");
        container.ImageName.Should().Be("nginx:latest");
        container.ImageId.Should().Be("sha256:abc123");
        container.State.Should().Be("running");
    }

    [Fact]
    public void MonitoredContainer_Labels_DefaultsToEmptyDictionary()
    {
        // Arrange
        var container = new MonitoredContainer
        {
            Id = "abc123",
            Name = "test-container",
            ImageName = "nginx:latest",
            ImageId = "sha256:abc123",
            State = "running"
        };

        // Assert
        container.Labels.Should().NotBeNull();
        container.Labels.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private static MonitoredContainer CreateContainer(string imageName, IDictionary<string, string>? labels = null)
    {
        return new MonitoredContainer
        {
            Id = "container-id",
            Name = "test-container",
            ImageName = imageName,
            ImageId = "sha256:abc123def456",
            State = "running",
            Labels = labels ?? new Dictionary<string, string>()
        };
    }

    #endregion
}
