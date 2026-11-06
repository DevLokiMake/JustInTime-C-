namespace JustInTime.Core.Models;

/// <summary>
/// Запланированная задача. В БД хранится в таблице "Tasks".
/// Все даты хранятся в UTC, часовой пояс пользователя лежит отдельным полем.
/// </summary>
public class ScheduledTask
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Title { get; set; } = string.Empty;

    public ActionType ActionType { get; set; }

    /// <summary>Когда нужно выполнить задачу (UTC).</summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>Идентификатор часового пояса, в котором пользователь создал задачу.</summary>
    public string TimeZone { get; set; } = TimeZoneInfo.Local.Id;

    public ScheduledTaskStatus Status { get; set; } = ScheduledTaskStatus.Pending;

    /// <summary>Параметры действия в формате JSON (получатель, текст сообщения и т.д.).</summary>
    public string Payload { get; set; } = "{}";

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastExecutedAt { get; set; }

    public bool IsRecurring { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<ExecutionHistory> History { get; set; } = [];
}
