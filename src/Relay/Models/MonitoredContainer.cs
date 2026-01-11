namespace Relay.Models;

/// <summary>
/// Represents a container being monitored by Relay.
/// </summary>
public class MonitoredContainer
{
    /// <summary>
    /// The unique identifier of the container.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The name of the container (without leading slash).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The full image name including tag (e.g., "nginx:latest").
    /// </summary>
    public required string ImageName { get; init; }

    /// <summary>
    /// The image tag only (e.g., "latest", "1.25.0").
    /// </summary>
    public string ImageTag => GetImageTag();

    /// <summary>
    /// The image repository without tag (e.g., "nginx", "myuser/myapp").
    /// </summary>
    public string ImageRepository => GetImageRepository();

    /// <summary>
    /// The image ID (digest) currently used by the container.
    /// </summary>
    public required string ImageId { get; init; }

    /// <summary>
    /// Container creation timestamp.
    /// </summary>
    public DateTime Created { get; init; }

    /// <summary>
    /// Current state of the container (e.g., "running", "exited").
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Container labels.
    /// </summary>
    public IDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The update strategy for this container, derived from labels.
    /// </summary>
    public UpdateStrategy UpdateStrategy => UpdateStrategyExtensions.FromLabels(Labels);

    private string GetImageTag()
    {
        var colonIndex = ImageName.LastIndexOf(':');
        if (colonIndex == -1)
            return "latest";

        // Check if the colon is part of a port number in the registry URL
        var slashIndex = ImageName.LastIndexOf('/');
        if (colonIndex < slashIndex)
            return "latest";

        return ImageName[(colonIndex + 1)..];
    }

    private string GetImageRepository()
    {
        var colonIndex = ImageName.LastIndexOf(':');
        if (colonIndex == -1)
            return ImageName;

        // Check if the colon is part of a port number in the registry URL
        var slashIndex = ImageName.LastIndexOf('/');
        if (colonIndex < slashIndex)
            return ImageName;

        return ImageName[..colonIndex];
    }
}
