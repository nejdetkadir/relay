using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service interface for reading Docker configuration and credentials.
/// </summary>
public interface IDockerConfigService
{
    /// <summary>
    /// Gets credentials for a specific registry from Docker config.
    /// </summary>
    /// <param name="registry">The registry hostname (e.g., "ghcr.io", "docker.io").</param>
    /// <returns>Credentials for the registry, or None if not found.</returns>
    DockerCredentials GetCredentials(string registry);

    /// <summary>
    /// Checks if credentials are available for a specific registry.
    /// </summary>
    /// <param name="registry">The registry hostname.</param>
    /// <returns>True if credentials are available.</returns>
    bool HasCredentials(string registry);

    /// <summary>
    /// Reloads the Docker configuration from disk.
    /// </summary>
    void Reload();
}
