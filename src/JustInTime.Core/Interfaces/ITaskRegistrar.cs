using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

/// <summary>
/// Системный запуск задач. На Windows это Планировщик заданий: он запускает приложение в нужное время,
/// даже если окно закрыто. Ни один метод не бросает исключения при сбое системы: причина возвращается в результате.
/// </summary>
public interface ITaskRegistrar
{
    /// <summary>Создаёт или перезаписывает системный запуск для задачи.</summary>
    Task<RegistrationResult> RegisterAsync(ScheduledTask task, CancellationToken cancellationToken = default);

    /// <summary>Удаляет системный запуск задачи. Если его нет, это тоже успех.</summary>
    Task<RegistrationResult> UnregisterAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Приводит системные запуски в соответствие с БД: регистрирует все задачи из toRegister
    /// и, если removeStale, удаляет запуски, которых нет ни в toRegister, ни в keepIds.
    /// </summary>
    Task<ReconcileResult> ReconcileAsync(
        IReadOnlyCollection<ScheduledTask> toRegister,
        IReadOnlyCollection<Guid> keepIds,
        bool removeStale,
        CancellationToken cancellationToken = default);
}
