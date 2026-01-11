using Docker.DotNet.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Relay.Configuration;
using Relay.Models;
using Relay.Services;
using Xunit;

namespace Relay.Tests.Services;

public class ContainerUpdaterServiceTests
{
    private readonly Mock<IDockerService> _dockerServiceMock;
    private readonly Mock<ILogger<ContainerUpdaterService>> _loggerMock;
    private readonly RelayOptions _options;
    private readonly ContainerUpdaterService _sut;

    public ContainerUpdaterServiceTests()
    {
        _dockerServiceMock = new Mock<IDockerService>();
        _loggerMock = new Mock<ILogger<ContainerUpdaterService>>();
        _options = new RelayOptions
        {
            RollingUpdateEnabled = true,
            HealthCheckTimeoutSeconds = 60,
            HealthCheckIntervalSeconds = 5,
            CleanupOldImages = false
        };

        _sut = new ContainerUpdaterService(
            _dockerServiceMock.Object,
            Options.Create(_options),
            _loggerMock.Object);
    }

    #region Rolling Update Tests

    [Fact]
    public async Task UpdateContainerAsync_RollingUpdateEnabled_CreatesStagingContainer()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        var result = await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        result.Should().BeTrue();
        _dockerServiceMock.Verify(x => x.CreateStagingContainerAsync(
            "test-container-relay-staging",
            It.IsAny<Config>(),
            It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_RollingUpdateEnabled_WaitsForHealth()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.WaitForContainerHealthAsync(
            "staging-container-id",
            _options.HealthCheckTimeoutSeconds,
            _options.HealthCheckIntervalSeconds,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_StagingHealthy_StopsOldContainer()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.StopContainerAsync(container.Id, It.IsAny<CancellationToken>()), Times.Once);
        _dockerServiceMock.Verify(x => x.RemoveContainerAsync(container.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_StagingHealthy_RemovesStagingContainer()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.ForceRemoveContainerAsync(
            "staging-container-id", 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_StagingHealthy_CreatesFinalContainer()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.CreateAndStartContainerAsync(
            container.Name,
            It.Is<Config>(c => c.Image == "nginx:1.26.0"),
            It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_StagingUnhealthy_KeepsOldContainerRunning()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        
        _dockerServiceMock.Setup(x => x.CreateStagingContainerAsync(
            It.IsAny<string>(), It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("staging-container-id");

        // Health check fails
        _dockerServiceMock.Setup(x => x.WaitForContainerHealthAsync(
            "staging-container-id", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        result.Should().BeFalse();
        
        // Old container should NOT be stopped
        _dockerServiceMock.Verify(x => x.StopContainerAsync(container.Id, It.IsAny<CancellationToken>()), Times.Never);
        _dockerServiceMock.Verify(x => x.RemoveContainerAsync(container.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateContainerAsync_StagingUnhealthy_CleansStagingContainer()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        
        _dockerServiceMock.Setup(x => x.CreateStagingContainerAsync(
            It.IsAny<string>(), It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("staging-container-id");

        // Health check fails
        _dockerServiceMock.Setup(x => x.WaitForContainerHealthAsync(
            "staging-container-id", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.ForceRemoveContainerAsync(
            "staging-container-id", 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_WithCustomHealthCheckTimeout_UsesLabelValue()
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["relay.healthcheck.timeout"] = "120"
        };
        var container = CreateTestContainer(labels);
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.WaitForContainerHealthAsync(
            "staging-container-id",
            120, // Custom timeout from label
            _options.HealthCheckIntervalSeconds,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Legacy Update Tests

    [Fact]
    public async Task UpdateContainerAsync_RollingUpdateDisabled_UsesLegacyUpdate()
    {
        // Arrange
        _options.RollingUpdateEnabled = false;
        var sut = new ContainerUpdaterService(
            _dockerServiceMock.Object,
            Options.Create(_options),
            _loggerMock.Object);

        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        
        _dockerServiceMock.Setup(x => x.CreateAndStartContainerAsync(
            container.Name, It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-container-id");

        // Act
        var result = await sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        result.Should().BeTrue();
        
        // Should NOT create staging container
        _dockerServiceMock.Verify(x => x.CreateStagingContainerAsync(
            It.IsAny<string>(), It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()), Times.Never);
        
        // Should stop old container first (legacy behavior)
        _dockerServiceMock.Verify(x => x.StopContainerAsync(container.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_LegacyUpdate_StopsThenRemovesThenCreates()
    {
        // Arrange
        _options.RollingUpdateEnabled = false;
        var sut = new ContainerUpdaterService(
            _dockerServiceMock.Object,
            Options.Create(_options),
            _loggerMock.Object);

        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        var callOrder = new List<string>();

        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        
        _dockerServiceMock.Setup(x => x.StopContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("stop"));

        _dockerServiceMock.Setup(x => x.RemoveContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("remove"));

        _dockerServiceMock.Setup(x => x.CreateAndStartContainerAsync(
            container.Name, It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("create"))
            .ReturnsAsync("new-container-id");

        // Act
        await sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        callOrder.Should().ContainInOrder("stop", "remove", "create");
    }

    #endregion

    #region Cleanup Old Images Tests

    [Fact]
    public async Task UpdateContainerAsync_CleanupEnabled_RemovesOldImage()
    {
        // Arrange
        _options.CleanupOldImages = true;
        var sut = new ContainerUpdaterService(
            _dockerServiceMock.Object,
            Options.Create(_options),
            _loggerMock.Object);

        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.RemoveImageAsync(container.ImageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateContainerAsync_CleanupDisabled_DoesNotRemoveOldImage()
    {
        // Arrange
        _options.CleanupOldImages = false;
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        SetupSuccessfulRollingUpdate(container, inspection);

        // Act
        await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        _dockerServiceMock.Verify(x => x.RemoveImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task UpdateContainerAsync_InspectionThrows_ReturnsFalse()
    {
        // Arrange
        var container = CreateTestContainer();
        
        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Docker API error"));

        // Act
        var result = await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateContainerAsync_StagingCreationThrows_CleanupAndReturnsFalse()
    {
        // Arrange
        var container = CreateTestContainer();
        var inspection = CreateTestInspection();
        
        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        
        _dockerServiceMock.Setup(x => x.CreateStagingContainerAsync(
            It.IsAny<string>(), It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Failed to create staging container"));

        // Act
        var result = await _sut.UpdateContainerAsync(container, "nginx:1.26.0", "sha256:newimageid");

        // Assert
        result.Should().BeFalse();
        
        // Old container should NOT be touched
        _dockerServiceMock.Verify(x => x.StopContainerAsync(container.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private static MonitoredContainer CreateTestContainer(IDictionary<string, string>? labels = null)
    {
        return new MonitoredContainer
        {
            Id = "container-123",
            Name = "test-container",
            ImageName = "nginx:1.25.0",
            ImageId = "sha256:oldimage123",
            State = "running",
            Labels = labels ?? new Dictionary<string, string>()
        };
    }

    private static ContainerInspectResponse CreateTestInspection()
    {
        return new ContainerInspectResponse
        {
            ID = "container-123",
            Name = "/test-container",
            Config = new Config
            {
                Image = "nginx:1.25.0",
                Env = new List<string> { "VAR=value" },
                Labels = new Dictionary<string, string>()
            },
            HostConfig = new HostConfig
            {
                PortBindings = new Dictionary<string, IList<PortBinding>>
                {
                    ["80/tcp"] = new List<PortBinding>
                    {
                        new() { HostPort = "8080" }
                    }
                }
            },
            NetworkSettings = new NetworkSettings
            {
                Networks = new Dictionary<string, EndpointSettings>
                {
                    ["bridge"] = new EndpointSettings()
                }
            }
        };
    }

    private void SetupSuccessfulRollingUpdate(MonitoredContainer container, ContainerInspectResponse inspection)
    {
        _dockerServiceMock.Setup(x => x.InspectContainerAsync(container.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inspection);
        
        _dockerServiceMock.Setup(x => x.CreateStagingContainerAsync(
            It.IsAny<string>(), It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("staging-container-id");

        _dockerServiceMock.Setup(x => x.WaitForContainerHealthAsync(
            "staging-container-id", It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _dockerServiceMock.Setup(x => x.CreateAndStartContainerAsync(
            container.Name, It.IsAny<Config>(), It.IsAny<HostConfig>(),
            It.IsAny<NetworkingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-container-id");
    }

    #endregion
}
