namespace JustInTime.Core.Models;

public static class ScheduledTaskStatusExtensions
{
    /// <summary>Название статуса для показа пользователю.</summary>
    public static string ToDisplayName(this ScheduledTaskStatus status) => status switch
    {
        ScheduledTaskStatus.Pending => "Ожидает",
        ScheduledTaskStatus.Scheduled => "Запланирована",
        ScheduledTaskStatus.Running => "Выполняется",
        ScheduledTaskStatus.WaitingForUnlock => "Ждёт разблокировки",
        ScheduledTaskStatus.WaitingForNetwork => "Ждёт интернет",
        ScheduledTaskStatus.NeedsUserAction => "Нужно действие",
        ScheduledTaskStatus.Completed => "Выполнена",
        ScheduledTaskStatus.Failed => "Ошибка",
        ScheduledTaskStatus.Cancelled => "Отменена",
        _ => status.ToString()
    };
}
