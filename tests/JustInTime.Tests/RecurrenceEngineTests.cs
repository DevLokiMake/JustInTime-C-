using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Infrastructure.Repositories;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

/// <summary>Повторяющиеся задачи: создание и перевооружение после запуска (реальный PostgreSQL).</summary>
[Collection(PostgresCollection.Name)]
public sealed class RecurrenceEngineTests(PostgresFixture db) : IAsyncLifetime
{
    private static readonly OpenUrlPayload Url = new("https://example.com");

    // Now = 2026-11-06 12:00 UTC. Часовой пояс задач UTC, чтобы расчёты были наглядны.
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly TaskRepository _tasks = new(db.Factory);
    private readonly SettingsRepository _settings = new(db.Factory);
    private readonly FakeTaskRegistrar _registrar = new();
    private readonly FakeExecutor _executor = new(ActionType.OpenUrl);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private SchedulerEngine CreateEngine() =>
        new(_tasks, _settings, _registrar, [_executor], _clock, new SchedulerEngineOptions());

    private async Task<ScheduledTask> AddDailyAsync(DateTime at, bool recurring = true)
    {
        var task = NewTask(at, ScheduledTaskStatus.Scheduled);
        task.ActionType = ActionType.OpenUrl;
        task.Payload = TaskPayloadSerializer.Serialize(Url);
        task.TimeZone = "UTC";
        task.IsRecurring = recurring;
        task.Recurrence = recurring
            ? RecurrenceSerializer.Serialize(new RecurrenceRule(RecurrenceKind.Daily, 1, TimeOfDayMinutes: 9 * 60))
            : null;
        await _tasks.AddAsync(task);
        return task;
    }

    [Fact]
    public async Task Successful_run_of_a_recurring_task_schedules_the_next_day()
    {
        var task = await AddDailyAsync(Now.AddMinutes(-10));   // 11:50 сегодня, опоздание в пределах допустимых 30 минут

        await CreateEngine().RunDueAsync();

        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.Equal(ScheduledTaskStatus.Scheduled, saved.Status);
        Assert.Equal(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), saved.ScheduledAt);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Contains(task.Id, _registrar.Registered);
        Assert.Equal(saved.ScheduledAt, _registrar.LastRegistered!.ScheduledAt);
    }

    [Fact]
    public async Task Failed_run_does_not_stop_the_series()
    {
        _executor.Behavior = _ => ExecutionOutcome.Failed("нет сети");
        var task = await AddDailyAsync(Now.AddMinutes(-10));

        await CreateEngine().RunDueAsync();

        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.True(saved.Status is ScheduledTaskStatus.Pending or ScheduledTaskStatus.Scheduled);
        Assert.Equal(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), saved.ScheduledAt);
    }

    [Fact]
    public async Task Unknown_result_pauses_the_series_until_the_user_decides()
    {
        _executor.Behavior = _ => ExecutionOutcome.Unknown("не ясно");
        var task = await AddDailyAsync(Now.AddMinutes(-10));

        await CreateEngine().RunDueAsync();

        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, saved.Status);
        Assert.Equal(Now.AddMinutes(-10), saved.ScheduledAt);
    }

    [Fact]
    public async Task One_off_task_is_not_rearmed()
    {
        var task = await AddDailyAsync(Now.AddMinutes(-10), recurring: false);

        await CreateEngine().RunDueAsync();

        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.Equal(ScheduledTaskStatus.Completed, saved.Status);
    }

    [Fact]
    public async Task Missed_occurrence_of_a_recurring_task_is_skipped_and_the_next_one_is_scheduled()
    {
        var task = await AddDailyAsync(Now.AddDays(-2));    // опоздание больше допустимых 30 минут

        await CreateEngine().RunDueAsync();

        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.Equal(0, _executor.Calls);
        Assert.True(saved.Status is ScheduledTaskStatus.Pending or ScheduledTaskStatus.Scheduled);
        Assert.Equal(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), saved.ScheduledAt);
    }

    [Fact]
    public async Task Cancelled_recurring_task_is_not_rearmed()
    {
        var task = await AddDailyAsync(Now.AddMinutes(-10));
        await _tasks.TryCancelAsync(task.Id, Now);

        Assert.False(await _tasks.TryRearmAsync(task.Id, Now.AddDays(1), Now));
    }

    [Fact]
    public async Task Service_creates_a_weekly_task_and_moves_the_first_run_to_the_matching_day()
    {
        var service = new TaskService(_tasks, _registrar, _clock);
        var rule = new RecurrenceRule(RecurrenceKind.Weekly, DaysOfWeekMask: RecurrenceRule.MaskOf(DayOfWeek.Monday));
        // 06.11.2026 пятница, выбрано 10:00. Первый запуск должен быть в понедельник 09.11 в 10:00.
        var schedule = new AtLocalTimeSchedule(new DateTime(2026, 11, 6, 15, 0, 0), "UTC");

        var result = await service.CreateAsync(new TaskInput("Еженедельно", Url, schedule, rule));

        Assert.True(result.Succeeded);
        var saved = (await _tasks.GetByIdAsync(result.Value!.Id))!;
        Assert.True(saved.IsRecurring);
        Assert.Equal(new DateTime(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc), saved.ScheduledAt);
        var stored = RecurrenceSerializer.Deserialize(saved.Recurrence)!;
        Assert.Equal(15 * 60, stored.TimeOfDayMinutes);
    }

    [Fact]
    public async Task Service_refuses_an_invalid_rule()
    {
        var service = new TaskService(_tasks, _registrar, _clock);
        var rule = new RecurrenceRule(RecurrenceKind.Weekly, DaysOfWeekMask: 0);

        var result = await service.CreateAsync(new TaskInput("Плохое", Url, new AfterDelaySchedule(10), rule));

        Assert.False(result.Succeeded);
    }
}
