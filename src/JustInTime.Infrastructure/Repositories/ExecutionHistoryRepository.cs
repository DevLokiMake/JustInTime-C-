using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace JustInTime.Infrastructure.Repositories;

public sealed class ExecutionHistoryRepository(IDbContextFactory<JustInTimeDbContext> contextFactory)
    : IExecutionHistoryRepository
{
    public async Task AddAsync(ExecutionHistory entry, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.ExecutionHistories.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionHistory>> GetByTaskAsync(
        Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExecutionHistories
            .AsNoTracking()
            .Where(h => h.TaskId == taskId)
            .OrderByDescending(h => h.StartedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionHistory>> GetRecentAsync(
        int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExecutionHistories
            .AsNoTracking()
            .Include(h => h.ScheduledTask)
            .OrderByDescending(h => h.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionHistory>> GetRecentByResultAsync(
        IReadOnlyCollection<ExecutionResult> results, int count, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var wanted = results.Select(r => (ExecutionResult?)r).ToArray();

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExecutionHistories
            .AsNoTracking()
            .Include(h => h.ScheduledTask)
            .Where(h => wanted.Contains(h.Result))
            .OrderByDescending(h => h.StartedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
