using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

public interface IExecutionHistoryRepository
{
    Task AddAsync(ExecutionHistory entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionHistory>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExecutionHistory>> GetRecentFailuresAsync(int count, CancellationToken cancellationToken = default);
}
