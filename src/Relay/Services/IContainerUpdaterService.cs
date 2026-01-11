using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service interface for updating containers with new images.
/// </summary>
public interface IContainerUpdaterService
{
    /// <summary>
    /// Updates a container by stopping it, creating a new container with the 
    /// same configuration but a new image, and removing the old container.
    /// </summary>
    /// <param name="container">The container to update.</param>
    /// <param name="newImageName">The new image name to use (e.g., "nginx:1.26.0").</param>
    /// <param name="newImageId">The new image ID (digest).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the update was successful, false otherwise.</returns>
    Task<bool> UpdateContainerAsync(MonitoredContainer container, string newImageName, string newImageId, CancellationToken cancellationToken = default);
}
