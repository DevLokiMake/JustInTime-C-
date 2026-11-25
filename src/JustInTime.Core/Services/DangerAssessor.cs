using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>
/// Решает, какие операции требуют подтверждения. Подтверждение нужно только для массовых и необратимых операций:
/// обычное создание, изменение и одиночная отмена задачи, включение и выключение Bluetooth, Wi-Fi, смена громкости,
/// открытие ссылки и разовая отправка сообщения подтверждения не требуют (их можно исправить или они ожидаемы).
/// </summary>
public static class DangerAssessor
{
    /// <summary>С скольких задач отмена считается массовой.</summary>
    public const int BulkThreshold = 2;

    public static IReadOnlyList<DangerReason> ForTask(TaskPayload? payload, bool recurring)
    {
        var reasons = new List<DangerReason>();

        switch (payload)
        {
            case LaunchProgramPayload:
                reasons.Add(new DangerReason(DangerKind.LaunchProgram));
                break;
            case SendMessagePayload when recurring:
                reasons.Add(new DangerReason(DangerKind.RecurringMessage));
                break;
        }

        return reasons;
    }

    public static IReadOnlyList<DangerReason> ForCancelMany(int count) =>
        count >= BulkThreshold ? [new DangerReason(DangerKind.CancelMany, count)] : [];

    public static IReadOnlyList<DangerReason> ForClearHistory(int entries) =>
        entries > 0 ? [new DangerReason(DangerKind.ClearHistory, entries)] : [];
}

/// <summary>Политика подтверждения: спрашивать, если есть опасные причины и не указан --force / -y.</summary>
public static class ConfirmationPolicy
{
    public static bool NeedsConfirmation(IReadOnlyCollection<DangerReason> reasons, bool force) =>
        reasons.Count > 0 && !force;
}
