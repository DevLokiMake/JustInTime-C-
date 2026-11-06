namespace JustInTime.Core.Models;

/// <summary>Запись о попытке выполнения задачи. В БД: таблица "ExecutionHistory".</summary>
public class ExecutionHistory
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TaskId { get; set; }

    public ScheduledTask? ScheduledTask { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public ExecutionResult Result { get; set; }

    public string? ErrorMessage { get; set; }

    public int AttemptNumber { get; set; } = 1;
}
