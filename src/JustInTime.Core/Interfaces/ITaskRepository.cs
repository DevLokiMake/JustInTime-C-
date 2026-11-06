using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

public interface ITaskRepository
{
    Task AddAsync(ScheduledTask task, CancellationToken cancellationToken = default);

    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    Task<int> CountByStatusAsync(ScheduledTaskStatus status, CancellationToken cancellationToken = default);

    /// <summary>Ближайшая задача, которая ещё ждёт запуска.</summary>
    Task<ScheduledTask?> GetNextScheduledAsync(CancellationToken cancellationToken = default);
}
