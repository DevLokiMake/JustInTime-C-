using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>
/// Выполняет задачи, время которых наступило. Не знает ни про Windows, ни про интерфейс:
/// запускается и из окна (цикл раз в 30 секунд), и из Планировщика заданий (ключ --run-due).
/// Одновременные запуски безопасны: каждую задачу атомарно «берёт» только один из них.
/// </summary>
public sealed class SchedulerEngine
{
    private readonly ITaskRepository _tasks;
    private readonly ISettingsStore _settings;
    private readonly ITaskRegistrar _registrar;
    private readonly TimeProvider _time;
    private readonly SchedulerEngineOptions _options;
    private readonly IDesktopSession? _desktop;
    private readonly Dictionary<ActionType, IActionExecutor> _executors;

    public SchedulerEngine(
        ITaskRepository tasks,
        ISettingsStore settings,
        ITaskRegistrar registrar,
        IEnumerable<IActionExecutor> executors,
        TimeProvider time,
        SchedulerEngineOptions options,
        IDesktopSession? desktop = null)
    {
        _desktop = desktop;
        _tasks = tasks;
        _settings = settings;
        _registrar = registrar;
        _time = time;
        _options = options;
        _executors = executors.ToDictionary(e => e.ActionType);
    }

    /// <summary>Один прогон: чинит зависшие задачи, затем выполняет всё, что пора.</summary>
    public async Task<RunReport> RunDueAsync(CancellationToken cancellationToken = default)
    {
        // Выключатель автоматизации главнее всего: ничего не запускаем и ничего не помечаем.
        if (!await _settings.GetBoolAsync(SettingKeys.AutomationEnabled, true, cancellationToken))
        {
            return RunReport.Disabled;
        }

        var now = Now();

        var recovered = await _tasks.RecoverStuckAsync(
            now - _options.StuckAfter, now, StuckMessage, cancellationToken);

        var graceMinutes = Math.Clamp(
            await _settings.GetIntAsync(
                SettingKeys.MissedGraceMinutes, SchedulerEngineOptions.DefaultMissedGraceMinutes, cancellationToken),
            SchedulerEngineOptions.MinMissedGraceMinutes,
            SchedulerEngineOptions.MaxMissedGraceMinutes);

        var due = await _tasks.GetDueAsync(
            now, now - _options.WaitingRetryInterval, _options.BatchLimit, cancellationToken);

        var entries = new List<TaskRunEntry>();

        foreach (var task in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = await RunOneAsync(task, TimeSpan.FromMinutes(graceMinutes), cancellationToken);

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return new RunReport(false, entries, recovered);
    }

    /// <summary>
    /// Сверяет БД с системным планировщиком: регистрирует запуски для всех ожидающих задач.
    /// removeStale=true удаляет лишние запуски; из процесса, который сам запущен планировщиком, это делать нельзя.
    /// </summary>
    public async Task<ReconcileResult> ReconcileAsync(bool removeStale, CancellationToken cancellationToken = default)
    {
        var active = await _tasks.GetListAsync(ScheduledTaskStatuses.Active, cancellationToken);

        var toRegister = active.Where(t => ScheduledTaskStatuses.Startable.Contains(t.Status)).ToList();

        // Запуск выполняющейся задачи удалять нельзя: его мог начать именно тот процесс, который сейчас работает.
        var keep = active
            .Where(t => t.Status == ScheduledTaskStatus.Running)
            .Select(t => t.Id)
            .ToList();

        var result = await _registrar.ReconcileAsync(toRegister, keep, removeStale, cancellationToken);

        foreach (var id in result.RegisteredIds)
        {
            await _tasks.TryMarkScheduledAsync(id, Now(), cancellationToken);
        }

        return result;
    }

    private const string StuckMessage =
        "Выполнение прервано: приложение закрылось или завершилось посреди действия. " +
        "Неизвестно, успело ли действие выполниться. Проверьте результат вручную и при необходимости повторите задачу.";

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private async Task<TaskRunEntry?> RunOneAsync(
        ScheduledTask task, TimeSpan grace, CancellationToken cancellationToken)
    {
        var now = Now();
        var late = now - task.ScheduledAt;

        if (late > grace)
        {
            var message =
                $"Пропущена: запуск опоздал на {FormatDuration(late)} при допустимых {FormatDuration(grace)}. " +
                "Компьютер был выключен или приложение не работало в нужное время. Можно повторить задачу вручную.";

            if (!await _tasks.TryMarkMissedAsync(task.Id, message, now, cancellationToken))
            {
                return null;
            }

            var missedNote = await RearmIfRecurringAsync(task, cancellationToken);

            return new TaskRunEntry(
                task.Id, task.Title, ScheduledTaskStatus.Failed, ExecutionResult.Skipped, message + missedNote);
        }

        if (_desktop is not null
            && _executors.TryGetValue(task.ActionType, out var needsDesktop)
            && needsDesktop.RequiresInteractiveDesktop
            && !_desktop.IsAvailable(out var reason))
        {
            // Запуска не было: ни попытки, ни записи в историю. Задача ждёт, пока пользователь разблокирует компьютер.
            var waitMessage = "Ожидает разблокировки: " + reason;

            return await _tasks.TryMarkWaitingForUnlockAsync(task.Id, waitMessage, now, cancellationToken)
                ? new TaskRunEntry(
                    task.Id, task.Title, ScheduledTaskStatus.WaitingForUnlock, ExecutionResult.Skipped, waitMessage)
                : null;
        }

        var history = await _tasks.TryStartExecutionAsync(task.Id, now, cancellationToken);

        if (history is null)
        {
            // Задачу уже взял другой процесс, отменили или изменили. Это нормально.
            return null;
        }

        var outcome = await ExecuteSafelyAsync(task, cancellationToken);
        var (result, status) = Map(outcome.Kind);

        // Результат сохраняем даже при отмене: иначе задача навсегда останется в Running.
        var saved = await _tasks.FinishExecutionAsync(
            history.Id, result, outcome.Message, status, Now(), CancellationToken.None);

        var note = saved
            ? outcome.Message
            : "Результат не сохранён: состояние задачи изменилось во время выполнения. " + outcome.Message;

        if (saved && status is ScheduledTaskStatus.Completed or ScheduledTaskStatus.Failed)
        {
            note += await RearmIfRecurringAsync(task, cancellationToken);
        }

        return new TaskRunEntry(task.Id, task.Title, status, result, note);
    }

    /// <summary>
    /// Повторяющаяся задача после запуска (успешного, неудачного или пропущенного) получает время следующего
    /// запуска и снова встаёт в очередь. Возвращает пояснение для журнала (пусто, если повтора нет).
    /// Неизвестный результат сюда не попадает: такая задача ждёт решения пользователя.
    /// </summary>
    private async Task<string> RearmIfRecurringAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        if (!task.IsRecurring)
        {
            return string.Empty;
        }

        var rule = RecurrenceSerializer.Deserialize(task.Recurrence);

        if (rule is null)
        {
            return " Повтор остановлен: правило повтора повреждено, задачу нужно создать заново.";
        }

        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = _time.LocalTimeZone;
        }

