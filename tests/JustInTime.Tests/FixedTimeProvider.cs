namespace JustInTime.Tests;

/// <summary>Часы с заданным временем и часовым поясом, чтобы тесты не зависели от настоящего времени.</summary>
internal sealed class FixedTimeProvider(DateTime nowUtc, TimeZoneInfo? zone = null) : TimeProvider
{
    public DateTime NowUtc { get; set; } = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

    public override DateTimeOffset GetUtcNow() => new(NowUtc);

    public override TimeZoneInfo LocalTimeZone { get; } = zone ?? TimeZoneInfo.Utc;
}

internal static class TestZones
{
    /// <summary>Берлин с переходом на летнее время. На Windows и Linux у зоны разные идентификаторы.</summary>
    public static TimeZoneInfo Berlin { get; } = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Berlin");
}
