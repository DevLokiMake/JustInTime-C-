namespace JustInTime.Core.Services;

public sealed class SchedulerEngineOptions
{
    /// <summary>Сколько минут опоздания допустимо по умолчанию. Меняется настройкой Scheduler.MissedGraceMinutes.</summary>
    public const int DefaultMissedGraceMinutes = 30;

    public const int MinMissedGraceMinutes = 1;
    public const int MaxMissedGraceMinutes = 24 * 60;

    /// <summary>Сколько максимум может идти одно действие. После этого результат считается неизвестным.</summary>
    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Через сколько задача в статусе Running считается зависшей (приложение закрылось посреди действия).
    /// Должно быть заметно больше ActionTimeout.
    /// </summary>
    public TimeSpan StuckAfter { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Как часто повторять задачи, которые ждут разблокировки или интернета.</summary>
    public TimeSpan WaitingRetryInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Сколько задач берём за один прогон.</summary>
    public int BatchLimit { get; init; } = 50;
}
