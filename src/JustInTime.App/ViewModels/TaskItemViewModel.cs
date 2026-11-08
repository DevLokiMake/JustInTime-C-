using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.App.ViewModels;

/// <summary>Строка списка задач. Только для показа, поэтому без уведомлений об изменениях.</summary>
public sealed class TaskItemViewModel
{
    public TaskItemViewModel(ScheduledTask task)
    {
        Id = task.Id;
        Title = task.Title;
        ActionName = UiNames.Action(task.ActionType);
        Summary = UiNames.Summary(TaskPayloadSerializer.Deserialize(task.ActionType, task.Payload));
        WhenText = task.ScheduledAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        StatusText = task.Status.ToDisplayName();
        ErrorText = task.LastError;
        AttemptsText = task.AttemptCount > 0 ? $"  ·  Попыток: {task.AttemptCount}" : string.Empty;

        IsProblem = task.Status is ScheduledTaskStatus.Failed or ScheduledTaskStatus.NeedsUserAction;
        CanEdit = ScheduledTaskStatuses.Editable.Contains(task.Status);
        CanCancel = CanEdit;
        CanRetry = ScheduledTaskStatuses.Retryable.Contains(task.Status);
        RetryNeedsDuplicateWarning = task.Status == ScheduledTaskStatus.NeedsUserAction;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string ActionName { get; }

    public string Summary { get; }

    public string WhenText { get; }

    public string StatusText { get; }

    public string? ErrorText { get; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string AttemptsText { get; }

    public bool IsProblem { get; }

    public bool CanEdit { get; }

    public bool CanCancel { get; }

    public bool CanRetry { get; }

    /// <summary>Результат прошлой попытки неизвестен, повтор может создать дубль (например, второе сообщение).</summary>
    public bool RetryNeedsDuplicateWarning { get; }
}
