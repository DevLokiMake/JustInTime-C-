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

    /// <summary>
    /// Задачу можно изменить или отменить: она не завершена и сейчас не выполняется.
    /// Выполняющуюся задачу менять нельзя, чтобы не получить рассинхрон с уже идущим действием.
    /// </summary>
    public static readonly ScheduledTaskStatus[] Editable =
    [
        ScheduledTaskStatus.Pending,
        ScheduledTaskStatus.Scheduled,
        ScheduledTaskStatus.WaitingForUnlock,
        ScheduledTaskStatus.WaitingForNetwork,
        ScheduledTaskStatus.NeedsUserAction
    ];

    /// <summary>
    /// Задачу можно запустить, когда наступило её время. NeedsUserAction сюда намеренно не входит:
    /// если результат прошлой попытки неизвестен, автоматический повтор может создать дубль
    /// (например, второе сообщение получателю).
    /// </summary>
    public static readonly ScheduledTaskStatus[] Startable =
    [
        ScheduledTaskStatus.Pending,
        ScheduledTaskStatus.Scheduled,
        ScheduledTaskStatus.WaitingForUnlock,
        ScheduledTaskStatus.WaitingForNetwork
    ];

    /// <summary>Задача завершена и больше сама не выполнится.</summary>
    public static readonly ScheduledTaskStatus[] Finished =
    [
        ScheduledTaskStatus.Completed,
        ScheduledTaskStatus.Failed,
        ScheduledTaskStatus.Cancelled
    ];

    /// <summary>
    /// Задачу можно запустить повторно по явной команде пользователя.
    /// Для NeedsUserAction интерфейс обязан предупредить о возможном дубле.
    /// </summary>
    public static readonly ScheduledTaskStatus[] Retryable =
    [
        ScheduledTaskStatus.Failed,
        ScheduledTaskStatus.NeedsUserAction
    ];
}
