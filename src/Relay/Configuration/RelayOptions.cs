namespace Relay.Configuration;

/// <summary>
/// Configuration options for the Relay container monitoring service.
/// </summary>
public class RelayOptions
{
    /// <summary>
    /// Configuration section name for binding.
    /// </summary>
    public const string SectionName = "Relay";

    /// <summary>
    /// Interval in seconds between container update checks.
    /// Default: 300 seconds (5 minutes).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Label key used to identify containers that should be monitored.
    /// Containers with this label set to "true" will be monitored for updates.
    /// Default: "relay.enable"
    /// </summary>
    public string LabelKey { get; set; } = "relay.enable";

    /// <summary>
    /// Whether to remove old images after updating containers.
    /// Default: false
    /// </summary>
    public bool CleanupOldImages { get; set; } = false;

    /// <summary>
    /// Docker host URI. Uses Unix socket by default.
    /// </summary>
    public string DockerHost { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>
    /// Timeout in seconds for Docker API operations.
    /// Default: 60 seconds.
    /// </summary>
    public int DockerTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Whether to run an immediate check on startup before waiting for the first interval.
    /// Default: true
    /// </summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>
    /// Path to the Docker config.json file containing registry credentials.
    /// If not specified, will try standard locations:
    /// - DOCKER_CONFIG environment variable
    /// - ~/.docker/config.json
    /// - /root/.docker/config.json
    /// </summary>
    public string? DockerConfigPath { get; set; }

    /// <summary>
    /// Whether to use rolling updates when updating containers.
    /// When enabled, a staging container is started without port bindings to verify
    /// health before stopping the old container, minimizing downtime.
    /// Default: true
    /// </summary>
    public bool RollingUpdateEnabled { get; set; } = true;

    /// <summary>
    /// Maximum time in seconds to wait for a container's health check to pass
    /// during rolling updates. If the container has no HEALTHCHECK defined,
    /// this is the time to verify the container stays running.
    /// Default: 60 seconds.
    /// </summary>
    public int HealthCheckTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Interval in seconds between health check polls during rolling updates.
    /// Default: 5 seconds.
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 5;
}
