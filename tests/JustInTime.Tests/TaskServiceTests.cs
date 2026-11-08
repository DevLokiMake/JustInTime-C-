using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Infrastructure.Repositories;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

/// <summary>Сервис задач вместе с реальным репозиторием и PostgreSQL.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TaskServiceTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly TaskRepository _repository = new(db.Factory);
    private readonly ExecutionHistoryRepository _history = new(db.Factory);
    private TaskService _service = null!;

    public Task InitializeAsync()
    {
        _service = new TaskService(_repository, _clock);
        return db.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static TaskInput MessageInput(
        string title = "  Позвонить  ", TaskSchedule? schedule = null, string recipient = "Анна") =>
        new(title, new SendMessagePayload(MessengerApp.WhatsApp, recipient, "Привет"), schedule ?? new AfterDelaySchedule(15));

    // Сценарии 1 и 2: создание и сохранение

    [Fact]
    public async Task Create_saves_task_with_all_fields()
    {
        var result = await _service.CreateAsync(MessageInput());

        Assert.True(result.Succeeded);

        var saved = await _repository.GetByIdAsync(result.Value!.Id);
        Assert.NotNull(saved);
        Assert.Equal("Позвонить", saved.Title);
        Assert.Equal(ActionType.SendMessage, saved.ActionType);
        Assert.Equal(ScheduledTaskStatus.Pending, saved.Status);
        Assert.Equal(Now.AddMinutes(15), saved.ScheduledAt);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(TimeZoneInfo.Utc.Id, saved.TimeZone);

        var payload = TaskPayloadSerializer.Deserialize(saved.ActionType, saved.Payload);
        Assert.Equal(new SendMessagePayload(MessengerApp.WhatsApp, "Анна", "Привет"), payload);
    }

    [Fact]
    public async Task Create_with_invalid_input_saves_nothing_and_reports_every_problem()
    {
        var input = new TaskInput("", new OpenUrlPayload("javascript:alert(1)"), new AfterDelaySchedule(0));

        var result = await _service.CreateAsync(input);

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Errors.Count);
        Assert.Empty(await _repository.GetListAsync());
    }

    [Fact]
    public async Task Create_in_the_past_is_rejected()
    {
        var past = new AtLocalTimeSchedule(Now.AddHours(-1));

        var result = await _service.CreateAsync(MessageInput(schedule: past));

        Assert.False(result.Succeeded);
        Assert.Empty(await _repository.GetListAsync());
    }

    // Сценарий 3: выполнение через 15 минут

    [Fact]
    public async Task Task_created_for_15_minutes_becomes_startable_only_when_time_comes()
    {
        var task = (await _service.CreateAsync(MessageInput())).Value!;

        Assert.Null(await _repository.TryStartExecutionAsync(task.Id, Now.AddMinutes(14)));
        Assert.NotNull(await _repository.TryStartExecutionAsync(task.Id, Now.AddMinutes(15)));
    }

    // Сценарий 5: изменение времени и параметров

    [Fact]
    public async Task Update_changes_time_and_parameters_and_keeps_task_pending()
    {
        var task = (await _service.CreateAsync(MessageInput())).Value!;
        var newInput = new TaskInput(
            "Новое название",
            new SendMessagePayload(MessengerApp.Telegram, "Борис", "Эй"),
            new AfterDelaySchedule(120));

        var result = await _service.UpdateAsync(task.Id, newInput);

        Assert.True(result.Succeeded);

        var saved = await _repository.GetByIdAsync(task.Id);
        Assert.Equal("Новое название", saved!.Title);
        Assert.Equal(Now.AddMinutes(120), saved.ScheduledAt);
        Assert.Equal(ScheduledTaskStatus.Pending, saved.Status);
        Assert.Equal(
            new SendMessagePayload(MessengerApp.Telegram, "Борис", "Эй"),
            TaskPayloadSerializer.Deserialize(saved.ActionType, saved.Payload));
    }

    [Fact]
    public async Task Update_cannot_change_action_type()
    {
        var task = (await _service.CreateAsync(MessageInput())).Value!;
        var otherType = new TaskInput("x", new OpenUrlPayload("https://example.com"), new AfterDelaySchedule(5));

        var result = await _service.UpdateAsync(task.Id, otherType);

        Assert.False(result.Succeeded);
        Assert.Contains("Тип действия", result.ErrorText);
        Assert.Equal(ActionType.SendMessage, (await _repository.GetByIdAsync(task.Id))!.ActionType);
    }

    [Fact]
    public async Task Update_unknown_task_reports_not_found()
    {
        var result = await _service.UpdateAsync(Guid.NewGuid(), MessageInput());

        Assert.False(result.Succeeded);
        Assert.Contains("не найдена", result.ErrorText);
    }

    [Fact]
    public async Task Update_running_task_is_refused_with_status_in_message()
    {
        var task = NewTask(Now.AddMinutes(1), ScheduledTaskStatus.Running);
        await _repository.AddAsync(task);

        var result = await _service.UpdateAsync(task.Id, MessageInput());

        Assert.False(result.Succeeded);
        Assert.Contains("Выполняется", result.ErrorText);
    }

    // Сценарий 4: отмена

    [Fact]
    public async Task Cancel_marks_task_cancelled_and_it_can_no_longer_start()
    {
        var task = (await _service.CreateAsync(MessageInput())).Value!;

        var result = await _service.CancelAsync(task.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(ScheduledTaskStatus.Cancelled, (await _repository.GetByIdAsync(task.Id))!.Status);
        Assert.Null(await _repository.TryStartExecutionAsync(task.Id, Now.AddHours(1)));
    }

    [Fact]
    public async Task Cancel_twice_is_refused_the_second_time()
    {
        var task = (await _service.CreateAsync(MessageInput())).Value!;

        await _service.CancelAsync(task.Id);
        var second = await _service.CancelAsync(task.Id);

        Assert.False(second.Succeeded);
        Assert.Contains("Отменена", second.ErrorText);
    }

    // Повтор

    [Theory]
    [InlineData(ScheduledTaskStatus.Failed)]
    [InlineData(ScheduledTaskStatus.NeedsUserAction)]
    public async Task Retry_returns_failed_or_unknown_task_to_queue_for_immediate_run(ScheduledTaskStatus status)
    {
        var task = NewTask(Now.AddHours(-2), status);
        task.LastError = "сбой";
        await _repository.AddAsync(task);

        var result = await _service.RetryAsync(task.Id);

        Assert.True(result.Succeeded);

        var saved = await _repository.GetByIdAsync(task.Id);
        Assert.Equal(ScheduledTaskStatus.Pending, saved!.Status);
        Assert.Equal(Now, saved.ScheduledAt);
        Assert.Null(saved.LastError);
        Assert.NotNull(await _repository.TryStartExecutionAsync(task.Id, Now));
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Pending)]
    [InlineData(ScheduledTaskStatus.Running)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    public async Task Retry_is_refused_for_other_statuses(ScheduledTaskStatus status)
    {
        var task = NewTask(Now, status);
        await _repository.AddAsync(task);

        var result = await _service.RetryAsync(task.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(status, (await _repository.GetByIdAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task Retry_unknown_task_reports_not_found()
    {
        var result = await _service.RetryAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Contains("не найдена", result.ErrorText);
    }

    // Список

    [Fact]
    public async Task GetList_returns_tasks_filtered_by_status()
    {
        var active = (await _service.CreateAsync(MessageInput(title: "активная"))).Value!;
        var cancelled = (await _service.CreateAsync(MessageInput(title: "отменённая"))).Value!;
        await _service.CancelAsync(cancelled.Id);

        var activeList = await _service.GetListAsync(ScheduledTaskStatuses.Active);
        var finishedList = await _service.GetListAsync(ScheduledTaskStatuses.Finished);

        Assert.Equal([active.Id], activeList.Select(t => t.Id).ToArray());
        Assert.Equal([cancelled.Id], finishedList.Select(t => t.Id).ToArray());
        Assert.Equal(2, (await _service.GetListAsync()).Count);
    }
}
