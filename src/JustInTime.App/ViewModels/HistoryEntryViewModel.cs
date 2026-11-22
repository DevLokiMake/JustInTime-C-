using JustInTime.Core.Models;
using JustInTime.Core.Localization;

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
            null => (Loc.T("res_running_cap"), false),
            ExecutionResult.Success => (Loc.T("res_success_cap"), false),
            ExecutionResult.Failed => (Loc.T("res_failed_cap"), true),
            ExecutionResult.Unknown => (Loc.T("res_unknown_cap"), true),
            ExecutionResult.Skipped => (Loc.T("res_skipped_cap"), true),
            _ => (entry.Result.ToString()!, false)
        };

        return new HistoryEntryViewModel(
            entry.ScheduledTask?.Title ?? Loc.T("task_deleted"),
            entry.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            result,
            FormatDuration(entry),
            Loc.T("attempt_n", entry.AttemptNumber),
            entry.ErrorMessage,
            problem);
    }

    private static string FormatDuration(ExecutionHistory entry)
    {
        if (entry.FinishedAt is not { } finished)
        {
            return Loc.T("dur_unfinished");
        }

        var span = finished - entry.StartedAt;

        if (span < TimeSpan.FromSeconds(1))
        {
            return Loc.T("dur_under_second");
        }

        return span < TimeSpan.FromMinutes(1)
            ? Loc.T("dur_seconds", (int)span.TotalSeconds)
            : Loc.T("dur_min_sec", (int)span.TotalMinutes, span.Seconds);
    }
}
