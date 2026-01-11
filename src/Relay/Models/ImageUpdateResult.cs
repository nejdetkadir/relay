namespace Relay.Models;

/// <summary>
/// Result of checking for an image update.
/// </summary>
public class ImageUpdateResult
{
    /// <summary>
    /// The container that was checked.
    /// </summary>
    public required MonitoredContainer Container { get; init; }

    /// <summary>
    /// Whether an update is available.
    /// </summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>
    /// The current image ID (digest) used by the container.
    /// </summary>
    public required string CurrentImageId { get; init; }

    /// <summary>
    /// The latest image ID (digest) available from the registry.
    /// Null if check failed.
    /// </summary>
    public string? LatestImageId { get; init; }

    /// <summary>
    /// The new image name to use (may differ from current if version tag changed).
    /// For digest updates, this is the same as Container.ImageName.
    /// For version updates, this is the new versioned image name (e.g., "nginx:1.26.0").
    /// </summary>
    public string? NewImageName { get; init; }

    /// <summary>
    /// Error message if the check failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether the check completed successfully.
    /// </summary>
    public bool Success => string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Creates a successful result indicating no update is available.
    /// </summary>
    public static ImageUpdateResult NoUpdate(MonitoredContainer container, string currentImageId)
    {
        return new ImageUpdateResult
        {
            Container = container,
            UpdateAvailable = false,
            CurrentImageId = currentImageId,
            LatestImageId = currentImageId,
            NewImageName = container.ImageName
        };
    }

    /// <summary>
    /// Creates a successful result indicating an update is available (same tag, new digest).
    /// </summary>
    public static ImageUpdateResult UpdateFound(MonitoredContainer container, string currentImageId, string latestImageId)
    {
        return new ImageUpdateResult
        {
            Container = container,
            UpdateAvailable = true,
            CurrentImageId = currentImageId,
            LatestImageId = latestImageId,
            NewImageName = container.ImageName
        };
    }

    /// <summary>
    /// Creates a successful result indicating an update is available (new version tag).
    /// </summary>
    public static ImageUpdateResult UpdateFound(MonitoredContainer container, string currentImageId, string latestImageId, string newImageName)
    {
        return new ImageUpdateResult
        {
            Container = container,
            UpdateAvailable = true,
            CurrentImageId = currentImageId,
            LatestImageId = latestImageId,
            NewImageName = newImageName
        };
    }

    /// <summary>
    /// Creates a failed result with an error message.
    /// </summary>
    public static ImageUpdateResult Failed(MonitoredContainer container, string currentImageId, string errorMessage)
    {
        return new ImageUpdateResult
        {
            Container = container,
            UpdateAvailable = false,
            CurrentImageId = currentImageId,
            ErrorMessage = errorMessage
        };
    }
}
