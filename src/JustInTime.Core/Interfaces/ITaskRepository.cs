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
}
