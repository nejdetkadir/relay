using Microsoft.Extensions.Logging;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Orchestrates container monitoring, update checking, and container replacement.
/// </summary>
public class ContainerMonitorService : IContainerMonitorService
{
    private readonly IDockerService _dockerService;
    private readonly IImageCheckerService _imageChecker;
    private readonly IContainerUpdaterService _containerUpdater;
    private readonly ILogger<ContainerMonitorService> _logger;

    public ContainerMonitorService(
        IDockerService dockerService,
        IImageCheckerService imageChecker,
        IContainerUpdaterService containerUpdater,
        ILogger<ContainerMonitorService> logger)
    {
        _dockerService = dockerService;
        _imageChecker = imageChecker;
        _containerUpdater = containerUpdater;
        _logger = logger;
    }

    public async Task<(int Checked, int Updated, int Failed)> RunCheckCycleAsync(CancellationToken cancellationToken = default)
    {
        int checkedCount = 0;
        int updatedCount = 0;
        int failedCount = 0;

        try
        {
            // Get all containers with the relay.enable=true label
            var containers = await _dockerService.GetMonitoredContainersAsync(cancellationToken);

            if (containers.Count == 0)
            {
                _logger.LogInformation("No containers found with relay monitoring enabled");
                return (0, 0, 0);
            }

            _logger.LogInformation("Check cycle started. Found {ContainerCount} monitored container(s)", containers.Count);

            foreach (var container in containers)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Check cycle cancelled");
                    break;
                }

                checkedCount++;

                try
                {
                    var result = await ProcessContainerAsync(container, cancellationToken);

                    if (result == UpdateResult.Updated)
                    {
                        updatedCount++;
                    }
                    else if (result == UpdateResult.Failed)
                    {
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing container {ContainerName}", container.Name);
                    failedCount++;
                }
            }

            LogCycleSummary(checkedCount, updatedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during check cycle");
        }

        return (checkedCount, updatedCount, failedCount);
    }

    private async Task<UpdateResult> ProcessContainerAsync(MonitoredContainer container, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing container {ContainerName} ({ImageName}, Strategy: {Strategy})", 
            container.Name, container.ImageName, container.UpdateStrategy);

        // Check if an update is available
        var updateResult = await _imageChecker.CheckForUpdateAsync(container, cancellationToken);

        if (!updateResult.Success)
        {
            _logger.LogWarning("Container {ContainerName} ({ImageName}) - Check failed: {Error}",
                container.Name, container.ImageName, updateResult.ErrorMessage);
            return UpdateResult.Failed;
        }

        if (!updateResult.UpdateAvailable)
        {
            _logger.LogInformation("Container {ContainerName} ({ImageName}) - No update available",
                container.Name, container.ImageName);
            return UpdateResult.NoUpdate;
        }

        // Update is available - apply it
        var newImageName = updateResult.NewImageName ?? container.ImageName;
        _logger.LogInformation("Container {ContainerName} - Update detected: {OldImage} -> {NewImage}",
            container.Name, container.ImageName, newImageName);

        var success = await _containerUpdater.UpdateContainerAsync(
            container,
            newImageName,
            updateResult.LatestImageId!,
            cancellationToken);

        return success ? UpdateResult.Updated : UpdateResult.Failed;
    }

    private void LogCycleSummary(int checked_, int updated, int failed)
    {
        if (updated > 0 || failed > 0)
        {
            _logger.LogInformation(
                "Check cycle completed. Checked: {Checked}, Updated: {Updated}, Failed: {Failed}, Unchanged: {Unchanged}",
                checked_, updated, failed, checked_ - updated - failed);
        }
        else
        {
            _logger.LogInformation("Check cycle completed. All {Checked} container(s) are up to date", checked_);
        }
    }

    private enum UpdateResult
    {
        NoUpdate,
        Updated,
        Failed
    }
}
