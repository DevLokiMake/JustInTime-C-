namespace JustInTime.Core.Models;

/// <summary>Итог одной попытки выполнения задачи.</summary>
public enum ExecutionResult
{
    Success,
    Failed,

    /// <summary>
    /// Результат неизвестен (например, не удалось подтвердить отправку сообщения).
    /// Такую задачу нельзя повторять автоматически, чтобы не создать дубль.
    /// </summary>
    Unknown,

    Skipped
}
