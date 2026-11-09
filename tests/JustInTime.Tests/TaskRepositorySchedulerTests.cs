using JustInTime.Core.Models;
using JustInTime.Infrastructure.Repositories;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

/// <summary>Запросы репозитория, которые нужны планировщику.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TaskRepositorySchedulerTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly TaskRepository _tasks = new(db.Factory);
    private readonly ExecutionHistoryRepository _history = new(db.Factory);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<ScheduledTask> AddAsync(DateTime at, ScheduledTaskStatus status)
    {
        var task = NewTask(at, status);
        await _tasks.AddAsync(task);
        return task;
    }

    [Fact]
    public async Task GetDue_returns_only_due_tasks_in_startable_statuses_ordered_by_time()
    {
        var later = await AddAsync(Now.AddMinutes(-1), ScheduledTaskStatus.Scheduled);
        var earlier = await AddAsync(Now.AddMinutes(-5), ScheduledTaskStatus.Pending);
        await AddAsync(Now.AddMinutes(5), ScheduledTaskStatus.Scheduled);
        await AddAsync(Now.AddMinutes(-1), ScheduledTaskStatus.Completed);
        await AddAsync(Now.AddMinutes(-1), ScheduledTaskStatus.Cancelled);
        await AddAsync(Now.AddMinutes(-1), ScheduledTaskStatus.Failed);
        await AddAsync(Now.AddMinutes(-1), ScheduledTaskStatus.Running);
        await AddAsync(Now.AddMinutes(-1), ScheduledTaskStatus.NeedsUserAction);

        var due = await _tasks.GetDueAsync(Now, Now.AddMinutes(-1), 50);

        Assert.Equal(new[] { earlier.Id, later.Id }, due.Select(t => t.Id));
    }

    [Fact]
    public async Task GetDue_includes_waiting_tasks_only_after_the_retry_interval()
    {
        var task = NewTask(Now.AddMinutes(-10), ScheduledTaskStatus.WaitingForNetwork);
        task.UpdatedAt = Now.AddSeconds(-30);
        await _tasks.AddAsync(task);

        Assert.Empty(await _tasks.GetDueAsync(Now, Now.AddMinutes(-1), 50));
        Assert.Single(await _tasks.GetDueAsync(Now.AddMinutes(1), Now.AddSeconds(-30), 50));
    }

    [Fact]
    public async Task GetDue_respects_limit()
    {
        for (var i = 1; i <= 5; i++)
        {
            await AddAsync(Now.AddMinutes(-i), ScheduledTaskStatus.Scheduled);
        }

        Assert.Equal(3, (await _tasks.GetDueAsync(Now, Now, 3)).Count);
    }

    [Fact]
    public async Task TryMarkScheduled_changes_only_pending_tasks()
    {
        var pending = await AddAsync(Now.AddHours(1), ScheduledTaskStatus.Pending);
        var cancelled = await AddAsync(Now.AddHours(1), ScheduledTaskStatus.Cancelled);

        Assert.True(await _tasks.TryMarkScheduledAsync(pending.Id, Now));
        Assert.False(await _tasks.TryMarkScheduledAsync(pending.Id, Now));
        Assert.False(await _tasks.TryMarkScheduledAsync(cancelled.Id, Now));
        Assert.False(await _tasks.TryMarkScheduledAsync(Guid.NewGuid(), Now));

        Assert.Equal(ScheduledTaskStatus.Scheduled, (await _tasks.GetByIdAsync(pending.Id))!.Status);
        Assert.Equal(ScheduledTaskStatus.Cancelled, (await _tasks.GetByIdAsync(cancelled.Id))!.Status);
    }

    [Fact]
    public async Task TryMarkMissed_fails_task_and_writes_skipped_history()
    {
        var task = await AddAsync(Now.AddHours(-2), ScheduledTaskStatus.Scheduled);

        Assert.True(await _tasks.TryMarkMissedAsync(task.Id, "Пропущена", Now));

        var saved = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.Failed, saved!.Status);
        Assert.Equal("Пропущена", saved.LastError);
        Assert.Equal(0, saved.AttemptCount);

        var entry = Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(ExecutionResult.Skipped, entry.Result);
        Assert.NotNull(entry.FinishedAt);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Running)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    [InlineData(ScheduledTaskStatus.NeedsUserAction)]
    public async Task TryMarkMissed_refuses_tasks_that_cannot_be_started(ScheduledTaskStatus status)
    {
        var task = await AddAsync(Now.AddHours(-2), status);

        Assert.False(await _tasks.TryMarkMissedAsync(task.Id, "x", Now));
        Assert.Empty(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(status, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task RecoverStuck_moves_old_running_tasks_to_needs_user_action_and_closes_their_attempt()
    {
        var stuck = await AddAsync(Now.AddMinutes(-30), ScheduledTaskStatus.Scheduled);
        await _tasks.TryStartExecutionAsync(stuck.Id, Now.AddMinutes(-20));

        var fresh = await AddAsync(Now.AddMinutes(-2), ScheduledTaskStatus.Scheduled);
        await _tasks.TryStartExecutionAsync(fresh.Id, Now.AddMinutes(-1));

        var recovered = await _tasks.RecoverStuckAsync(Now.AddMinutes(-10), Now, "прервано");

        Assert.Equal(1, recovered);

        var stuckSaved = await _tasks.GetByIdAsync(stuck.Id);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, stuckSaved!.Status);
        Assert.Equal("прервано", stuckSaved.LastError);

        var entry = Assert.Single(await _history.GetByTaskAsync(stuck.Id));
        Assert.Equal(ExecutionResult.Unknown, entry.Result);
        Assert.Equal(Now, entry.FinishedAt);

        Assert.Equal(ScheduledTaskStatus.Running, (await _tasks.GetByIdAsync(fresh.Id))!.Status);
        Assert.Null(Assert.Single(await _history.GetByTaskAsync(fresh.Id)).FinishedAt);
    }

    [Fact]
    public async Task RecoverStuck_returns_zero_when_nothing_is_stuck()
    {
        await AddAsync(Now.AddMinutes(1), ScheduledTaskStatus.Scheduled);

        Assert.Equal(0, await _tasks.RecoverStuckAsync(Now.AddMinutes(-10), Now, "x"));
    }
}
