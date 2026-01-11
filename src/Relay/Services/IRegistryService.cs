namespace Relay.Services;

/// <summary>
/// Service interface for querying Docker registries for available image tags.
/// </summary>
public interface IRegistryService
{
    /// <summary>
    /// Gets all available tags for an image from the registry.
    /// </summary>
    /// <param name="imageName">The full image name (e.g., "nginx", "library/nginx", "ghcr.io/owner/repo").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of available tags for the image.</returns>
    Task<IReadOnlyList<string>> GetTagsAsync(string imageName, CancellationToken cancellationToken = default);
}
