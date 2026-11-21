using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.App.ViewModels;

/// <summary>Строка списка запланированных сообщений. Только для показа.</summary>
public sealed class MessageItemViewModel
{
    private const int PreviewLength = 140;

    public MessageItemViewModel(ScheduledTask task)
    {
        Id = task.Id;
        Title = task.Title;

        var payload = TaskPayloadSerializer.Deserialize(task.ActionType, task.Payload) as SendMessagePayload;

        AppName = payload is null ? "?" : UiNames.Messenger(payload.App);
        Recipient = payload?.Recipient ?? "не удалось прочитать";
        TextPreview = payload is null ? string.Empty : Preview(payload.Text);

        WhenText = task.ScheduledAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        StatusText = task.Status.ToDisplayName();
        ErrorText = task.LastError;

        var rule = task.IsRecurring ? RecurrenceSerializer.Deserialize(task.Recurrence) : null;
        RepeatText = rule is null ? null : RecurrenceDescriber.Describe(rule);

        NeedsResolve = task.Status == ScheduledTaskStatus.NeedsUserAction;
        IsProblem = NeedsResolve || task.Status == ScheduledTaskStatus.Failed;
        CanEdit = ScheduledTaskStatuses.Editable.Contains(task.Status);
        CanCancel = CanEdit;
        CanRetry = task.Status == ScheduledTaskStatus.Failed;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string AppName { get; }

    public string Recipient { get; }

    public string TextPreview { get; }

    public string WhenText { get; }

    public string StatusText { get; }

    public string? ErrorText { get; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string? RepeatText { get; }

    public bool IsRepeating => RepeatText is not null;

    public string WhenLine => Loc.T("label_when", WhenText);

    public string RepeatLine => RepeatText is null ? string.Empty : Loc.T("label_repeat", RepeatText);

    public bool IsProblem { get; }

    public bool CanEdit { get; }

    public bool CanCancel { get; }

    /// <summary>Повтор только для Failed: здесь точно известно, что сообщение не ушло, дубля быть не может.</summary>
    public bool CanRetry { get; }

    /// <summary>Результат неизвестен: пользователь должен проверить чат и подтвердить, ушло ли сообщение.</summary>
    public bool NeedsResolve { get; }

    private static string Preview(string text)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= PreviewLength ? oneLine : oneLine[..PreviewLength] + "…";
    }
}
