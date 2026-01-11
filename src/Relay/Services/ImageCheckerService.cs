using Microsoft.Extensions.Logging;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service for checking if newer versions of container images are available.
/// Supports both digest-based updates (same tag rebuilt) and version-based updates (newer semver tags).
/// </summary>
public class ImageCheckerService : IImageCheckerService
{
    private readonly IDockerService _dockerService;
    private readonly IRegistryService _registryService;
    private readonly IVersionService _versionService;
    private readonly ILogger<ImageCheckerService> _logger;

    public ImageCheckerService(
        IDockerService dockerService,
        IRegistryService registryService,
        IVersionService versionService,
        ILogger<ImageCheckerService> logger)
    {
        _dockerService = dockerService;
        _registryService = registryService;
        _versionService = versionService;
        _logger = logger;
    }

    public async Task<ImageUpdateResult> CheckForUpdateAsync(MonitoredContainer container, CancellationToken cancellationToken = default)
    {
        try
        {
            var strategy = container.UpdateStrategy;
            _logger.LogDebug("Checking for updates: {ContainerName} using image {ImageName} (Strategy: {Strategy})",
                container.Name, container.ImageName, strategy);

            // For version-based strategies, check for newer version tags first
            if (strategy.RequiresRegistryQuery())
            {
                return await CheckForVersionUpdateAsync(container, strategy, cancellationToken);
            }

            // Default: digest-based update check
            return await CheckForDigestUpdateAsync(container, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates for container {ContainerName}", container.Name);
            return ImageUpdateResult.Failed(container, container.ImageId, ex.Message);
        }
    }

    private async Task<ImageUpdateResult> CheckForDigestUpdateAsync(MonitoredContainer container, CancellationToken cancellationToken)
    {
        var currentImageId = container.ImageId;

        // Pull the latest image from the registry (same tag)
        string latestImageId;
        try
        {
            latestImageId = await _dockerService.PullImageAsync(container.ImageName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pull image {ImageName} for container {ContainerName}",
                container.ImageName, container.Name);
            return ImageUpdateResult.Failed(container, currentImageId, $"Failed to pull image: {ex.Message}");
        }

        // Compare the image IDs (digests)
        if (string.Equals(currentImageId, latestImageId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Container {ContainerName} is up to date (Image ID: {ImageId})",
                container.Name, TruncateId(currentImageId));
            return ImageUpdateResult.NoUpdate(container, currentImageId);
        }

        _logger.LogInformation("Update available for container {ContainerName}: {CurrentId} -> {LatestId}",
            container.Name, TruncateId(currentImageId), TruncateId(latestImageId));

        return ImageUpdateResult.UpdateFound(container, currentImageId, latestImageId, container.ImageName);
    }

    private async Task<ImageUpdateResult> CheckForVersionUpdateAsync(
        MonitoredContainer container,
        UpdateStrategy strategy,
        CancellationToken cancellationToken)
    {
        var currentImageId = container.ImageId;
        var currentTag = container.ImageTag;

        // Query registry for available tags
        _logger.LogDebug("Querying registry for available tags for {ImageName}", container.ImageRepository);
        var availableTags = await _registryService.GetTagsAsync(container.ImageName, cancellationToken);

        if (availableTags.Count == 0)
        {
            _logger.LogWarning("No tags found for image {ImageName}", container.ImageRepository);
            // Fall back to digest check
            return await CheckForDigestUpdateAsync(container, cancellationToken);
        }

        _logger.LogDebug("Found {TagCount} tags for {ImageName}", availableTags.Count, container.ImageRepository);

        // Find the newest version that matches the update strategy
        var newestTag = _versionService.FindNewestVersion(currentTag, availableTags, strategy);

        if (newestTag == null)
        {
            _logger.LogDebug("No newer version found for container {ContainerName} (current: {CurrentTag}, strategy: {Strategy})",
                container.Name, currentTag, strategy);

            // Also check if current tag was rebuilt (digest update)
            return await CheckForDigestUpdateAsync(container, cancellationToken);
        }

        // A newer version tag was found - pull it
        var newImageName = $"{container.ImageRepository}:{newestTag}";
        _logger.LogInformation("Newer version found for container {ContainerName}: {CurrentTag} -> {NewTag}",
            container.Name, currentTag, newestTag);

        string newImageId;
        try
        {
            newImageId = await _dockerService.PullImageAsync(newImageName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pull new version image {ImageName} for container {ContainerName}",
                newImageName, container.Name);
            return ImageUpdateResult.Failed(container, currentImageId, $"Failed to pull image: {ex.Message}");
        }

        return ImageUpdateResult.UpdateFound(container, currentImageId, newImageId, newImageName);
    }

    private static string TruncateId(string id) => id[..Math.Min(12, id.Length)];
}
