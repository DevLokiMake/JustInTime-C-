using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Infrastructure.Repositories;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

/// <summary>Движок планировщика с реальным PostgreSQL и поддельными исполнителями.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SchedulerEngineTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly OpenUrlPayload Url = new("https://example.com");

    private readonly FixedTimeProvider _clock = new(Now);
    private readonly TaskRepository _tasks = new(db.Factory);
    private readonly ExecutionHistoryRepository _history = new(db.Factory);
    private readonly SettingsRepository _settings = new(db.Factory);
    private readonly FakeTaskRegistrar _registrar = new();
    private readonly FakeExecutor _executor = new(ActionType.OpenUrl);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private SchedulerEngine CreateEngine(SchedulerEngineOptions? options = null) =>
        new(_tasks, _settings, _registrar, [_executor], _clock, options ?? new SchedulerEngineOptions());

    private async Task<ScheduledTask> AddTaskAsync(
        DateTime at,
        ScheduledTaskStatus status = ScheduledTaskStatus.Scheduled,
        ActionType action = ActionType.OpenUrl,
        string? payload = null)
    {
        var task = NewTask(at, status);
        task.ActionType = action;
        task.Payload = payload ?? TaskPayloadSerializer.Serialize(Url);
        await _tasks.AddAsync(task);
        return task;
    }

    private async Task<ScheduledTask> ReloadAsync(ScheduledTask task) =>
        (await _tasks.GetByIdAsync(task.Id))!;

    // Сценарий 3: выполнение в нужное время

    [Fact]
    public async Task Due_task_is_executed_and_completed()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));

        var report = await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.Completed, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Equal(1, _executor.Calls);

        var history = Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(ExecutionResult.Success, history.Result);
        Assert.NotNull(history.FinishedAt);

        Assert.Equal(ScheduledTaskStatus.Completed, Assert.Single(report.Entries).FinalStatus);
    }

    [Fact]
    public async Task Task_in_the_future_is_not_executed_until_its_time()
    {
        var task = await AddTaskAsync(Now.AddMinutes(15));
        var engine = CreateEngine();

        await engine.RunDueAsync();
        Assert.Equal(0, _executor.Calls);
        Assert.Equal(ScheduledTaskStatus.Scheduled, (await ReloadAsync(task)).Status);

        _clock.NowUtc = Now.AddMinutes(15);
        await engine.RunDueAsync();
        Assert.Equal(1, _executor.Calls);
        Assert.Equal(ScheduledTaskStatus.Completed, (await ReloadAsync(task)).Status);
    }

    // Сценарий 13: повторное выполнение

    [Fact]
    public async Task Completed_task_is_not_executed_again()
    {
        await AddTaskAsync(Now.AddMinutes(-1));
        var engine = CreateEngine();

        await engine.RunDueAsync();
        await engine.RunDueAsync();

        Assert.Equal(1, _executor.Calls);
    }

    [Fact]
    public async Task Two_engines_running_at_once_execute_the_task_only_once()
    {
        await AddTaskAsync(Now.AddMinutes(-1));
        _executor.Behavior = _ =>
        {
            Thread.Sleep(100);
            return ExecutionOutcome.Success();
        };

        await Task.WhenAll(
            Task.Run(() => CreateEngine().RunDueAsync()),
            Task.Run(() => CreateEngine().RunDueAsync()));

        Assert.Equal(1, _executor.Calls);
    }

    // Сценарий 4: отмена

    [Fact]
    public async Task Cancelled_task_is_not_executed()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1), ScheduledTaskStatus.Cancelled);

        await CreateEngine().RunDueAsync();

        Assert.Equal(0, _executor.Calls);
        Assert.Equal(ScheduledTaskStatus.Cancelled, (await ReloadAsync(task)).Status);
    }

    // Выключатель автоматизации

    [Fact]
    public async Task Nothing_runs_when_automation_is_disabled()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        await _settings.SetBoolAsync(SettingKeys.AutomationEnabled, false);

        var report = await CreateEngine().RunDueAsync();

        Assert.True(report.AutomationDisabled);
        Assert.Equal(0, _executor.Calls);
        Assert.Equal(ScheduledTaskStatus.Scheduled, (await ReloadAsync(task)).Status);
        Assert.Empty(await _history.GetByTaskAsync(task.Id));
    }

    // Пропущенные задачи (компьютер был выключен)

    [Fact]
    public async Task Task_missed_by_more_than_grace_is_marked_failed_and_not_executed()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-31));

        var report = await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.Failed, saved.Status);
        Assert.Contains("Пропущена", saved.LastError);
        Assert.Equal(0, saved.AttemptCount);
        Assert.Equal(0, _executor.Calls);

        var history = Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(ExecutionResult.Skipped, history.Result);
        Assert.Equal(ExecutionResult.Skipped, Assert.Single(report.Entries).Result);
    }

    [Fact]
    public async Task Task_late_within_grace_is_still_executed()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-29));

        await CreateEngine().RunDueAsync();

        Assert.Equal(ScheduledTaskStatus.Completed, (await ReloadAsync(task)).Status);
    }

    [Fact]
    public async Task Grace_period_can_be_changed_in_settings()
    {
        await _settings.SetIntAsync(SettingKeys.MissedGraceMinutes, 120);
        var task = await AddTaskAsync(Now.AddMinutes(-100));

        await CreateEngine().RunDueAsync();

        Assert.Equal(ScheduledTaskStatus.Completed, (await ReloadAsync(task)).Status);
    }

    [Fact]
    public async Task Nonsense_grace_setting_is_clamped_to_a_safe_range()
    {
        await _settings.SetIntAsync(SettingKeys.MissedGraceMinutes, -5);
        var task = await AddTaskAsync(Now.AddMinutes(-2));

        await CreateEngine().RunDueAsync();

        // Минимум 1 минута: опоздание на 2 минуты уже слишком много.
        Assert.Equal(ScheduledTaskStatus.Failed, (await ReloadAsync(task)).Status);
    }

    // Разные итоги действия

    [Theory]
    [InlineData(OutcomeKind.Failed, ScheduledTaskStatus.Failed, ExecutionResult.Failed)]
    [InlineData(OutcomeKind.Unknown, ScheduledTaskStatus.NeedsUserAction, ExecutionResult.Unknown)]
    [InlineData(OutcomeKind.WaitForUnlock, ScheduledTaskStatus.WaitingForUnlock, ExecutionResult.Skipped)]
    [InlineData(OutcomeKind.WaitForNetwork, ScheduledTaskStatus.WaitingForNetwork, ExecutionResult.Skipped)]
    public async Task Executor_outcome_is_mapped_to_status_and_history(
        OutcomeKind kind, ScheduledTaskStatus expectedStatus, ExecutionResult expectedResult)
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        _executor.Behavior = _ => new ExecutionOutcome(kind, "сообщение");

        await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(expectedStatus, saved.Status);
        Assert.Equal("сообщение", saved.LastError);
        Assert.Equal(expectedResult, Assert.Single(await _history.GetByTaskAsync(task.Id)).Result);
    }

    // Сценарий 14: неопределённый результат

    [Fact]
    public async Task Unknown_result_is_never_retried_automatically()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        _executor.Behavior = _ => ExecutionOutcome.Unknown("не уверены");
        var engine = CreateEngine();

        await engine.RunDueAsync();
        _clock.NowUtc = Now.AddHours(2);
        await engine.RunDueAsync();
        await engine.RunDueAsync();

        Assert.Equal(1, _executor.Calls);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, (await ReloadAsync(task)).Status);
    }

    [Fact]
    public async Task Unexpected_exception_is_treated_as_unknown_result()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        _executor.Behavior = _ => throw new InvalidOperationException("kaboom");

        await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, saved.Status);
        Assert.Contains("kaboom", saved.LastError);
    }

    [Fact]
    public async Task Action_that_exceeds_timeout_is_treated_as_unknown_result()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        _executor.Hang = true;

        await CreateEngine(new SchedulerEngineOptions { ActionTimeout = TimeSpan.FromMilliseconds(150) }).RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, saved.Status);
        Assert.Contains("не уложилось", saved.LastError);
    }

    [Fact]
    public async Task Shutdown_during_action_still_saves_result_instead_of_leaving_task_running()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        using var cts = new CancellationTokenSource();
        _executor.Behavior = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        await CreateEngine().RunDueAsync(cts.Token);

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, saved.Status);
        Assert.NotNull(Assert.Single(await _history.GetByTaskAsync(task.Id)).FinishedAt);
    }

    // Ожидание условий (разблокировка, интернет)

    [Fact]
    public async Task Waiting_task_is_retried_no_more_often_than_the_interval()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        _executor.Behavior = _ => ExecutionOutcome.WaitForNetwork("нет сети");
        var engine = CreateEngine();

        await engine.RunDueAsync();
        Assert.Equal(1, _executor.Calls);

        _clock.NowUtc = Now.AddSeconds(30);
        await engine.RunDueAsync();
        Assert.Equal(1, _executor.Calls);

        _clock.NowUtc = Now.AddSeconds(70);
        await engine.RunDueAsync();
        Assert.Equal(2, _executor.Calls);
        Assert.Equal(2, (await ReloadAsync(task)).AttemptCount);
    }

    // Честные отказы

    [Fact]
    public async Task Action_without_executor_fails_honestly()
    {
        var task = await AddTaskAsync(
            Now.AddMinutes(-1),
            action: ActionType.ToggleSystemSetting,
            payload: TaskPayloadSerializer.Serialize(new SystemSettingPayload(SystemSettingKind.Bluetooth, false)));

        await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.Failed, saved.Status);
        Assert.Contains("пока не поддерживается", saved.LastError);
        Assert.Equal(0, _executor.Calls);
    }

    [Fact]
    public async Task Corrupt_payload_fails_without_calling_executor()
    {
        // jsonb не принимает невалидный JSON, поэтому «повреждение» моделируем корректным JSON не того вида.
        var task = await AddTaskAsync(Now.AddMinutes(-1), payload: "[]");

        await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.Failed, saved.Status);
        Assert.Contains("повреждены", saved.LastError);
        Assert.Equal(0, _executor.Calls);
    }

    // Зависшие задачи

    [Fact]
    public async Task Task_stuck_in_running_becomes_needs_user_action_and_is_not_rerun()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-20));
        await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(-15));

        var report = await CreateEngine().RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(1, report.RecoveredStuck);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, saved.Status);
        Assert.Equal(0, _executor.Calls);

        var history = Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(ExecutionResult.Unknown, history.Result);
        Assert.NotNull(history.FinishedAt);
    }

    [Fact]
    public async Task Recently_started_running_task_is_left_alone()
    {
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(-1));

        var report = await CreateEngine().RunDueAsync();

        Assert.Equal(0, report.RecoveredStuck);
        Assert.Equal(ScheduledTaskStatus.Running, (await ReloadAsync(task)).Status);
        Assert.Equal(0, _executor.Calls);
    }

    // Сверка с системным планировщиком (восстановление после перезапуска)

    [Fact]
    public async Task Reconcile_registers_only_startable_tasks_and_keeps_running_ones()
    {
        var scheduled = await AddTaskAsync(Now.AddHours(1));
        var pending = await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.Pending);
        var waiting = await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.WaitingForNetwork);
        var running = await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.Running);
        await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.NeedsUserAction);
        await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.Completed);
        await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.Cancelled);

        await CreateEngine().ReconcileAsync(removeStale: true);

        Assert.Equal(
            new[] { scheduled.Id, pending.Id, waiting.Id }.OrderBy(x => x),
            _registrar.LastToRegister.OrderBy(x => x));
        Assert.Equal(new[] { running.Id }, _registrar.LastKeep.ToArray());
        Assert.True(_registrar.LastRemoveStale);
    }

    [Fact]
    public async Task Reconcile_marks_pending_tasks_as_scheduled()
    {
        var pending = await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.Pending);

        await CreateEngine().ReconcileAsync(removeStale: false);

        Assert.Equal(ScheduledTaskStatus.Scheduled, (await ReloadAsync(pending)).Status);
        Assert.False(_registrar.LastRemoveStale);
    }

    [Fact]
    public async Task Reconcile_leaves_task_pending_when_registration_fails()
    {
        var pending = await AddTaskAsync(Now.AddHours(1), ScheduledTaskStatus.Pending);
        var engine = new SchedulerEngine(
            _tasks, _settings, new FailingRegistrar(), [_executor], _clock, new SchedulerEngineOptions());

        var result = await engine.ReconcileAsync(removeStale: false);

        Assert.Single(result.Errors);
        Assert.Equal(ScheduledTaskStatus.Pending, (await ReloadAsync(pending)).Status);
    }

    // Этап 6: интерфейсные действия ждут разблокировки

    private sealed class DesktopExecutor(ActionType type) : IActionExecutor
    {
        public ActionType ActionType => type;
        public bool RequiresInteractiveDesktop => true;
        public int Calls { get; private set; }

        public Task<ExecutionOutcome> ExecuteAsync(TaskPayload payload, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ExecutionOutcome.Success());
        }
    }

    private SchedulerEngine CreateDesktopEngine(IActionExecutor executor, IDesktopSession? desktop) =>
        new(_tasks, _settings, _registrar, [executor], _clock, new SchedulerEngineOptions(), desktop);

    [Fact]
    public async Task Desktop_task_waits_for_unlock_without_attempt_or_history()
    {
        var executor = new DesktopExecutor(ActionType.OpenUrl);
        var desktop = new FakeDesktopSession(available: false);
        var task = await AddTaskAsync(Now.AddMinutes(-1));

        var report = await CreateDesktopEngine(executor, desktop).RunDueAsync();

        var saved = await ReloadAsync(task);
        Assert.Equal(ScheduledTaskStatus.WaitingForUnlock, saved.Status);
        Assert.Equal(0, saved.AttemptCount);
        Assert.Contains("разблокир", saved.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, executor.Calls);
        Assert.Empty(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(ScheduledTaskStatus.WaitingForUnlock, Assert.Single(report.Entries).FinalStatus);
    }

    [Fact]
    public async Task Desktop_task_runs_after_unlock()
    {
        var executor = new DesktopExecutor(ActionType.OpenUrl);
        var desktop = new FakeDesktopSession(available: false);
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        var engine = CreateDesktopEngine(executor, desktop);

        await engine.RunDueAsync();
        desktop.Available = true;
        _clock.NowUtc = Now.AddMinutes(2);
        await engine.RunDueAsync();

        Assert.Equal(ScheduledTaskStatus.Completed, (await ReloadAsync(task)).Status);
        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public async Task Waiting_desktop_task_is_not_reported_again_and_is_retried_at_most_once_a_minute()
    {
        var executor = new DesktopExecutor(ActionType.OpenUrl);
        var desktop = new FakeDesktopSession(available: false);
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        var engine = CreateDesktopEngine(executor, desktop);

        await engine.RunDueAsync();

        // Сразу же второй прогон: задача ждёт и ещё не созрела для повтора.
        var second = await engine.RunDueAsync();
        Assert.Empty(second.Entries);

        // Через две минуты проверка повторяется, но статус не менялся, поэтому нового сообщения нет.
        _clock.NowUtc = Now.AddMinutes(2);
        var third = await engine.RunDueAsync();
        Assert.Empty(third.Entries);
        Assert.Equal(ScheduledTaskStatus.WaitingForUnlock, (await ReloadAsync(task)).Status);
    }

    [Fact]
    public async Task Non_desktop_task_runs_on_locked_computer()
    {
        var desktop = new FakeDesktopSession(available: false);
        var task = await AddTaskAsync(Now.AddMinutes(-1));

        // _executor (OpenUrl) интерфейс не использует: заблокированный экран ему не мешает.
        var engine = new SchedulerEngine(
            _tasks, _settings, _registrar, [_executor], _clock, new SchedulerEngineOptions(), desktop);
        await engine.RunDueAsync();

        Assert.Equal(ScheduledTaskStatus.Completed, (await ReloadAsync(task)).Status);
    }

    [Fact]
    public async Task Desktop_task_that_waited_too_long_is_marked_missed()
    {
        var executor = new DesktopExecutor(ActionType.OpenUrl);
        var desktop = new FakeDesktopSession(available: false);
        var task = await AddTaskAsync(Now.AddMinutes(-1));
        var engine = CreateDesktopEngine(executor, desktop);

        await engine.RunDueAsync();
        _clock.NowUtc = Now.AddHours(2);
        var report = await engine.RunDueAsync();

        Assert.Equal(ScheduledTaskStatus.Failed, (await ReloadAsync(task)).Status);
        Assert.Equal(ExecutionResult.Skipped, Assert.Single(report.Entries).Result);
        Assert.Equal(0, executor.Calls);
    }

    private sealed class FailingRegistrar : ITaskRegistrar
    {
        public Task<RegistrationResult> RegisterAsync(ScheduledTask task, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegistrationResult.Fail("нет доступа"));

        public Task<RegistrationResult> UnregisterAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegistrationResult.Fail("нет доступа"));

        public Task<ReconcileResult> ReconcileAsync(
            IReadOnlyCollection<ScheduledTask> toRegister,
            IReadOnlyCollection<Guid> keepIds,
            bool removeStale,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReconcileResult(Array.Empty<Guid>(), 0, ["нет доступа"]));
    }
}
