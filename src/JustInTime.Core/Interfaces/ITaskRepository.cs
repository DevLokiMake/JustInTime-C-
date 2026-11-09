using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

/// <summary>
/// Доступ к задачам. Операции Try* и Finish* атомарны на уровне БД: условие и изменение
/// выполняются одним запросом, поэтому две конкурирующие попытки не могут обе «выиграть».
/// </summary>
public interface ITaskRepository
{
    Task AddAsync(ScheduledTask task, CancellationToken cancellationToken = default);

    Task<ScheduledTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Задачи с указанными статусами (или все, если null), по возрастанию времени запуска.</summary>
    Task<IReadOnlyList<ScheduledTask>> GetListAsync(
        IReadOnlyCollection<ScheduledTaskStatus>? statuses = null,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    Task<int> CountByStatusAsync(ScheduledTaskStatus status, CancellationToken cancellationToken = default);

    /// <summary>Ближайшая задача, которая ещё ждёт запуска.</summary>
    Task<ScheduledTask?> GetNextScheduledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Меняет задачу и возвращает её в Pending (перепланирование).
    /// Возвращает false, если задачи нет или её статус не позволяет редактирование.
    /// </summary>
    Task<bool> TryUpdateAsync(
        Guid id, TaskUpdate update, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Отменяет задачу. Возвращает false, если задачи нет или она уже завершена либо выполняется.
    /// </summary>
    Task<bool> TryCancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает задачу со статусом Failed или NeedsUserAction в Pending с новым временем запуска.
    /// Возвращает false, если задачи нет или её статус не позволяет повтор.
    /// </summary>
    Task<bool> TryRetryAsync(
        Guid id, DateTime scheduledAtUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно переводит задачу в Running, увеличивает счётчик попыток и создаёт запись истории.
    /// Возвращает запись истории, либо null, если запустить нельзя: время не наступило,
    /// статус не позволяет запуск или задачу уже взял другой исполнитель.
    /// </summary>
    Task<ExecutionHistory?> TryStartExecutionAsync(
        Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Завершает попытку: записывает результат в историю и переводит задачу из Running в newStatus.
    /// Возвращает false, если попытка уже завершена или задача не в статусе Running.
    /// </summary>
    Task<bool> FinishExecutionAsync(
        Guid historyId,
        ExecutionResult result,
        string? errorMessage,
        ScheduledTaskStatus newStatus,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Задачи, которые пора запускать: время наступило и статус позволяет запуск. Задачи в статусах ожидания
    /// берутся не чаще, чем раз в интервал (их UpdatedAt должен быть не позже waitingRetryBeforeUtc).
    /// Порядок по времени запуска.
    /// </summary>
    Task<IReadOnlyList<ScheduledTask>> GetDueAsync(
        DateTime nowUtc, DateTime waitingRetryBeforeUtc, int limit, CancellationToken cancellationToken = default);

    /// <summary>Pending → Scheduled после успешной регистрации в системном планировщике. false, если статус уже другой.</summary>
    Task<bool> TryMarkScheduledAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Помечает пропущенную задачу как Failed и пишет в историю запись с результатом Skipped.
    /// false, если задачу уже взял другой исполнитель или статус не позволяет запуск.
    /// </summary>
    Task<bool> TryMarkMissedAsync(
        Guid id, string message, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Задачи в статусе Running, которые не обновлялись с cutoffUtc, переводит в NeedsUserAction
    /// и закрывает их открытые попытки с результатом Unknown. Возвращает количество задач.
    /// </summary>
    Task<int> RecoverStuckAsync(
        DateTime cutoffUtc, DateTime nowUtc, string message, CancellationToken cancellationToken = default);
}
