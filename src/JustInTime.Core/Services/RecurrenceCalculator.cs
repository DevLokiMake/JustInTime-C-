using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>
/// Считает моменты запуска повторяющейся задачи. Чистая функция: нужны только правило, часовой пояс и время,
/// поэтому результат проверяется тестами без часов и без БД.
/// Время суток держится по местным часам пояса задачи: «каждый день в 09:00» остаётся 09:00 и после перехода
/// на летнее время. Несуществующее время (пропуск при переходе вперёд) сдвигается к ближайшему существующему,
/// двусмысленное (повтор часа при переходе назад) берётся первым вхождением.
/// </summary>
public static class RecurrenceCalculator
{
    private const int MaxSteps = 20000;

    /// <summary>
    /// Первый запуск не раньше startUtc. Нужен при создании: пользователь выбрал дату начала, а правило
    /// (например, «по понедельникам») может требовать другого дня.
    /// </summary>
    public static DateTime? FirstOnOrAfter(RecurrenceRule rule, TimeZoneInfo zone, DateTime startUtc)
    {
        if (rule.Kind == RecurrenceKind.Interval)
        {
            return DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        }

        // Ищем первое вхождение строго позже момента на секунду раньше старта.
        var anchor = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        return Next(rule, zone, anchor.AddSeconds(-1), anchor.AddSeconds(-1));
    }

    /// <summary>
    /// Следующий запуск строго после previousUtc и после nowUtc (пропущенные запуски не нагоняются).
    /// null, если правило некорректно.
    /// </summary>
    public static DateTime? Next(RecurrenceRule rule, TimeZoneInfo zone, DateTime previousUtc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(zone);

        previousUtc = DateTime.SpecifyKind(previousUtc, DateTimeKind.Utc);
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var bound = previousUtc > nowUtc ? previousUtc : nowUtc;

        if (RecurrenceValidator.Validate(rule).Count > 0)
        {
            return null;
        }

        return rule.Kind switch
        {
            RecurrenceKind.Interval => NextInterval(rule, previousUtc, bound),
            RecurrenceKind.Daily => NextDaily(rule, zone, previousUtc, bound),
            RecurrenceKind.Weekly => NextWeekly(rule, zone, previousUtc, bound),
            RecurrenceKind.Monthly => NextMonthly(rule, zone, previousUtc, bound),
            _ => null
        };
    }

    private static DateTime? NextInterval(RecurrenceRule rule, DateTime previousUtc, DateTime bound)
    {
        var step = TimeSpan.FromMinutes(rule.Every);
        var behind = bound - previousUtc;
        var steps = (long)Math.Floor(behind / step) + 1;

        return previousUtc + steps * step;
    }

    private static DateTime? NextDaily(RecurrenceRule rule, TimeZoneInfo zone, DateTime previousUtc, DateTime bound)
    {
        var date = LocalDate(previousUtc, zone);

        for (var i = 1; i <= MaxSteps; i++)
        {
            var candidate = AtLocalTime(date.AddDays(i * rule.Every), rule.TimeOfDayMinutes!.Value, zone);

            if (candidate > bound)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTime? NextWeekly(RecurrenceRule rule, TimeZoneInfo zone, DateTime previousUtc, DateTime bound)
    {
        var date = LocalDate(previousUtc, zone);

        // Первый день ищем с самого предыдущего: время суток у правила своё, и в тот же день оно может оказаться позже.
        for (var i = 0; i <= MaxSteps; i++)
        {
            var day = date.AddDays(i);

            if (!rule.Includes(day.DayOfWeek))
            {
                continue;
            }

            var candidate = AtLocalTime(day, rule.TimeOfDayMinutes!.Value, zone);

            if (candidate > bound)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTime? NextMonthly(RecurrenceRule rule, TimeZoneInfo zone, DateTime previousUtc, DateTime bound)
    {
        var date = LocalDate(previousUtc, zone);
        var firstOfMonth = new DateOnly(date.Year, date.Month, 1);

        for (var i = 0; i <= MaxSteps; i++)
        {
            var month = firstOfMonth.AddMonths(i * rule.Every);
            var day = Math.Min(rule.DayOfMonth!.Value, DateTime.DaysInMonth(month.Year, month.Month));
            var candidate = AtLocalTime(new DateOnly(month.Year, month.Month, day), rule.TimeOfDayMinutes!.Value, zone);

            if (candidate > bound)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

    /// <summary>Местные дата и время суток в UTC с правилами перехода на летнее время.</summary>
    public static DateTime AtLocalTime(DateOnly date, int minutesOfDay, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutesOfDay), DateTimeKind.Unspecified);

        // Пропуск при переходе вперёд: сдвигаем на минуту, пока время не станет существующим.
        var guard = 0;

        while (zone.IsInvalidTime(local) && guard++ < 24 * 60)
        {
            local = local.AddMinutes(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
