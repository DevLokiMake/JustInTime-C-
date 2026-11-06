using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace JustInTime.Infrastructure.Repositories;

public sealed class TaskRepository(IDbContextFactory<JustInTimeDbContext> contextFactory) : ITaskRepository
{
    public async Task AddAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var active = ScheduledTaskStatuses.Active;
        return await db.Tasks.CountAsync(t => active.Contains(t.Status), cancellationToken);
    }

    public async Task<int> CountByStatusAsync(ScheduledTaskStatus status, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.CountAsync(t => t.Status == status, cancellationToken);
    }

    public async Task<ScheduledTask?> GetNextScheduledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var upcoming = ScheduledTaskStatuses.Upcoming;
        return await db.Tasks
            .AsNoTracking()
            .Where(t => upcoming.Contains(t.Status))
            .OrderBy(t => t.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
