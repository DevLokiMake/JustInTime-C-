using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

public sealed class DashboardService(
    ITaskRepository tasks,
    IExecutionHistoryRepository history) : IDashboardService
{
    private const int ListSize = 5;

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var active = await tasks.CountActiveAsync(cancellationToken);
        var failed = await tasks.CountByStatusAsync(ScheduledTaskStatus.Failed, cancellationToken);
        var next = await tasks.GetNextScheduledAsync(cancellationToken);
        var recent = await history.GetRecentAsync(ListSize, cancellationToken);
        var failures = await history.GetRecentFailuresAsync(ListSize, cancellationToken);

        return new DashboardSnapshot(active, failed, next, recent, failures);
    }
}
