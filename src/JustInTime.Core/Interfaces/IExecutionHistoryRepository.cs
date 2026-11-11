using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

public interface IExecutionHistoryRepository
{
    Task AddAsync(ExecutionHistory entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionHistory>> GetByTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionHistory>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>Последние попытки с одним из указанных итогов (новые первыми). Незавершённые попытки сюда не попадают.</summary>
    Task<IReadOnlyList<ExecutionHistory>> GetRecentByResultAsync(
        IReadOnlyCollection<ExecutionResult> results, int count, CancellationToken cancellationToken = default);
}
