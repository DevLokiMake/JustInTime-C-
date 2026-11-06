namespace JustInTime.Core.Models;

/// <summary>Данные для главной страницы.</summary>
public sealed record DashboardSnapshot(
    int ActiveTasks,
    int FailedTasks,
    ScheduledTask? NextTask,
    IReadOnlyList<ExecutionHistory> RecentExecutions,
    IReadOnlyList<ExecutionHistory> RecentFailures);
