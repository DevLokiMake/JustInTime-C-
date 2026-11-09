namespace JustInTime.Core.Models;

/// <summary>Что произошло с одной задачей за прогон планировщика.</summary>
public sealed record TaskRunEntry(
    Guid TaskId,
    string Title,
    ScheduledTaskStatus FinalStatus,
    ExecutionResult Result,
    string? Message);

/// <summary>Итог одного прогона планировщика. Нужен вызывающему коду для журнала и интерфейса.</summary>
public sealed record RunReport(
    bool AutomationDisabled,
    IReadOnlyList<TaskRunEntry> Entries,
    int RecoveredStuck)
{
    public static RunReport Disabled { get; } = new(true, Array.Empty<TaskRunEntry>(), 0);

    public bool HasActivity => Entries.Count > 0 || RecoveredStuck > 0;
}
