using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

public interface IExecutionHistoryRepository
{
    Task AddAsync(ExecutionHistory entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionHistory>> GetByTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionHistory>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Количество завершённых попыток (FinishedAt заполнено).</summary>
    Task<int> CountFinishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Удаляет завершённые попытки. Идущие сейчас (FinishedAt пусто) остаются. Возвращает количество удалённых.</summary>
    Task<int> DeleteFinishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Последние попытки с одним из указанных итогов (новые первыми). Незавершённые попытки сюда не попадают.</summary>
    Task<IReadOnlyList<ExecutionHistory>> GetRecentByResultAsync(
        IReadOnlyCollection<ExecutionResult> results, int count, CancellationToken cancellationToken = default);
}
