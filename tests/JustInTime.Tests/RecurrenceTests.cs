using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

/// <summary>Расчёт повторов без БД и без часов: правило, часовой пояс и время подаются явно.</summary>
public sealed class RecurrenceTests
{
    // UTC+5 без перехода на летнее время, как в Караганде.
    private static readonly TimeZoneInfo Karaganda =
        TimeZoneInfo.CreateCustomTimeZone("KRG", TimeSpan.FromHours(5), "KRG", "KRG");

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static RecurrenceRule Daily(int minutes, int every = 1) =>
        new(RecurrenceKind.Daily, every, TimeOfDayMinutes: minutes);

    [Fact]
    public void Daily_keeps_local_time_across_spring_forward()
    {
        // Берлин 28.03.2026 09:00 = 08:00 UTC (зима). 29.03 в 02:00 часы идут на 03:00. 09:00 становится 07:00 UTC.
        var prev = Utc(2026, 3, 28, 8);

        var next = RecurrenceCalculator.Next(Daily(9 * 60), TestZones.Berlin, prev, prev);

        Assert.Equal(Utc(2026, 3, 29, 7), next);
    }

    [Fact]
    public void Time_inside_the_spring_gap_moves_to_the_first_valid_time()
    {
        // 02:30 29.03.2026 в Берлине не существует. Сдвиг до 03:00 CEST = 01:00 UTC.
        var prev = Utc(2026, 3, 28, 1, 30);

        var next = RecurrenceCalculator.Next(Daily(2 * 60 + 30), TestZones.Berlin, prev, prev);

        Assert.Equal(Utc(2026, 3, 29, 1), next);
    }

    [Fact]
    public void Ambiguous_time_in_autumn_takes_the_first_occurrence()
    {
        // 02:30 25.10.2026 бывает дважды (CEST и CET). Берём первое, летнее: 00:30 UTC.
        var prev = Utc(2026, 10, 24, 0, 30);

        var next = RecurrenceCalculator.Next(Daily(2 * 60 + 30), TestZones.Berlin, prev, prev);

        Assert.Equal(Utc(2026, 10, 25, 0, 30), next);
    }

    [Fact]
    public void Daily_does_not_catch_up_missed_days()
    {
        var prev = Utc(2026, 11, 1, 4);              // 09:00 по Караганде
        var now = Utc(2026, 11, 10, 8);              // 13:00 10 ноября

        var next = RecurrenceCalculator.Next(Daily(9 * 60), Karaganda, prev, now);

        Assert.Equal(Utc(2026, 11, 11, 4), next);
    }

    [Fact]
    public void Every_three_days_steps_by_three()
    {
        var prev = Utc(2026, 11, 1, 4);

        var next = RecurrenceCalculator.Next(Daily(9 * 60, every: 3), Karaganda, prev, prev);

        Assert.Equal(Utc(2026, 11, 4, 4), next);
    }

    [Fact]
    public void Weekly_picks_the_next_selected_weekday_and_wraps_to_next_week()
    {
        var rule = new RecurrenceRule(
            RecurrenceKind.Weekly, DaysOfWeekMask: RecurrenceRule.MaskOf(DayOfWeek.Monday, DayOfWeek.Wednesday),
            TimeOfDayMinutes: 10 * 60);
        var monday = Utc(2026, 11, 2, 5);            // понедельник 10:00 по Караганде

        var wednesday = RecurrenceCalculator.Next(rule, Karaganda, monday, monday);
        var nextMonday = RecurrenceCalculator.Next(rule, Karaganda, wednesday!.Value, wednesday.Value);

        Assert.Equal(Utc(2026, 11, 4, 5), wednesday);
        Assert.Equal(Utc(2026, 11, 9, 5), nextMonday);
    }

    [Fact]
    public void Monthly_day_31_uses_the_last_day_of_short_months_without_drifting()
    {
        var rule = new RecurrenceRule(RecurrenceKind.Monthly, DayOfMonth: 31, TimeOfDayMinutes: 10 * 60);
        var january = Utc(2026, 1, 31, 5);

        var february = RecurrenceCalculator.Next(rule, Karaganda, january, january);
        var march = RecurrenceCalculator.Next(rule, Karaganda, february!.Value, february.Value);

        Assert.Equal(Utc(2026, 2, 28, 5), february);
        Assert.Equal(Utc(2026, 3, 31, 5), march);
    }

