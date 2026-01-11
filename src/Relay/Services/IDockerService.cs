using Docker.DotNet.Models;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service interface for Docker API operations.
/// </summary>
public interface IDockerService
{
    /// <summary>
    /// Gets all containers with the relay monitoring label enabled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of monitored containers.</returns>
    Task<IReadOnlyList<MonitoredContainer>> GetMonitoredContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full inspection details of a container.
    /// </summary>
    /// <param name="containerId">The container ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Container inspection response.</returns>
    Task<ContainerInspectResponse> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pulls the latest version of an image from the registry.
    /// </summary>
    /// <param name="imageName">The full image name including tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the pulled image.</returns>
    Task<string> PullImageAsync(string imageName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the ID (digest) of a local image.
    /// </summary>
    /// <param name="imageName">The full image name including tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image ID, or null if not found.</returns>
    Task<string?> GetImageIdAsync(string imageName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops a running container.
    /// </summary>
    /// <param name="containerId">The container ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a container.
    /// </summary>
    /// <param name="containerId">The container ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and starts a new container based on the provided configuration.
    /// </summary>
    /// <param name="name">The container name.</param>
    /// <param name="config">The container configuration.</param>
    /// <param name="hostConfig">The host configuration.</param>
    /// <param name="networkingConfig">The networking configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the created container.</returns>
    Task<string> CreateAndStartContainerAsync(
        string name,
        Config config,
        HostConfig hostConfig,
        NetworkingConfig? networkingConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an image from the local Docker host.
    /// </summary>
    /// <param name="imageId">The image ID to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveImageAsync(string imageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates and starts a staging container without port bindings for health verification.
    /// Used during rolling updates to test the new image before switching over.
    /// </summary>
    /// <param name="stagingName">The temporary name for the staging container.</param>
    /// <param name="config">The container configuration.</param>
    /// <param name="hostConfig">The host configuration (port bindings will be stripped).</param>
    /// <param name="networkingConfig">The networking configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the created staging container.</returns>
    Task<string> CreateStagingContainerAsync(
        string stagingName,
        Config config,
        HostConfig hostConfig,
        NetworkingConfig? networkingConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a container to become healthy by polling its state.
    /// If the container has a HEALTHCHECK, waits for healthy status.
    /// Otherwise, verifies the container remains running for a grace period.
    /// </summary>
    /// <param name="containerId">The container ID to monitor.</param>
    /// <param name="timeoutSeconds">Maximum time to wait for health.</param>
    /// <param name="intervalSeconds">Polling interval.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the container is healthy, false if it exited or timed out.</returns>
    Task<bool> WaitForContainerHealthAsync(
        string containerId,
        int timeoutSeconds,
        int intervalSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Forcefully removes a container, stopping it first if running.
    /// </summary>
    /// <param name="containerId">The container ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ForceRemoveContainerAsync(string containerId, CancellationToken cancellationToken = default);
}