        var now = Now();
        var next = RecurrenceCalculator.Next(rule, zone, task.ScheduledAt, now);

        if (next is null)
        {
            return " Повтор остановлен: не удалось вычислить следующий запуск.";
        }

        if (!await _tasks.TryRearmAsync(task.Id, next.Value, now, cancellationToken))
        {
            return string.Empty;
        }

        var current = await _tasks.GetByIdAsync(task.Id, cancellationToken);

        if (current is not null)
        {
            var registration = await _registrar.RegisterAsync(current, cancellationToken);

            if (registration.Succeeded)
            {
                await _tasks.TryMarkScheduledAsync(task.Id, Now(), cancellationToken);
            }
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(next.Value, zone);
        return $" Следующий запуск: {local:dd.MM.yyyy HH:mm}.";
    }

    private async Task<ExecutionOutcome> ExecuteSafelyAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        if (!_executors.TryGetValue(task.ActionType, out var executor))
        {
            return ExecutionOutcome.Failed(
                $"Действие «{task.ActionType.ToDisplayName()}» пока не поддерживается в этой версии приложения.");
        }

        var payload = TaskPayloadSerializer.Deserialize(task.ActionType, task.Payload);

        if (payload is null)
        {
            return ExecutionOutcome.Failed("Параметры задачи повреждены и не читаются. Создайте задачу заново.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = executor.Timeout is { } own && own > _options.ActionTimeout ? own : _options.ActionTimeout;
        timeout.CancelAfter(limit);

        try
        {
            return await executor.ExecuteAsync(payload, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ExecutionOutcome.Unknown(
                $"Действие не уложилось в {FormatDuration(limit)}. Неизвестно, выполнено ли оно. Проверьте результат вручную.");
        }
        catch (OperationCanceledException)
        {
            return ExecutionOutcome.Unknown(
                "Выполнение прервано закрытием приложения. Неизвестно, выполнено ли действие. Проверьте результат вручную.");
        }
        catch (Exception ex)
        {
            return ExecutionOutcome.Unknown(
                $"Непредвиденная ошибка при выполнении: {ex.Message} Неизвестно, выполнено ли действие. Проверьте результат вручную.");
        }
    }

    private static (ExecutionResult Result, ScheduledTaskStatus Status) Map(OutcomeKind kind) => kind switch
    {
        OutcomeKind.Success => (ExecutionResult.Success, ScheduledTaskStatus.Completed),
        OutcomeKind.Failed => (ExecutionResult.Failed, ScheduledTaskStatus.Failed),
        OutcomeKind.Unknown => (ExecutionResult.Unknown, ScheduledTaskStatus.NeedsUserAction),
        OutcomeKind.WaitForUnlock => (ExecutionResult.Skipped, ScheduledTaskStatus.WaitingForUnlock),
        OutcomeKind.WaitForNetwork => (ExecutionResult.Skipped, ScheduledTaskStatus.WaitingForNetwork),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный вид результата.")
    };

    private static string FormatDuration(TimeSpan value)
    {
        if (value < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(1, (int)value.TotalSeconds)} с";
        }

        var totalMinutes = (int)value.TotalMinutes;

        return totalMinutes < 120
            ? $"{totalMinutes} мин."
            : $"{totalMinutes / 60} ч {totalMinutes % 60} мин.";
    }
}
