using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Короткое описание правила для интерфейса: «Каждый день в 09:00».</summary>
public static class RecurrenceDescriber
{
    private static readonly string[] ShortDays = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];

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
                ? $"Каждые {rule.Every / 60} ч"
                : $"Каждые {rule.Every} мин",
            RecurrenceKind.Daily => rule.Every == 1 ? $"Каждый день в {time}" : $"Каждые {rule.Every} дн. в {time}",
            RecurrenceKind.Weekly =>
                $"По дням: {string.Join(", ", WeekOrder.Where(rule.Includes).Select(d => ShortDays[(int)d]))} в {time}",
            RecurrenceKind.Monthly => rule.Every == 1
                ? $"Каждый месяц, {rule.DayOfMonth}-го в {time}"
                : $"Каждые {rule.Every} мес., {rule.DayOfMonth}-го в {time}",
            _ => string.Empty
        };
    }
}
