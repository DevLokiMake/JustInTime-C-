using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Localization;

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
    private readonly TaskRearmer _rearmer;
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
        _rearmer = new TaskRearmer(tasks, registrar, time);
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

    private static string StuckMessage => Loc.T("eng_stuck");

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private async Task<TaskRunEntry?> RunOneAsync(
        ScheduledTask task, TimeSpan grace, CancellationToken cancellationToken)
    {
        var now = Now();
        var late = now - task.ScheduledAt;

        if (late > grace)
        {
            var message =
                Loc.T("eng_missed", FormatDuration(late), FormatDuration(grace));

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
            var waitMessage = Loc.T("eng_wait_unlock", reason);

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
            : Loc.T("eng_not_saved", outcome.Message);

        if (saved && status is ScheduledTaskStatus.Completed or ScheduledTaskStatus.Failed)
        {
            note += await RearmIfRecurringAsync(task, cancellationToken);
        }

        return new TaskRunEntry(task.Id, task.Title, status, result, note);
    }

    /// <summary>Повторяющаяся задача после запуска получает следующий (см. TaskRearmer). Неизвестный результат сюда не попадает.</summary>
    private async Task<string> RearmIfRecurringAsync(ScheduledTask task, CancellationToken cancellationToken) =>
        (await _rearmer.RearmIfRecurringAsync(task, cancellationToken)).Note;

    private async Task<ExecutionOutcome> ExecuteSafelyAsync(ScheduledTask task, CancellationToken cancellationToken)
    {
        if (!_executors.TryGetValue(task.ActionType, out var executor))
        {
            return ExecutionOutcome.Failed(
                Loc.T("eng_unsupported", task.ActionType.ToDisplayName()));
        }

        var payload = TaskPayloadSerializer.Deserialize(task.ActionType, task.Payload);

        if (payload is null)
        {
            return ExecutionOutcome.Failed(Loc.T("eng_payload_corrupt"));
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
                Loc.T("eng_timeout", FormatDuration(limit)));
        }
        catch (OperationCanceledException)
        {
            return ExecutionOutcome.Unknown(
                Loc.T("eng_shutdown"));
        }
        catch (Exception ex)
        {
            return ExecutionOutcome.Unknown(
                Loc.T("eng_unexpected", ex.Message));
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
            return Loc.T("dur_seconds", Math.Max(1, (int)value.TotalSeconds));
        }

        var totalMinutes = (int)value.TotalMinutes;

        return totalMinutes < 120
            ? Loc.T("dur_minutes", totalMinutes)
            : Loc.T("dur_hours_minutes", totalMinutes / 60, totalMinutes % 60);
    }
}
