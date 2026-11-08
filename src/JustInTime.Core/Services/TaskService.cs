using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

public sealed class TaskService(ITaskRepository tasks, TimeProvider time) : ITaskService
{
    private sealed record Prepared(string Title, TaskPayload Payload, ResolvedSchedule Schedule, string PayloadJson);

    public async Task<ServiceResult<ScheduledTask>> CreateAsync(
        TaskInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var now = UtcNow();
        var prepared = Prepare(input, now);

        if (!prepared.Succeeded)
        {
            return ServiceResult<ScheduledTask>.Fail(prepared.Errors);
        }

        var p = prepared.Value!;

        var task = new ScheduledTask
        {
            Title = p.Title,
            ActionType = p.Payload.GetActionType(),
            ScheduledAt = p.Schedule.Utc,
            TimeZone = p.Schedule.TimeZoneId,
            Status = ScheduledTaskStatus.Pending,
            Payload = p.PayloadJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        await tasks.AddAsync(task, cancellationToken);

        return ServiceResult<ScheduledTask>.Ok(task);
    }

    public async Task<ServiceResult> UpdateAsync(
        Guid id, TaskInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var existing = await tasks.GetByIdAsync(id, cancellationToken);

        if (existing is null)
        {
            return ServiceResult.Fail("Задача не найдена.");
        }

        // Тип действия не меняется ни одной операцией репозитория, поэтому эта проверка не устаревает.
        if (existing.ActionType != input.Payload?.GetActionType())
        {
            return ServiceResult.Fail("Тип действия изменить нельзя. Создайте новую задачу.");
        }

        var now = UtcNow();
        var prepared = Prepare(input, now);

        if (!prepared.Succeeded)
        {
            return ServiceResult.Fail(prepared.Errors);
        }

        var p = prepared.Value!;
        var update = new TaskUpdate(p.Title, p.Schedule.Utc, p.Schedule.TimeZoneId, p.PayloadJson, false);

        if (await tasks.TryUpdateAsync(id, update, now, cancellationToken))
        {
            return ServiceResult.Ok();
        }

        return ServiceResult.Fail(await ExplainRefusalAsync(id, "изменить", cancellationToken));
    }

    public async Task<ServiceResult> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await tasks.TryCancelAsync(id, UtcNow(), cancellationToken))
        {
            return ServiceResult.Ok();
        }

        return ServiceResult.Fail(await ExplainRefusalAsync(id, "отменить", cancellationToken));
    }

    public async Task<ServiceResult> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = UtcNow();

        if (await tasks.TryRetryAsync(id, now, now, cancellationToken))
        {
            return ServiceResult.Ok();
        }

        var task = await tasks.GetByIdAsync(id, cancellationToken);

        return ServiceResult.Fail(task is null
            ? "Задача не найдена."
            : $"Повторить можно только задачу с ошибкой или неизвестным результатом, а эта в статусе «{task.Status.ToDisplayName()}».");
    }

    public Task<ScheduledTask?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        tasks.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<ScheduledTask>> GetListAsync(
        IReadOnlyCollection<ScheduledTaskStatus>? statuses = null,
        CancellationToken cancellationToken = default) =>
        tasks.GetListAsync(statuses, cancellationToken);

    private DateTime UtcNow() => time.GetUtcNow().UtcDateTime;

    private ServiceResult<Prepared> Prepare(TaskInput input, DateTime nowUtc)
    {
        var title = (input.Title ?? string.Empty).Trim();
        var payload = input.Payload?.Normalize();

        var errors = TaskValidator.Validate(title, payload).ToList();

        var schedule = ScheduleResolver.Resolve(input.Schedule, nowUtc, time.LocalTimeZone);

        if (!schedule.Succeeded)
        {
            errors.AddRange(schedule.Errors);
        }

        if (errors.Count > 0 || payload is null)
        {
            return ServiceResult<Prepared>.Fail(errors);
        }

        return ServiceResult<Prepared>.Ok(new Prepared(
            title, payload, schedule.Value!, TaskPayloadSerializer.Serialize(payload)));
    }

    private async Task<string> ExplainRefusalAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(id, cancellationToken);

        return task is null
            ? "Задача не найдена."
            : $"Задачу в статусе «{task.Status.ToDisplayName()}» нельзя {action}.";
    }
}
