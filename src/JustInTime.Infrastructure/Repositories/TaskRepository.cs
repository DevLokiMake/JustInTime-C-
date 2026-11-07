using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace JustInTime.Infrastructure.Repositories;

public sealed class TaskRepository(IDbContextFactory<JustInTimeDbContext> contextFactory) : ITaskRepository
{
    // Должны совпадать с HasMaxLength в конфигурациях сущностей.
    private const int MaxTaskErrorLength = 2000;
    private const int MaxHistoryErrorLength = 4000;

    public async Task AddAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ScheduledTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetListAsync(
        IReadOnlyCollection<ScheduledTaskStatus>? statuses = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Tasks.AsNoTracking();

        if (statuses is not null)
        {
            var filter = statuses.ToArray();
            query = query.Where(t => filter.Contains(t.Status));
        }

        return await query.OrderBy(t => t.ScheduledAt).ToListAsync(cancellationToken);
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

    public async Task<bool> TryUpdateAsync(
        Guid id, TaskUpdate update, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var editable = ScheduledTaskStatuses.Editable;

        var affected = await db.Tasks
            .Where(t => t.Id == id && editable.Contains(t.Status))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(t => t.Title, update.Title)
                    .SetProperty(t => t.ScheduledAt, update.ScheduledAtUtc)
                    .SetProperty(t => t.TimeZone, update.TimeZone)
                    .SetProperty(t => t.Payload, update.Payload)
                    .SetProperty(t => t.IsRecurring, update.IsRecurring)
                    .SetProperty(t => t.Status, ScheduledTaskStatus.Pending)
                    .SetProperty(t => t.LastError, (string?)null)
                    .SetProperty(t => t.UpdatedAt, nowUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task<bool> TryCancelAsync(Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var cancellable = ScheduledTaskStatuses.Editable;

        var affected = await db.Tasks
            .Where(t => t.Id == id && cancellable.Contains(t.Status))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(t => t.Status, ScheduledTaskStatus.Cancelled)
                    .SetProperty(t => t.UpdatedAt, nowUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task<ExecutionHistory?> TryStartExecutionAsync(
        Guid id, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var startable = ScheduledTaskStatuses.Startable;

        // Условие и смена статуса в одном UPDATE. Если два исполнителя придут одновременно,
        // PostgreSQL заблокирует строку для второго, и после коммита первого его условие
        // (статус из списка Startable) уже не выполнится: затронуто 0 строк.
        var claimed = await db.Tasks
            .Where(t => t.Id == id && startable.Contains(t.Status) && t.ScheduledAt <= nowUtc)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(t => t.Status, ScheduledTaskStatus.Running)
                    .SetProperty(t => t.AttemptCount, t => t.AttemptCount + 1)
                    .SetProperty(t => t.LastExecutedAt, (DateTime?)nowUtc)
                    .SetProperty(t => t.UpdatedAt, nowUtc),
                cancellationToken);

        if (claimed == 0)
        {
            return null;
        }

        var attempt = await db.Tasks
            .Where(t => t.Id == id)
            .Select(t => t.AttemptCount)
            .SingleAsync(cancellationToken);

        var entry = new ExecutionHistory
        {
            TaskId = id,
            StartedAt = nowUtc,
            AttemptNumber = attempt
        };

        db.ExecutionHistories.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entry;
    }

    public async Task<bool> FinishExecutionAsync(
        Guid historyId,
        ExecutionResult result,
        string? errorMessage,
        ScheduledTaskStatus newStatus,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (newStatus == ScheduledTaskStatus.Running)
        {
            throw new ArgumentException("После завершения попытки задача не может остаться в статусе Running.", nameof(newStatus));
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Только незавершённая попытка: повторное завершение той же записи ничего не меняет.
        var entry = await db.ExecutionHistories
            .FirstOrDefaultAsync(h => h.Id == historyId && h.FinishedAt == null, cancellationToken);

        if (entry is null)
        {
            return false;
        }

        var taskError = Truncate(errorMessage, MaxTaskErrorLength);

        var updated = await db.Tasks
            .Where(t => t.Id == entry.TaskId && t.Status == ScheduledTaskStatus.Running)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(t => t.Status, newStatus)
                    .SetProperty(t => t.LastError, taskError)
                    .SetProperty(t => t.UpdatedAt, nowUtc),
                cancellationToken);

        if (updated == 0)
        {
            return false;
        }

        entry.FinishedAt = nowUtc;
        entry.Result = result;
        entry.ErrorMessage = Truncate(errorMessage, MaxHistoryErrorLength);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
