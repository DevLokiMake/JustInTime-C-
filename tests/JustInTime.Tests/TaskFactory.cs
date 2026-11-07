using JustInTime.Core.Models;

namespace JustInTime.Tests;

internal static class TestData
{
    /// <summary>Фиксированное «сейчас», чтобы тесты не зависели от часов.</summary>
    public static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    public static ScheduledTask NewTask(
        DateTime scheduledAtUtc,
        ScheduledTaskStatus status = ScheduledTaskStatus.Pending,
        string title = "Тестовая задача") => new()
    {
        Title = title,
        ActionType = ActionType.SendMessage,
        ScheduledAt = scheduledAtUtc,
        TimeZone = "Asia/Qyzylorda",
        Status = status,
        Payload = """{"recipient":"Анна","text":"Привет"}"""
    };
}
