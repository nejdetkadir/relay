using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service interface for checking image updates.
/// </summary>
public interface IImageCheckerService
{
    /// <summary>
    /// Checks if a newer version of the container's image is available.
    /// This pulls the latest image and compares digests.
    /// </summary>
    /// <param name="container">The container to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether an update is available.</returns>
    Task<ImageUpdateResult> CheckForUpdateAsync(MonitoredContainer container, CancellationToken cancellationToken = default);
}
