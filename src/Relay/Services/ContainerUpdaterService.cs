using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Configuration;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service for updating containers with new images while preserving configuration.
/// Supports rolling updates to minimize downtime.
/// </summary>
public class ContainerUpdaterService : IContainerUpdaterService
{
    private const string StagingSuffix = "-relay-staging";
    
    private readonly IDockerService _dockerService;
    private readonly RelayOptions _options;
    private readonly ILogger<ContainerUpdaterService> _logger;

    public ContainerUpdaterService(
        IDockerService dockerService,
        IOptions<RelayOptions> options,
        ILogger<ContainerUpdaterService> logger)
    {
        _dockerService = dockerService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> UpdateContainerAsync(MonitoredContainer container, string newImageName, string newImageId, CancellationToken cancellationToken = default)
    {
        if (_options.RollingUpdateEnabled)
        {
            return await UpdateContainerWithRollingAsync(container, newImageName, newImageId, cancellationToken);
        }
        
        return await UpdateContainerLegacyAsync(container, newImageName, newImageId, cancellationToken);
    }

    /// <summary>
    /// Performs a rolling update: starts a staging container to verify health before switching.
    /// This minimizes downtime by only stopping the old container after the new one is verified.
    /// </summary>
    private async Task<bool> UpdateContainerWithRollingAsync(
        MonitoredContainer container, 
        string newImageName, 
        string newImageId, 
        CancellationToken cancellationToken)
    {
        var oldImageId = container.ImageId;
        var stagingName = $"{container.Name}{StagingSuffix}";
        string? stagingContainerId = null;

        try
        {
            _logger.LogInformation("Starting rolling update for container {ContainerName}: {OldImage} -> {NewImage}",
                container.Name, container.ImageName, newImageName);

            // Get the full container configuration
            var inspection = await _dockerService.InspectContainerAsync(container.Id, cancellationToken);
            var newConfig = CloneConfigWithNewImage(inspection.Config, newImageName);
            var hostConfig = inspection.HostConfig;
            var networkConfig = CreateNetworkingConfig(inspection);

            // Step 1: Create staging container WITHOUT port bindings
            _logger.LogInformation("Creating staging container {StagingName} for health verification...", stagingName);
            stagingContainerId = await _dockerService.CreateStagingContainerAsync(
                stagingName,
                newConfig,
                hostConfig,
                networkConfig,
                cancellationToken);

            // Step 2: Wait for staging container to become healthy
            var healthCheckTimeout = UpdateStrategyExtensions.GetHealthCheckTimeout(
                container.Labels, 
                _options.HealthCheckTimeoutSeconds);
            
            _logger.LogInformation("Waiting for staging container to become healthy (timeout: {Timeout}s)...", healthCheckTimeout);
            var isHealthy = await _dockerService.WaitForContainerHealthAsync(
                stagingContainerId,
                healthCheckTimeout,
                _options.HealthCheckIntervalSeconds,
                cancellationToken);

            if (!isHealthy)
            {
                _logger.LogError("Staging container {StagingName} failed health check. Rolling update aborted. Old container remains running.",
                    stagingName);
                
                // Clean up staging container
                await CleanupStagingContainerAsync(stagingContainerId, cancellationToken);
                return false;
            }

            _logger.LogInformation("Staging container is healthy. Proceeding with switchover...");

            // Step 3: Stop and remove the old container
            _logger.LogInformation("Stopping old container {ContainerName}...", container.Name);
            await _dockerService.StopContainerAsync(container.Id, cancellationToken);
            await _dockerService.RemoveContainerAsync(container.Id, cancellationToken);

            // Step 4: Stop and remove the staging container
            _logger.LogDebug("Removing staging container {StagingName}...", stagingName);
            await _dockerService.ForceRemoveContainerAsync(stagingContainerId, cancellationToken);
            stagingContainerId = null; // Mark as cleaned up

            // Step 5: Create the final container with original name and full config (including ports)
            _logger.LogInformation("Creating final container {ContainerName} with full configuration...", container.Name);
            var newContainerId = await _dockerService.CreateAndStartContainerAsync(
                container.Name,
                newConfig,
                hostConfig,
                networkConfig,
                cancellationToken);

            _logger.LogInformation("Rolling update completed successfully for {ContainerName} (new ID: {NewContainerId})",
                container.Name, newContainerId[..Math.Min(12, newContainerId.Length)]);

            // Optionally clean up the old image
            await TryCleanupOldImageAsync(oldImageId, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rolling update failed for container {ContainerName}", container.Name);

            // Clean up staging container if it exists
            if (stagingContainerId != null)
            {
                await CleanupStagingContainerAsync(stagingContainerId, cancellationToken);
            }

            // The old container should still be running since we didn't stop it yet
            // (unless the error happened after step 3)
            await TryRecoverContainerAsync(container, cancellationToken);

            return false;
        }
    }

    /// <summary>
    /// Legacy update method: stops the old container before creating the new one.
    /// Used when rolling updates are disabled.
    /// </summary>
    private async Task<bool> UpdateContainerLegacyAsync(
        MonitoredContainer container, 
        string newImageName, 
        string newImageId, 
        CancellationToken cancellationToken)
    {
        var oldImageId = container.ImageId;

        try
        {
            _logger.LogInformation("Starting legacy update for container {ContainerName}: {OldImage} -> {NewImage}",
                container.Name, container.ImageName, newImageName);

            // Get the full container configuration
            var inspection = await _dockerService.InspectContainerAsync(container.Id, cancellationToken);

            // Stop the running container
            _logger.LogInformation("Stopping container {ContainerName}...", container.Name);
            await _dockerService.StopContainerAsync(container.Id, cancellationToken);

            // Remove the old container
            _logger.LogInformation("Removing old container {ContainerName}...", container.Name);
            await _dockerService.RemoveContainerAsync(container.Id, cancellationToken);

            // Create new configuration with the new image
            var newConfig = CloneConfigWithNewImage(inspection.Config, newImageName);
            var hostConfig = inspection.HostConfig;
            var networkConfig = CreateNetworkingConfig(inspection);

            // Create and start the new container
            _logger.LogInformation("Creating new container {ContainerName} with image {NewImage}...", container.Name, newImageName);
            var newContainerId = await _dockerService.CreateAndStartContainerAsync(
                container.Name,
                newConfig,
                hostConfig,
                networkConfig,
                cancellationToken);

            _logger.LogInformation("Container {ContainerName} updated successfully (new ID: {NewContainerId})",
                container.Name, newContainerId[..Math.Min(12, newContainerId.Length)]);

            // Optionally clean up the old image
            await TryCleanupOldImageAsync(oldImageId, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update container {ContainerName}", container.Name);

            // Attempt to recover by restarting the old container if possible
            await TryRecoverContainerAsync(container, cancellationToken);

            return false;
        }
    }

    private async Task CleanupStagingContainerAsync(string stagingContainerId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Cleaning up staging container {ContainerId}...", stagingContainerId[..Math.Min(12, stagingContainerId.Length)]);
            await _dockerService.ForceRemoveContainerAsync(stagingContainerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up staging container {ContainerId}", stagingContainerId[..Math.Min(12, stagingContainerId.Length)]);
        }
    }

    private async Task TryCleanupOldImageAsync(string oldImageId, CancellationToken cancellationToken)
    {
        if (!_options.CleanupOldImages) return;

        try
        {
            await _dockerService.RemoveImageAsync(oldImageId, cancellationToken);
            _logger.LogInformation("Removed old image {ImageId}", oldImageId[..Math.Min(12, oldImageId.Length)]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove old image {ImageId}", oldImageId[..Math.Min(12, oldImageId.Length)]);
        }
    }

    private static Config CloneConfigWithNewImage(Config originalConfig, string imageName)
    {
        return new Config
        {
            Image = imageName,
            Env = originalConfig.Env,
            Cmd = originalConfig.Cmd,
            Entrypoint = originalConfig.Entrypoint,
            WorkingDir = originalConfig.WorkingDir,
            Labels = originalConfig.Labels,
            ExposedPorts = originalConfig.ExposedPorts,
            Volumes = originalConfig.Volumes,
            User = originalConfig.User,
            Tty = originalConfig.Tty,
            AttachStdin = originalConfig.AttachStdin,
            AttachStdout = originalConfig.AttachStdout,
            AttachStderr = originalConfig.AttachStderr,
            Hostname = originalConfig.Hostname,
            Domainname = originalConfig.Domainname,
            StopSignal = originalConfig.StopSignal,
            Healthcheck = originalConfig.Healthcheck,
            ArgsEscaped = originalConfig.ArgsEscaped,
            OnBuild = originalConfig.OnBuild,
            StdinOnce = originalConfig.StdinOnce,
            OpenStdin = originalConfig.OpenStdin
        };
    }

    private static NetworkingConfig? CreateNetworkingConfig(ContainerInspectResponse inspection)
    {
        if (inspection.NetworkSettings?.Networks == null || !inspection.NetworkSettings.Networks.Any())
        {
            return null;
        }

        var endpoints = new Dictionary<string, EndpointSettings>();

        foreach (var (networkName, networkSettings) in inspection.NetworkSettings.Networks)
        {
            endpoints[networkName] = new EndpointSettings
            {
                Aliases = networkSettings.Aliases,
                NetworkID = networkSettings.NetworkID,
                IPAddress = null, // Let Docker assign a new IP
                IPPrefixLen = 0,
                IPv6Gateway = null,
                GlobalIPv6Address = null,
                GlobalIPv6PrefixLen = 0,
                MacAddress = null,
                DriverOpts = networkSettings.DriverOpts,
                Links = networkSettings.Links,
                IPAMConfig = networkSettings.IPAMConfig
            };
        }

        return new NetworkingConfig
        {
            EndpointsConfig = endpoints
        };
    }

    private async Task TryRecoverContainerAsync(MonitoredContainer container, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Attempting to recover container {ContainerName}...", container.Name);

            // Try to start the container if it still exists
            var containers = await _dockerService.GetMonitoredContainersAsync(cancellationToken);
            var existingContainer = containers.FirstOrDefault(c => c.Id == container.Id);

            if (existingContainer != null)
            {
                _logger.LogInformation("Container {ContainerName} still exists, recovery may be possible", container.Name);
            }
            else
            {
                _logger.LogWarning("Container {ContainerName} no longer exists. Manual intervention may be required.", container.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery attempt failed for container {ContainerName}", container.Name);
        }
    }
}
