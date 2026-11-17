using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

public sealed class TaskService(ITaskRepository tasks, ITaskRegistrar registrar, TimeProvider time) : ITaskService
{
    private sealed record Prepared(
        string Title, TaskPayload Payload, ResolvedSchedule Schedule, string PayloadJson, string? RecurrenceJson);

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
            IsRecurring = p.RecurrenceJson is not null,
            Recurrence = p.RecurrenceJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        await tasks.AddAsync(task, cancellationToken);

        var warnings = await RegisterAsync(task, cancellationToken);

        return warnings.Count == 0
            ? ServiceResult<ScheduledTask>.Ok(task)
            : ServiceResult<ScheduledTask>.OkWithWarnings(task, warnings);
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
        var update = new TaskUpdate(
            p.Title, p.Schedule.Utc, p.Schedule.TimeZoneId, p.PayloadJson, p.RecurrenceJson is not null, p.RecurrenceJson);

        if (await tasks.TryUpdateAsync(id, update, now, cancellationToken))
        {
            var changed = await tasks.GetByIdAsync(id, cancellationToken);
            var warnings = changed is null
                ? new List<string>()
                : await RegisterAsync(changed, cancellationToken);

            return warnings.Count == 0 ? ServiceResult.Ok() : ServiceResult.OkWithWarnings(warnings);
        }

        return ServiceResult.Fail(await ExplainRefusalAsync(id, "изменить", cancellationToken));
    }

    public async Task<ServiceResult> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await tasks.TryCancelAsync(id, UtcNow(), cancellationToken))
        {
            // Задача уже отменена в БД. Лишний системный запуск безвреден: при срабатывании движок увидит
            // статус Cancelled и ничего не сделает, а при следующем старте приложения запуск будет удалён.
            var removed = await registrar.UnregisterAsync(id, cancellationToken);

            return removed.Succeeded
                ? ServiceResult.Ok()
                : ServiceResult.OkWithWarnings(new[]
                {
                    "Задача отменена, но её запуск не удалось снять в Планировщике Windows: " + removed.Error +
                    " Он ничего не сделает и будет удалён при следующем запуске приложения."
                });
        }

        return ServiceResult.Fail(await ExplainRefusalAsync(id, "отменить", cancellationToken));
    }

    public async Task<ServiceResult> RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = UtcNow();

        if (await tasks.TryRetryAsync(id, now, now, cancellationToken))
        {
            var retried = await tasks.GetByIdAsync(id, cancellationToken);
            var warnings = retried is null
                ? new List<string>()
                : await RegisterAsync(retried, cancellationToken);

            return warnings.Count == 0 ? ServiceResult.Ok() : ServiceResult.OkWithWarnings(warnings);
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

    /// <summary>
    /// Регистрирует системный запуск и помечает задачу Scheduled. Сбой регистрации не отменяет создание задачи:
    /// она остаётся в Pending и выполнится, пока приложение запущено, а пользователь получает предупреждение.
    /// </summary>
    private async Task<List<string>> RegisterAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        var registration = await registrar.RegisterAsync(task, cancellationToken);

        if (registration.Succeeded)
        {
            await tasks.TryMarkScheduledAsync(task.Id, UtcNow(), cancellationToken);
            return new List<string>();
        }

        return new List<string>
        {
            "Задача сохранена, но её не удалось поставить в Планировщик Windows: " + registration.Error +
            " Она выполнится в нужное время только пока приложение запущено."
        };
    }

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

        string? recurrenceJson = null;
        var resolved = schedule.Value;

        if (input.Recurrence is { } rule && schedule.Succeeded)
        {
            errors.AddRange(RecurrenceValidator.Validate(rule, requireComplete: false));

            if (errors.Count == 0)
            {
                var completed = CompleteRule(rule, resolved!);
                var zone = ZoneOf(resolved!.TimeZoneId);
                var first = RecurrenceCalculator.FirstOnOrAfter(completed, zone, resolved.Utc);

                if (first is null)
                {
                    errors.Add("Не удалось вычислить первый запуск по правилу повтора.");
                }
                else
                {
                    resolved = resolved with { Utc = first.Value };
                    recurrenceJson = RecurrenceSerializer.Serialize(completed);
                }
            }
        }

        if (errors.Count > 0 || payload is null || resolved is null)
        {
            return ServiceResult<Prepared>.Fail(errors);
        }

        return ServiceResult<Prepared>.Ok(new Prepared(
            title, payload, resolved, TaskPayloadSerializer.Serialize(payload), recurrenceJson));
    }

    /// <summary>Достраивает правило: время суток и число месяца берутся из выбранного пользователем первого запуска.</summary>
    private RecurrenceRule CompleteRule(RecurrenceRule rule, ResolvedSchedule start)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(start.Utc, ZoneOf(start.TimeZoneId));

        return rule with
        {
            TimeOfDayMinutes = rule.TimeOfDayMinutes ?? local.Hour * 60 + local.Minute,
            DayOfMonth = rule.Kind == RecurrenceKind.Monthly ? rule.DayOfMonth ?? local.Day : null
        };
    }

    private TimeZoneInfo ZoneOf(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return time.LocalTimeZone;
        }
    }

    private async Task<string> ExplainRefusalAsync(Guid id, string action, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(id, cancellationToken);

        return task is null
            ? "Задача не найдена."
            : $"Задачу в статусе «{task.Status.ToDisplayName()}» нельзя {action}.";
    }
}
