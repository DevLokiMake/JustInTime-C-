using JustInTime.Core.Localization;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Короткое описание правила для интерфейса: «Каждый день в 09:00». Язык берётся из текущего языка интерфейса.</summary>
public static class RecurrenceDescriber
{
    // Порядок показа: с понедельника.
    private static readonly DayOfWeek[] WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    ];

    public static string Describe(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var time = rule.TimeOfDayMinutes is { } t ? $"{t / 60:00}:{t % 60:00}" : "?";

        return rule.Kind switch
        {
            RecurrenceKind.Interval => rule.Every % 60 == 0 && rule.Every >= 60
                ? Loc.T("rec_every_hours", rule.Every / 60)
                : Loc.T("rec_every_minutes", rule.Every),
            RecurrenceKind.Daily => rule.Every == 1 ? Loc.T("rec_daily_1", time) : Loc.T("rec_daily_n", rule.Every, time),
            RecurrenceKind.Weekly => Loc.T(
                "rec_weekly",
                string.Join(", ", WeekOrder.Where(rule.Includes).Select(d => Loc.T("wds_" + (int)d))),
                time),
            RecurrenceKind.Monthly => rule.Every == 1
                ? Loc.T("rec_monthly_1", rule.DayOfMonth, time)
                : Loc.T("rec_monthly_n", rule.Every, rule.DayOfMonth, time),
            _ => string.Empty
        };
    }
}
