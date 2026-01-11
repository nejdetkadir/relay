using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Configuration;
using Relay.Services;

namespace Relay.Workers;

/// <summary>
/// Background service that periodically checks for container updates.
/// </summary>
public class RelayWorker : BackgroundService
{
    private readonly IContainerMonitorService _monitorService;
    private readonly RelayOptions _options;
    private readonly ILogger<RelayWorker> _logger;

    public RelayWorker(
        IContainerMonitorService monitorService,
        IOptions<RelayOptions> options,
        ILogger<RelayWorker> logger)
    {
        _monitorService = monitorService;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Relay started. Monitoring containers with label: {LabelKey}=true. Check interval: {Interval} seconds",
            _options.LabelKey,
            _options.CheckIntervalSeconds);

        // Run an immediate check on startup if configured
        if (_options.CheckOnStartup)
        {
            _logger.LogInformation("Running initial check...");
            await RunCheckCycleAsync(stoppingToken);
        }

        // Use PeriodicTimer for efficient interval-based execution
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CheckIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCheckCycleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Relay is shutting down...");
        }
    }

    private async Task RunCheckCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _monitorService.RunCheckCycleAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during check cycle. Will retry at next interval.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Relay stopping...");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Relay stopped");
    }
}
