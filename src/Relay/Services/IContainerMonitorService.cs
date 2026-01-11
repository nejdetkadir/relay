namespace Relay.Services;

/// <summary>
/// Service interface for orchestrating container monitoring and updates.
/// </summary>
public interface IContainerMonitorService
{
    /// <summary>
    /// Performs a single check cycle: finds monitored containers, checks for updates, and applies them.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the count of containers checked, updated, and failed.</returns>
    Task<(int Checked, int Updated, int Failed)> RunCheckCycleAsync(CancellationToken cancellationToken = default);
}
