using System.Text.Json;
using JustInTime.Core.Models;
using JustInTime.Infrastructure.Repositories;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TaskRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly TaskRepository _tasks = new(db.Factory);
    private readonly ExecutionHistoryRepository _history = new(db.Factory);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Сценарии 1 и 2: создание и сохранение задачи

    [Fact]
    public async Task Add_then_GetById_returns_saved_task()
    {
        var task = NewTask(Now.AddMinutes(15));
        task.IsRecurring = true;

        await _tasks.AddAsync(task);
        var loaded = await _tasks.GetByIdAsync(task.Id);

        Assert.NotNull(loaded);
        Assert.Equal(task.Title, loaded.Title);
        Assert.Equal(ActionType.SendMessage, loaded.ActionType);
        Assert.Equal(ScheduledTaskStatus.Pending, loaded.Status);
        Assert.Equal(Now.AddMinutes(15), loaded.ScheduledAt);
        Assert.Equal(DateTimeKind.Utc, loaded.ScheduledAt.Kind);
        Assert.Equal("Asia/Qyzylorda", loaded.TimeZone);
        Assert.True(loaded.IsRecurring);
        Assert.Equal(0, loaded.AttemptCount);

        // jsonb хранит JSON в своём формате, поэтому сравниваем содержимое, а не строку.
        using var json = JsonDocument.Parse(loaded.Payload);
        Assert.Equal("Анна", json.RootElement.GetProperty("recipient").GetString());
    }

    [Fact]
    public async Task GetById_returns_null_for_unknown_id()
    {
        Assert.Null(await _tasks.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CountActive_counts_only_unfinished_tasks()
    {
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.Pending));
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.Scheduled));
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.NeedsUserAction));
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.Completed));
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.Failed));
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.Cancelled));

        Assert.Equal(3, await _tasks.CountActiveAsync());
        Assert.Equal(1, await _tasks.CountByStatusAsync(ScheduledTaskStatus.Failed));
    }

    [Fact]
    public async Task GetNextScheduled_returns_earliest_waiting_task()
    {
        await _tasks.AddAsync(NewTask(Now.AddHours(3), title: "позже"));
        await _tasks.AddAsync(NewTask(Now.AddHours(1), title: "ближайшая"));
        await _tasks.AddAsync(NewTask(Now.AddMinutes(5), ScheduledTaskStatus.Cancelled, "отменена"));
        await _tasks.AddAsync(NewTask(Now.AddMinutes(1), ScheduledTaskStatus.Completed, "выполнена"));

        var next = await _tasks.GetNextScheduledAsync();

        Assert.NotNull(next);
        Assert.Equal("ближайшая", next.Title);
    }

    [Fact]
    public async Task GetList_filters_by_status_and_sorts_by_time()
    {
        await _tasks.AddAsync(NewTask(Now.AddHours(2), title: "вторая"));
        await _tasks.AddAsync(NewTask(Now.AddHours(1), title: "первая"));
        await _tasks.AddAsync(NewTask(Now, ScheduledTaskStatus.Cancelled, "отменена"));

        var list = await _tasks.GetListAsync([ScheduledTaskStatus.Pending]);

        Assert.Equal(["первая", "вторая"], list.Select(t => t.Title).ToArray());
    }

    // Сценарий 3: выполнение через 15 минут (запуск только когда время наступило)

    [Fact]
    public async Task TryStart_before_scheduled_time_returns_null_and_changes_nothing()
    {
        var task = NewTask(Now.AddMinutes(15));
        await _tasks.AddAsync(task);

        var entry = await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(14));

        Assert.Null(entry);
        var loaded = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.Pending, loaded!.Status);
        Assert.Equal(0, loaded.AttemptCount);
        Assert.Empty(await _history.GetByTaskAsync(task.Id));
    }

    [Fact]
    public async Task TryStart_at_scheduled_time_marks_running_and_creates_history()
    {
        var task = NewTask(Now.AddMinutes(15));
        await _tasks.AddAsync(task);
        var startAt = Now.AddMinutes(15);

        var entry = await _tasks.TryStartExecutionAsync(task.Id, startAt);

        Assert.NotNull(entry);
        Assert.Equal(1, entry.AttemptNumber);

        var loaded = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.Running, loaded!.Status);
        Assert.Equal(1, loaded.AttemptCount);
        Assert.Equal(startAt, loaded.LastExecutedAt);

        var history = await _history.GetByTaskAsync(task.Id);
        var saved = Assert.Single(history);
        Assert.Null(saved.Result);
        Assert.Null(saved.FinishedAt);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Running)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Failed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    [InlineData(ScheduledTaskStatus.NeedsUserAction)]
    public async Task TryStart_refuses_tasks_in_non_startable_status(ScheduledTaskStatus status)
    {
        var task = NewTask(Now, status);
        await _tasks.AddAsync(task);

        Assert.Null(await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)));
        Assert.Empty(await _history.GetByTaskAsync(task.Id));
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.WaitingForUnlock)]
    [InlineData(ScheduledTaskStatus.WaitingForNetwork)]
    public async Task TryStart_resumes_tasks_that_waited_for_conditions(ScheduledTaskStatus status)
    {
        var task = NewTask(Now, status);
        await _tasks.AddAsync(task);

        Assert.NotNull(await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)));
    }

    // Сценарий 13: повторное и одновременное выполнение

    [Fact]
    public async Task TryStart_called_concurrently_lets_only_one_caller_win()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1))));

        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r is not null);
        Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(1, (await _tasks.GetByIdAsync(task.Id))!.AttemptCount);
    }

    [Fact]
    public async Task TryStart_second_call_after_start_returns_null()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);

        Assert.NotNull(await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)));
        Assert.Null(await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)));
    }

    // Завершение попытки

    [Fact]
    public async Task Finish_success_completes_task_and_records_result()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);
        var entry = (await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)))!;
        var finishAt = Now.AddMinutes(2);

        var finished = await _tasks.FinishExecutionAsync(
            entry.Id, ExecutionResult.Success, null, ScheduledTaskStatus.Completed, finishAt);

        Assert.True(finished);
        Assert.Equal(ScheduledTaskStatus.Completed, (await _tasks.GetByIdAsync(task.Id))!.Status);

        var saved = Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal(ExecutionResult.Success, saved.Result);
        Assert.Equal(finishAt, saved.FinishedAt);
    }

    [Fact]
    public async Task Finish_failure_stores_error_in_task_and_history()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);
        var entry = (await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)))!;

        await _tasks.FinishExecutionAsync(
            entry.Id, ExecutionResult.Failed, "Контакт не найден", ScheduledTaskStatus.Failed, Now.AddMinutes(2));

        var loaded = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.Failed, loaded!.Status);
        Assert.Equal("Контакт не найден", loaded.LastError);

        var saved = Assert.Single(await _history.GetByTaskAsync(task.Id));
        Assert.Equal("Контакт не найден", saved.ErrorMessage);
    }

    [Fact]
    public async Task Finish_twice_for_same_attempt_returns_false_second_time()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);
        var entry = (await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)))!;

        var first = await _tasks.FinishExecutionAsync(
            entry.Id, ExecutionResult.Success, null, ScheduledTaskStatus.Completed, Now.AddMinutes(2));
        var second = await _tasks.FinishExecutionAsync(
            entry.Id, ExecutionResult.Failed, "поздний дубль", ScheduledTaskStatus.Failed, Now.AddMinutes(3));

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(ScheduledTaskStatus.Completed, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task Finish_cannot_leave_task_in_running_status()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _tasks.FinishExecutionAsync(
            Guid.NewGuid(), ExecutionResult.Success, null, ScheduledTaskStatus.Running, Now));
    }

    // Сценарий 14: неопределённый результат не должен приводить к повтору

    [Fact]
    public async Task Unknown_result_moves_task_to_needs_user_action_and_blocks_automatic_retry()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);
        var entry = (await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)))!;

        await _tasks.FinishExecutionAsync(
            entry.Id, ExecutionResult.Unknown, "Не удалось подтвердить отправку",
            ScheduledTaskStatus.NeedsUserAction, Now.AddMinutes(2));

        var loaded = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.NeedsUserAction, loaded!.Status);

        Assert.Null(await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(10)));
        Assert.Single(await _history.GetByTaskAsync(task.Id));
    }

    // Сценарий 4: отмена

    [Theory]
    [InlineData(ScheduledTaskStatus.Pending)]
    [InlineData(ScheduledTaskStatus.Scheduled)]
    [InlineData(ScheduledTaskStatus.WaitingForNetwork)]
    [InlineData(ScheduledTaskStatus.NeedsUserAction)]
    public async Task Cancel_works_for_unfinished_idle_tasks(ScheduledTaskStatus status)
    {
        var task = NewTask(Now.AddHours(1), status);
        await _tasks.AddAsync(task);

        Assert.True(await _tasks.TryCancelAsync(task.Id, Now));
        Assert.Equal(ScheduledTaskStatus.Cancelled, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Running)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Failed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    public async Task Cancel_refuses_running_and_finished_tasks(ScheduledTaskStatus status)
    {
        var task = NewTask(Now, status);
        await _tasks.AddAsync(task);

        Assert.False(await _tasks.TryCancelAsync(task.Id, Now));
        Assert.Equal(status, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task Cancelled_task_cannot_be_started()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);

        await _tasks.TryCancelAsync(task.Id, Now);

        Assert.Null(await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)));
    }

    [Fact]
    public async Task Cancel_unknown_task_returns_false()
    {
        Assert.False(await _tasks.TryCancelAsync(Guid.NewGuid(), Now));
    }

    // Сценарий 5: изменение времени и параметров

    [Fact]
    public async Task Update_changes_fields_and_returns_task_to_pending()
    {
        var task = NewTask(Now.AddHours(1), ScheduledTaskStatus.Scheduled);
        task.LastError = "старая ошибка";
        await _tasks.AddAsync(task);

        var update = new TaskUpdate(
            "Новое название", Now.AddHours(5), "Europe/Moscow", """{"recipient":"Борис"}""", true);

        Assert.True(await _tasks.TryUpdateAsync(task.Id, update, Now));

        var loaded = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal("Новое название", loaded!.Title);
        Assert.Equal(Now.AddHours(5), loaded.ScheduledAt);
        Assert.Equal("Europe/Moscow", loaded.TimeZone);
        Assert.True(loaded.IsRecurring);
        Assert.Equal(ScheduledTaskStatus.Pending, loaded.Status);
        Assert.Null(loaded.LastError);
        Assert.Equal(Now, loaded.UpdatedAt);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Running)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    public async Task Update_refuses_running_and_finished_tasks(ScheduledTaskStatus status)
    {
        var task = NewTask(Now, status, "исходное");
        await _tasks.AddAsync(task);

        var update = new TaskUpdate("новое", Now.AddHours(1), "UTC", "{}", false);

        Assert.False(await _tasks.TryUpdateAsync(task.Id, update, Now));
        Assert.Equal("исходное", (await _tasks.GetByIdAsync(task.Id))!.Title);
    }

    // Повтор задачи

    [Theory]
    [InlineData(ScheduledTaskStatus.Failed)]
    [InlineData(ScheduledTaskStatus.NeedsUserAction)]
    public async Task Retry_moves_failed_or_unknown_task_back_to_pending(ScheduledTaskStatus status)
    {
        var task = NewTask(Now.AddHours(-1), status);
        task.LastError = "сбой";
        await _tasks.AddAsync(task);

        Assert.True(await _tasks.TryRetryAsync(task.Id, Now, Now));

        var loaded = await _tasks.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.Pending, loaded!.Status);
        Assert.Equal(Now, loaded.ScheduledAt);
        Assert.Null(loaded.LastError);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Pending)]
    [InlineData(ScheduledTaskStatus.Scheduled)]
    [InlineData(ScheduledTaskStatus.Running)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    public async Task Retry_refuses_other_statuses(ScheduledTaskStatus status)
    {
        var task = NewTask(Now, status);
        await _tasks.AddAsync(task);

        Assert.False(await _tasks.TryRetryAsync(task.Id, Now, Now));
        Assert.Equal(status, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task Retry_keeps_previous_attempts_in_history()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);
        var first = (await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(1)))!;
        await _tasks.FinishExecutionAsync(
            first.Id, ExecutionResult.Failed, "сбой", ScheduledTaskStatus.Failed, Now.AddMinutes(2));

        Assert.True(await _tasks.TryRetryAsync(task.Id, Now.AddMinutes(3), Now.AddMinutes(3)));
        var second = await _tasks.TryStartExecutionAsync(task.Id, Now.AddMinutes(4));

        Assert.NotNull(second);
        Assert.Equal(2, second.AttemptNumber);

        var history = await _history.GetByTaskAsync(task.Id);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.Result == ExecutionResult.Failed && h.ErrorMessage == "сбой");
    }

    // Журнал

    [Fact]
    public async Task History_recent_is_sorted_newest_first_and_failures_are_filtered()
    {
        var task = NewTask(Now);
        await _tasks.AddAsync(task);

        await _history.AddAsync(new ExecutionHistory
        {
            TaskId = task.Id, StartedAt = Now.AddMinutes(1), Result = ExecutionResult.Success
        });
        await _history.AddAsync(new ExecutionHistory
        {
            TaskId = task.Id, StartedAt = Now.AddMinutes(3), Result = ExecutionResult.Failed, ErrorMessage = "сбой"
        });
        await _history.AddAsync(new ExecutionHistory
        {
            TaskId = task.Id, StartedAt = Now.AddMinutes(2), Result = ExecutionResult.Unknown
        });

        var recent = await _history.GetRecentAsync(10);
        var failures = await _history.GetRecentFailuresAsync(10);

        Assert.Equal(
            [Now.AddMinutes(3), Now.AddMinutes(2), Now.AddMinutes(1)],
            recent.Select(h => h.StartedAt).ToArray());
        Assert.Equal("Тестовая задача", recent[0].ScheduledTask!.Title);

        var failure = Assert.Single(failures);
        Assert.Equal("сбой", failure.ErrorMessage);
    }
}
