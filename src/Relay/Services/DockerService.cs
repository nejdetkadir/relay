using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Configuration;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Implementation of Docker API operations using Docker.DotNet.
/// </summary>
public class DockerService : IDockerService, IDisposable
{
    private readonly DockerClient _client;
    private readonly RelayOptions _options;
    private readonly ILogger<DockerService> _logger;
    private bool _disposed;

    public DockerService(IOptions<RelayOptions> options, ILogger<DockerService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new DockerClientConfiguration(new Uri(_options.DockerHost))
            .CreateClient();
    }

    public async Task<IReadOnlyList<MonitoredContainer>> GetMonitoredContainersAsync(CancellationToken cancellationToken = default)
    {
        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["label"] = new Dictionary<string, bool>
            {
                [$"{_options.LabelKey}=true"] = true
            }
        };

        var containers = await _client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = false, // Only running containers
                Filters = filters
            },
            cancellationToken);

        return containers.Select(c => new MonitoredContainer
        {
            Id = c.ID,
            Name = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12],
            ImageName = c.Image,
            ImageId = c.ImageID,
            Created = c.Created,
            State = c.State,
            Labels = c.Labels ?? new Dictionary<string, string>()
        }).ToList();
    }

    public async Task<ContainerInspectResponse> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        return await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
    }

    public async Task<string> PullImageAsync(string imageName, CancellationToken cancellationToken = default)
    {
        var (repository, tag) = ParseImageName(imageName);

        _logger.LogInformation("Pulling image {ImageName}...", imageName);

        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = repository,
                Tag = tag
            },
            null, // Auth config - could be extended for private registries
            new Progress<JSONMessage>(message =>
            {
                if (!string.IsNullOrEmpty(message.Status))
                {
                    _logger.LogDebug("Pull progress: {Status} {Progress}", message.Status, message.ProgressMessage);
                }
            }),
            cancellationToken);

        // Get the new image ID after pulling
        var imageId = await GetImageIdAsync(imageName, cancellationToken);
        return imageId ?? throw new InvalidOperationException($"Failed to get image ID after pulling {imageName}");
    }

    public async Task<string?> GetImageIdAsync(string imageName, CancellationToken cancellationToken = default)
    {
        try
        {
            var image = await _client.Images.InspectImageAsync(imageName, cancellationToken);
            return image.ID;
        }
        catch (DockerImageNotFoundException)
        {
            return null;
        }
    }

    public async Task StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping container {ContainerId}...", containerId[..12]);

        await _client.Containers.StopContainerAsync(
            containerId,
            new ContainerStopParameters
            {
                WaitBeforeKillSeconds = 10
            },
            cancellationToken);
    }

    public async Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Removing container {ContainerId}...", containerId[..12]);

        await _client.Containers.RemoveContainerAsync(
            containerId,
            new ContainerRemoveParameters
            {
                Force = false,
                RemoveVolumes = false
            },
            cancellationToken);
    }

    public async Task<string> CreateAndStartContainerAsync(
        string name,
        Config config,
        HostConfig hostConfig,
        NetworkingConfig? networkingConfig,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating container {ContainerName}...", name);

        var createResponse = await _client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = name,
                Image = config.Image,
                Env = config.Env,
                Cmd = config.Cmd,
                Entrypoint = config.Entrypoint,
                WorkingDir = config.WorkingDir,
                Labels = config.Labels,
                ExposedPorts = config.ExposedPorts,
                Volumes = config.Volumes,
                User = config.User,
                Tty = config.Tty,
                AttachStdin = config.AttachStdin,
                AttachStdout = config.AttachStdout,
                AttachStderr = config.AttachStderr,
                HostConfig = hostConfig,
                NetworkingConfig = networkingConfig
            },
            cancellationToken);

        _logger.LogDebug("Starting container {ContainerName} ({ContainerId})...", name, createResponse.ID[..12]);

        await _client.Containers.StartContainerAsync(
            createResponse.ID,
            new ContainerStartParameters(),
            cancellationToken);

        return createResponse.ID;
    }

    public async Task RemoveImageAsync(string imageId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Removing image {ImageId}...", imageId[..12]);

        try
        {
            await _client.Images.DeleteImageAsync(
                imageId,
                new ImageDeleteParameters
                {
                    Force = false
                },
                cancellationToken);
        }
        catch (DockerApiException ex) when (ex.Message.Contains("image is being used"))
        {
            _logger.LogDebug("Image {ImageId} is still in use, skipping removal", imageId[..12]);
        }
    }

    private static (string repository, string tag) ParseImageName(string imageName)
    {
        // Handle images with registry prefix and/or port
        var lastColonIndex = imageName.LastIndexOf(':');
        var lastSlashIndex = imageName.LastIndexOf('/');

        // Check if the colon is part of a port number in the registry URL
        if (lastColonIndex > lastSlashIndex && lastColonIndex != -1)
        {
            var potentialTag = imageName[(lastColonIndex + 1)..];
            // If it doesn't look like a port number, treat it as a tag
            if (!potentialTag.All(char.IsDigit) || potentialTag.Contains('/'))
            {
                return (imageName[..lastColonIndex], potentialTag);
            }
        }

        // No tag specified, use "latest"
        return (imageName, "latest");
    }

    public async Task<string> CreateStagingContainerAsync(
        string stagingName,
        Config config,
        HostConfig hostConfig,
        NetworkingConfig? networkingConfig,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating staging container {ContainerName} (without port bindings)...", stagingName);

        // Clone host config but strip port bindings to avoid conflicts
        var stagingHostConfig = CloneHostConfigWithoutPorts(hostConfig);

        var createResponse = await _client.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = stagingName,
                Image = config.Image,
                Env = config.Env,
                Cmd = config.Cmd,
                Entrypoint = config.Entrypoint,
                WorkingDir = config.WorkingDir,
                Labels = config.Labels,
                ExposedPorts = config.ExposedPorts,
                Volumes = config.Volumes,
                User = config.User,
                Tty = config.Tty,
                AttachStdin = config.AttachStdin,
                AttachStdout = config.AttachStdout,
                AttachStderr = config.AttachStderr,
                Healthcheck = config.Healthcheck,
                HostConfig = stagingHostConfig,
                NetworkingConfig = networkingConfig
            },
            cancellationToken);

        _logger.LogDebug("Starting staging container {ContainerName} ({ContainerId})...", stagingName, createResponse.ID[..12]);

        await _client.Containers.StartContainerAsync(
            createResponse.ID,
            new ContainerStartParameters(),
            cancellationToken);

        return createResponse.ID;
    }

    public async Task<bool> WaitForContainerHealthAsync(
        string containerId,
        int timeoutSeconds,
        int intervalSeconds,
        CancellationToken cancellationToken = default)
    {
        var shortId = containerId[..Math.Min(12, containerId.Length)];
        _logger.LogDebug("Waiting for container {ContainerId} to become healthy (timeout: {Timeout}s)...", shortId, timeoutSeconds);

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var gracePeriodStart = DateTime.UtcNow;
        const int runningGracePeriodSeconds = 5; // Container must stay running for this long if no healthcheck

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var inspection = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
                var state = inspection.State;

                // Container exited - health check failed
                if (state.Status == "exited" || state.Status == "dead")
                {
                    _logger.LogWarning("Container {ContainerId} exited with code {ExitCode}", shortId, state.ExitCode);
                    return false;
                }

                // Check if container has a health check defined
                if (inspection.Config?.Healthcheck != null && inspection.Config.Healthcheck.Test?.Count > 0)
                {
                    // Container has a health check - wait for it to report healthy
                    var healthStatus = state.Health?.Status;

                    if (healthStatus == "healthy")
                    {
                        _logger.LogDebug("Container {ContainerId} is healthy", shortId);
                        return true;
                    }

                    if (healthStatus == "unhealthy")
                    {
                        _logger.LogWarning("Container {ContainerId} health check failed", shortId);
                        return false;
                    }

                    _logger.LogDebug("Container {ContainerId} health status: {Status}, waiting...", shortId, healthStatus ?? "starting");
                }
                else
                {
                    // No health check defined - verify container stays running for grace period
                    if (state.Status == "running")
                    {
                        var runningDuration = DateTime.UtcNow - gracePeriodStart;
                        if (runningDuration.TotalSeconds >= runningGracePeriodSeconds)
                        {
                            _logger.LogDebug("Container {ContainerId} has been running for {Duration}s (no healthcheck defined)", 
                                shortId, (int)runningDuration.TotalSeconds);
                            return true;
                        }
                    }
                    else
                    {
                        // Reset grace period if container is not running
                        gracePeriodStart = DateTime.UtcNow;
                    }
                }
            }
            catch (DockerContainerNotFoundException)
            {
                _logger.LogWarning("Container {ContainerId} no longer exists", shortId);
                return false;
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
        }

        _logger.LogWarning("Timed out waiting for container {ContainerId} to become healthy", shortId);
        return false;
    }

    public async Task ForceRemoveContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        var shortId = containerId[..Math.Min(12, containerId.Length)];
        _logger.LogDebug("Force removing container {ContainerId}...", shortId);

        try
        {
            // Try to stop first (ignore errors if already stopped)
            try
            {
                await _client.Containers.StopContainerAsync(
                    containerId,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                    cancellationToken);
            }
            catch (DockerContainerNotFoundException)
            {
                // Container already gone
                return;
            }
            catch (DockerApiException)
            {
                // Ignore stop errors (container might already be stopped)
            }

            await _client.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters
                {
                    Force = true,
                    RemoveVolumes = false
                },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.LogDebug("Container {ContainerId} already removed", shortId);
        }
    }

    private static HostConfig CloneHostConfigWithoutPorts(HostConfig original)
    {
        return new HostConfig
        {
            // Strip port bindings to avoid conflicts during staging
            PortBindings = null,
            PublishAllPorts = false,

            // Preserve all other settings
            Binds = original.Binds,
            Links = original.Links,
            Memory = original.Memory,
            MemorySwap = original.MemorySwap,
            MemoryReservation = original.MemoryReservation,
            NanoCPUs = original.NanoCPUs,
            CPUShares = original.CPUShares,
            CPUPeriod = original.CPUPeriod,
            CPUQuota = original.CPUQuota,
            CPURealtimePeriod = original.CPURealtimePeriod,
            CPURealtimeRuntime = original.CPURealtimeRuntime,
            CpusetCpus = original.CpusetCpus,
            CpusetMems = original.CpusetMems,
            Devices = original.Devices,
            DeviceCgroupRules = original.DeviceCgroupRules,
            MemorySwappiness = original.MemorySwappiness,
            OomKillDisable = original.OomKillDisable,
            PidsLimit = original.PidsLimit,
            Ulimits = original.Ulimits,
            CPUCount = original.CPUCount,
            CPUPercent = original.CPUPercent,
            BlkioWeight = original.BlkioWeight,
            BlkioWeightDevice = original.BlkioWeightDevice,
            BlkioDeviceReadBps = original.BlkioDeviceReadBps,
            BlkioDeviceWriteBps = original.BlkioDeviceWriteBps,
            BlkioDeviceReadIOps = original.BlkioDeviceReadIOps,
            BlkioDeviceWriteIOps = original.BlkioDeviceWriteIOps,
            ContainerIDFile = original.ContainerIDFile,
            CapAdd = original.CapAdd,
            CapDrop = original.CapDrop,
            GroupAdd = original.GroupAdd,
            RestartPolicy = original.RestartPolicy,
            NetworkMode = original.NetworkMode,
            IpcMode = original.IpcMode,
            PidMode = original.PidMode,
            UTSMode = original.UTSMode,
            UsernsMode = original.UsernsMode,
            ShmSize = original.ShmSize,
            Sysctls = original.Sysctls,
            Runtime = original.Runtime,
            Isolation = original.Isolation,
            VolumeDriver = original.VolumeDriver,
            VolumesFrom = original.VolumesFrom,
            Mounts = original.Mounts,
            MaskedPaths = original.MaskedPaths,
            ReadonlyPaths = original.ReadonlyPaths,
            Init = original.Init,
            Privileged = original.Privileged,
            ReadonlyRootfs = original.ReadonlyRootfs,
            DNS = original.DNS,
            DNSOptions = original.DNSOptions,
            DNSSearch = original.DNSSearch,
            ExtraHosts = original.ExtraHosts,
            AutoRemove = original.AutoRemove,
            LogConfig = original.LogConfig,
            SecurityOpt = original.SecurityOpt,
            StorageOpt = original.StorageOpt,
            CgroupParent = original.CgroupParent,
            Cgroup = original.Cgroup,
            OomScoreAdj = original.OomScoreAdj
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
