namespace JustInTime.Core.Models;

/// <summary>Новые значения полей задачи при редактировании. Время только в UTC.</summary>
public sealed record TaskUpdate(
    string Title,
    DateTime ScheduledAtUtc,
    string TimeZone,
    string Payload,
    bool IsRecurring);
