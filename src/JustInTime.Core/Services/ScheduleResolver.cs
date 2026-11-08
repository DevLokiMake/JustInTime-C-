using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Время запуска в UTC и часовой пояс, в котором пользователь его задал.</summary>
public sealed record ResolvedSchedule(DateTime Utc, string TimeZoneId);

/// <summary>Превращает ввод пользователя в момент запуска в UTC с учётом часового пояса и перехода на летнее время.</summary>
public static class ScheduleResolver
{
    public const int MaxDelayMinutes = 60 * 24 * 365;

    public static ServiceResult<ResolvedSchedule> Resolve(
        TaskSchedule? schedule, DateTime nowUtc, TimeZoneInfo localZone)
    {
        ArgumentNullException.ThrowIfNull(localZone);
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

        switch (schedule)
        {
            case AfterDelaySchedule delay:
                if (delay.Minutes < 1 || delay.Minutes > MaxDelayMinutes)
                {
                    return ServiceResult<ResolvedSchedule>.Fail(
                        $"Задержка должна быть от 1 до {MaxDelayMinutes} минут.");
                }

                return ServiceResult<ResolvedSchedule>.Ok(
                    new ResolvedSchedule(nowUtc.AddMinutes(delay.Minutes), localZone.Id));

            case AtLocalTimeSchedule atTime:
                return ResolveLocalTime(atTime, nowUtc, localZone);

            default:
                return ServiceResult<ResolvedSchedule>.Fail("Не указано время выполнения.");
        }
    }

    private static ServiceResult<ResolvedSchedule> ResolveLocalTime(
        AtLocalTimeSchedule schedule, DateTime nowUtc, TimeZoneInfo localZone)
    {
        var zone = localZone;

        if (!string.IsNullOrWhiteSpace(schedule.TimeZoneId))
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return ServiceResult<ResolvedSchedule>.Fail($"Неизвестный часовой пояс: {schedule.TimeZoneId}.");
            }
        }

        var local = DateTime.SpecifyKind(schedule.LocalTime, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            return ServiceResult<ResolvedSchedule>.Fail(
                "Этого времени не существует из-за перехода на летнее время. Выберите другое.");
        }

        DateTime utc;

        if (zone.IsAmbiguousTime(local))
        {
            // Время повторяется дважды при переходе на зимнее. Берём первое вхождение (летнее смещение).
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            utc = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }
        else
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        if (utc <= nowUtc)
        {
            return ServiceResult<ResolvedSchedule>.Fail("Время выполнения должно быть в будущем.");
        }

        return ServiceResult<ResolvedSchedule>.Ok(new ResolvedSchedule(utc, zone.Id));
    }
}
