namespace Relay.Models;

/// <summary>
/// Defines the strategy for detecting and applying container image updates.
/// </summary>
public enum UpdateStrategy
{
    /// <summary>
    /// Only update if the same tag has a new digest (image was rebuilt).
    /// This is the default behavior and works for any tag including 'latest'.
    /// </summary>
    Digest,

    /// <summary>
    /// Update to newer patch versions only.
    /// Example: 1.25.0 -> 1.25.1, but NOT 1.26.0 or 2.0.0
    /// </summary>
    Patch,

    /// <summary>
    /// Update to newer minor versions (includes patch updates).
    /// Example: 1.25.0 -> 1.26.0, but NOT 2.0.0
    /// </summary>
    Minor,

    /// <summary>
    /// Update to any newer version (includes major updates).
    /// Example: 1.25.0 -> 2.0.0
    /// </summary>
    Major
}

/// <summary>
/// Extension methods for UpdateStrategy.
/// </summary>
public static class UpdateStrategyExtensions
{
    /// <summary>
    /// Label key used to specify update strategy.
    /// </summary>
    public const string LabelKey = "relay.update";

    /// <summary>
    /// Label key used to override health check timeout per container (in seconds).
    /// </summary>
    public const string HealthCheckTimeoutLabelKey = "relay.healthcheck.timeout";

    /// <summary>
    /// Parses an update strategy from a string value.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed strategy, or Digest if invalid.</returns>
    public static UpdateStrategy Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return UpdateStrategy.Digest;

        return value.ToLowerInvariant() switch
        {
            "digest" => UpdateStrategy.Digest,
            "patch" => UpdateStrategy.Patch,
            "minor" => UpdateStrategy.Minor,
            "major" => UpdateStrategy.Major,
            _ => UpdateStrategy.Digest
        };
    }

    /// <summary>
    /// Gets the update strategy from container labels.
    /// </summary>
    /// <param name="labels">Container labels.</param>
    /// <returns>The update strategy specified in labels, or Digest if not specified.</returns>
    public static UpdateStrategy FromLabels(IDictionary<string, string>? labels)
    {
        if (labels == null || !labels.TryGetValue(LabelKey, out var value))
            return UpdateStrategy.Digest;

        return Parse(value);
    }

    /// <summary>
    /// Returns whether this strategy requires querying the registry for available tags.
    /// </summary>
    public static bool RequiresRegistryQuery(this UpdateStrategy strategy)
    {
        return strategy != UpdateStrategy.Digest;
    }

    /// <summary>
    /// Gets the health check timeout override from container labels.
    /// </summary>
    /// <param name="labels">Container labels.</param>
    /// <param name="defaultTimeoutSeconds">Default timeout if not specified in labels.</param>
    /// <returns>The timeout in seconds.</returns>
    public static int GetHealthCheckTimeout(IDictionary<string, string>? labels, int defaultTimeoutSeconds)
    {
        if (labels == null || !labels.TryGetValue(HealthCheckTimeoutLabelKey, out var value))
            return defaultTimeoutSeconds;

        if (int.TryParse(value, out var timeout) && timeout > 0)
            return timeout;

        return defaultTimeoutSeconds;
    }
}
