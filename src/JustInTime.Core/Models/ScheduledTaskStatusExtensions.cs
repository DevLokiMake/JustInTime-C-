using JustInTime.Core.Localization;

namespace JustInTime.Core.Models;

public static class ScheduledTaskStatusExtensions
{
    /// <summary>Название статуса для показа пользователю на текущем языке интерфейса.</summary>
    public static string ToDisplayName(this ScheduledTaskStatus status) => status switch
    {
        ScheduledTaskStatus.Pending => Loc.T("status_pending"),
        ScheduledTaskStatus.Scheduled => Loc.T("status_scheduled"),
        ScheduledTaskStatus.Running => Loc.T("status_running"),
        ScheduledTaskStatus.WaitingForUnlock => Loc.T("status_waiting_unlock"),
        ScheduledTaskStatus.WaitingForNetwork => Loc.T("status_waiting_network"),
        ScheduledTaskStatus.NeedsUserAction => Loc.T("status_needs_action"),
        ScheduledTaskStatus.Completed => Loc.T("status_completed"),
        ScheduledTaskStatus.Failed => Loc.T("status_failed"),
        ScheduledTaskStatus.Cancelled => Loc.T("status_cancelled"),
        _ => status.ToString()
    };
}
