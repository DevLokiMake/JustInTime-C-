namespace JustInTime.Core.Models;

public enum RecurrenceKind
{
    /// <summary>Каждые N минут от предыдущего запуска.</summary>
    Interval,

    /// <summary>Каждые N дней в одно и то же местное время.</summary>
    Daily,

    /// <summary>В выбранные дни недели в одно и то же местное время.</summary>
    Weekly,

    /// <summary>Каждые N месяцев в выбранный день месяца (в коротких месяцах последний день).</summary>
    Monthly
}

/// <summary>
/// Правило повтора. Хранится в задаче JSON-ом и не зависит от текущего времени: по правилу, часовому поясу и
/// предыдущему запуску всегда однозначно считается следующий (RecurrenceCalculator).
/// </summary>
/// <param name="Kind">Вид повтора.</param>
/// <param name="Every">Интервал: минуты (Interval), дни (Daily), месяцы (Monthly). Для Weekly всегда 1.</param>
/// <param name="DaysOfWeekMask">Для Weekly: бит i установлен, если выполнять в день недели i (0 = воскресенье, как в DayOfWeek).</param>
/// <param name="DayOfMonth">Для Monthly: число месяца 1-31. При вводе пользователя может быть null, тогда берётся число первого запуска.</param>
/// <param name="TimeOfDayMinutes">Местное время суток в минутах от полуночи. При вводе пользователя может быть null, тогда берётся время первого запуска. Для Interval не используется.</param>
public sealed record RecurrenceRule(
    RecurrenceKind Kind,
    int Every = 1,
    int DaysOfWeekMask = 0,
    int? DayOfMonth = null,
    int? TimeOfDayMinutes = null)
{
    public const int MinIntervalMinutes = 5;
    public const int MaxIntervalMinutes = 60 * 24 * 7;
    public const int MaxEveryDays = 365;
    public const int MaxEveryMonths = 12;

    public static int MaskOf(params DayOfWeek[] days) => days.Aggregate(0, (mask, day) => mask | (1 << (int)day));

    public bool Includes(DayOfWeek day) => (DaysOfWeekMask & (1 << (int)day)) != 0;
}
