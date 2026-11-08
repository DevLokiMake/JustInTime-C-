using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Core.Interfaces;

/// <summary>Управление задачами: создание, изменение, отмена, повтор.</summary>
public interface ITaskService
{
    Task<ServiceResult<ScheduledTask>> CreateAsync(TaskInput input, CancellationToken cancellationToken = default);

    /// <summary>Меняет название, параметры и время задачи. Тип действия изменить нельзя.</summary>
    Task<ServiceResult> UpdateAsync(Guid id, TaskInput input, CancellationToken cancellationToken = default);

    Task<ServiceResult> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Возвращает задачу с ошибкой или неизвестным результатом в очередь на немедленный запуск.</summary>
    Task<ServiceResult> RetryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ScheduledTask?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScheduledTask>> GetListAsync(
        IReadOnlyCollection<ScheduledTaskStatus>? statuses = null,
        CancellationToken cancellationToken = default);
}
