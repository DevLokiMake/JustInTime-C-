using JustInTime.Core.Models;

namespace JustInTime.App.ViewModels;

/// <summary>Одна строка журнала: попытка выполнения задачи, подготовленная для показа.</summary>
public sealed record HistoryEntryViewModel(
    string Title,
    string WhenText,
    string ResultText,
    string DurationText,
    string AttemptText,
    string? ErrorText,
    bool IsProblem)
{
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public static HistoryEntryViewModel From(ExecutionHistory entry)
    {
        var (result, problem) = entry.Result switch
        {
            null => ("Выполняется", false),
            ExecutionResult.Success => ("Успешно", false),
            ExecutionResult.Failed => ("Ошибка", true),
            ExecutionResult.Unknown => ("Результат неизвестен", true),
            ExecutionResult.Skipped => ("Пропущено", true),
            _ => (entry.Result.ToString()!, false)
        };

        return new HistoryEntryViewModel(
            entry.ScheduledTask?.Title ?? "Задача удалена",
            entry.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            result,
            FormatDuration(entry),
            $"Попытка {entry.AttemptNumber}",
            entry.ErrorMessage,
            problem);
    }

    private static string FormatDuration(ExecutionHistory entry)
    {
        if (entry.FinishedAt is not { } finished)
        {
            return "ещё не завершена";
        }

        var span = finished - entry.StartedAt;

        if (span < TimeSpan.FromSeconds(1))
        {
            return "меньше секунды";
        }

        return span < TimeSpan.FromMinutes(1)
            ? $"{(int)span.TotalSeconds} с"
            : $"{(int)span.TotalMinutes} мин {span.Seconds} с";
    }
}
