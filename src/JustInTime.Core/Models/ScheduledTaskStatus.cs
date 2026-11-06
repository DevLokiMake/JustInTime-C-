namespace JustInTime.Core.Models;

/// <summary>
/// Статус задачи. Назван ScheduledTaskStatus, чтобы не конфликтовать
/// с System.Threading.Tasks.TaskStatus.
/// </summary>
public enum ScheduledTaskStatus
{
    Pending,
    Scheduled,
    Running,
    WaitingForUnlock,
    WaitingForNetwork,
    NeedsUserAction,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Готовые группы статусов, чтобы не дублировать списки в запросах.</summary>
public static class ScheduledTaskStatuses
{
    /// <summary>Задача ещё не завершена (ожидает, выполняется или ждёт условий).</summary>
    public static readonly ScheduledTaskStatus[] Active =
    [
        ScheduledTaskStatus.Pending,
        ScheduledTaskStatus.Scheduled,
        ScheduledTaskStatus.Running,
        ScheduledTaskStatus.WaitingForUnlock,
        ScheduledTaskStatus.WaitingForNetwork,
        ScheduledTaskStatus.NeedsUserAction
    ];

    /// <summary>Задача ждёт наступления времени запуска.</summary>
    public static readonly ScheduledTaskStatus[] Upcoming =
    [
        ScheduledTaskStatus.Pending,
        ScheduledTaskStatus.Scheduled
    ];
}