    [Fact]
    public void Interval_skips_missed_slots_and_stays_on_the_grid()
    {
        var rule = new RecurrenceRule(RecurrenceKind.Interval, Every: 30);
        var prev = Utc(2026, 11, 6, 10);
        var now = Utc(2026, 11, 6, 10, 50);

        var next = RecurrenceCalculator.Next(rule, Karaganda, prev, now);

        Assert.Equal(Utc(2026, 11, 6, 11), next);
    }

    [Fact]
    public void First_run_moves_to_the_matching_weekday()
    {
        var rule = new RecurrenceRule(
            RecurrenceKind.Weekly, DaysOfWeekMask: RecurrenceRule.MaskOf(DayOfWeek.Monday), TimeOfDayMinutes: 10 * 60);
        var tuesday = Utc(2026, 11, 3, 5);

        var first = RecurrenceCalculator.FirstOnOrAfter(rule, Karaganda, tuesday);

        Assert.Equal(Utc(2026, 11, 9, 5), first);
    }

    [Fact]
    public void First_run_keeps_the_chosen_moment_when_it_already_matches()
    {
        var rule = new RecurrenceRule(
            RecurrenceKind.Weekly, DaysOfWeekMask: RecurrenceRule.MaskOf(DayOfWeek.Monday), TimeOfDayMinutes: 10 * 60);
        var monday = Utc(2026, 11, 2, 5);

        Assert.Equal(monday, RecurrenceCalculator.FirstOnOrAfter(rule, Karaganda, monday));
    }

    [Fact]
    public void Invalid_rule_gives_no_next_run()
    {
        var empty = new RecurrenceRule(RecurrenceKind.Weekly, DaysOfWeekMask: 0, TimeOfDayMinutes: 600);

        Assert.Null(RecurrenceCalculator.Next(empty, Karaganda, Utc(2026, 11, 2), Utc(2026, 11, 2)));
    }

    [Theory]
    [InlineData(RecurrenceKind.Interval, 3)]
    [InlineData(RecurrenceKind.Interval, 20000)]
    [InlineData(RecurrenceKind.Daily, 0)]
    [InlineData(RecurrenceKind.Daily, 366)]
    [InlineData(RecurrenceKind.Monthly, 13)]
    public void Out_of_range_intervals_are_rejected(RecurrenceKind kind, int every)
    {
        var rule = new RecurrenceRule(kind, every, DayOfMonth: 5, TimeOfDayMinutes: 600);

        Assert.True(RecurrenceValidator.Validate(rule).Count > 0);
    }

    [Fact]
    public void Incomplete_input_is_allowed_only_before_the_service_completes_it()
    {
        var input = new RecurrenceRule(RecurrenceKind.Monthly);

        Assert.Empty(RecurrenceValidator.Validate(input, requireComplete: false));
        Assert.True(RecurrenceValidator.Validate(input).Count > 0);
    }

    [Fact]
    public void Rule_survives_json_round_trip_and_corrupted_json_gives_null()
    {
        var rule = new RecurrenceRule(
            RecurrenceKind.Weekly, DaysOfWeekMask: RecurrenceRule.MaskOf(DayOfWeek.Friday), TimeOfDayMinutes: 75);

        Assert.Equal(rule, RecurrenceSerializer.Deserialize(RecurrenceSerializer.Serialize(rule)));
        Assert.Null(RecurrenceSerializer.Deserialize("{ не json"));
        Assert.Null(RecurrenceSerializer.Deserialize(null));
    }

    [Fact]
    public void Describer_writes_readable_text()
    {
        Assert.Equal("Каждый день в 09:00", RecurrenceDescriber.Describe(Daily(9 * 60)));
        Assert.Equal("Каждые 30 мин", RecurrenceDescriber.Describe(new RecurrenceRule(RecurrenceKind.Interval, 30)));
        Assert.Equal("Каждые 2 ч", RecurrenceDescriber.Describe(new RecurrenceRule(RecurrenceKind.Interval, 120)));
        Assert.Equal(
            "По дням: пн, ср в 10:00",
            RecurrenceDescriber.Describe(new RecurrenceRule(
                RecurrenceKind.Weekly,
                DaysOfWeekMask: RecurrenceRule.MaskOf(DayOfWeek.Wednesday, DayOfWeek.Monday),
                TimeOfDayMinutes: 600)));
    }
}
