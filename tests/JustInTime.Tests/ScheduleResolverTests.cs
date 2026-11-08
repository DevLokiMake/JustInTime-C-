using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

public sealed class ScheduleResolverTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int y, int m, int d, int h, int min) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void After_delay_adds_minutes_to_now()
    {
        var result = ScheduleResolver.Resolve(new AfterDelaySchedule(15), Now, TimeZoneInfo.Utc);

        Assert.True(result.Succeeded);
        Assert.Equal(Now.AddMinutes(15), result.Value!.Utc);
        Assert.Equal(DateTimeKind.Utc, result.Value.Utc.Kind);
        Assert.Equal(TimeZoneInfo.Utc.Id, result.Value.TimeZoneId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ScheduleResolver.MaxDelayMinutes + 1)]
    public void After_delay_outside_allowed_range_is_rejected(int minutes)
    {
        Assert.False(ScheduleResolver.Resolve(new AfterDelaySchedule(minutes), Now, TimeZoneInfo.Utc).Succeeded);
    }

    [Fact]
    public void Missing_schedule_is_rejected()
    {
        Assert.False(ScheduleResolver.Resolve(null, Now, TimeZoneInfo.Utc).Succeeded);
    }

    [Fact]
    public void Local_time_in_future_is_converted_to_utc()
    {
        var local = new DateTime(2026, 11, 6, 13, 0, 0);

        var result = ScheduleResolver.Resolve(new AtLocalTimeSchedule(local), Now, TimeZoneInfo.Utc);

        Assert.True(result.Succeeded);
        Assert.Equal(Utc(2026, 11, 6, 13, 0), result.Value!.Utc);
    }

    [Fact]
    public void Time_in_past_or_exactly_now_is_rejected()
    {
        Assert.False(ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2026, 11, 6, 11, 59, 0)), Now, TimeZoneInfo.Utc).Succeeded);
        Assert.False(ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2026, 11, 6, 12, 0, 0)), Now, TimeZoneInfo.Utc).Succeeded);
    }

    [Fact]
    public void Unknown_time_zone_is_rejected()
    {
        var result = ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2026, 12, 1, 10, 0, 0), "Нет/Такого_Пояса"), Now, TimeZoneInfo.Utc);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Explicit_time_zone_wins_over_device_zone()
    {
        var result = ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2026, 11, 20, 12, 0, 0), TimeZoneInfo.Utc.Id),
            Now,
            TestZones.Berlin);

        Assert.True(result.Succeeded);
        Assert.Equal(Utc(2026, 11, 20, 12, 0), result.Value!.Utc);
        Assert.Equal(TimeZoneInfo.Utc.Id, result.Value.TimeZoneId);
    }

    [Fact]
    public void DateTime_kind_of_input_is_ignored()
    {
        var local = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Local);

        var result = ScheduleResolver.Resolve(new AtLocalTimeSchedule(local), Utc(2026, 6, 1, 0, 0), TestZones.Berlin);

        Assert.True(result.Succeeded);
        Assert.Equal(Utc(2026, 7, 1, 10, 0), result.Value!.Utc);
    }

    // Переход на летнее и зимнее время

    [Fact]
    public void Summer_time_uses_summer_offset()
    {
        var result = ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2026, 7, 1, 12, 0, 0)), Utc(2026, 6, 1, 0, 0), TestZones.Berlin);

        Assert.Equal(Utc(2026, 7, 1, 10, 0), result.Value!.Utc);
    }

    [Fact]
    public void Winter_time_uses_winter_offset()
    {
        var result = ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2027, 1, 15, 12, 0, 0)), Now, TestZones.Berlin);

        Assert.Equal(Utc(2027, 1, 15, 11, 0), result.Value!.Utc);
    }

    [Fact]
    public void Time_skipped_by_spring_forward_is_rejected()
    {
        // 28 марта 2027 в Берлине часы идут с 02:00 сразу на 03:00, времени 02:30 не существует.
        var result = ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2027, 3, 28, 2, 30, 0)), Now, TestZones.Berlin);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Time_repeated_by_fall_back_uses_first_occurrence()
    {
        // 25 октября 2026 в Берлине 02:30 бывает дважды. Берём первое (летнее, UTC+2), то есть 00:30 UTC.
        var result = ScheduleResolver.Resolve(
            new AtLocalTimeSchedule(new DateTime(2026, 10, 25, 2, 30, 0)), Utc(2026, 10, 1, 0, 0), TestZones.Berlin);

        Assert.True(result.Succeeded);
        Assert.Equal(Utc(2026, 10, 25, 0, 30), result.Value!.Utc);
    }
}
