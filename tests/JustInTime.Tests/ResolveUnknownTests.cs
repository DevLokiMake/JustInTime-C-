using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Infrastructure.Repositories;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

/// <summary>Пользователь подтверждает неизвестный результат отправки (реальный PostgreSQL).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ResolveUnknownTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly TaskRepository _tasks = new(db.Factory);
    private readonly FakeTaskRegistrar _registrar = new();

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private TaskService CreateService() => new(_tasks, _registrar, _clock);

    private async Task<ScheduledTask> AddAsync(ScheduledTaskStatus status, bool recurring = false)
    {
        var task = NewTask(Now.AddMinutes(-10), status);
        task.ActionType = ActionType.OpenUrl;
        task.Payload = TaskPayloadSerializer.Serialize(new OpenUrlPayload("https://example.com"));
        task.TimeZone = "UTC";
        task.IsRecurring = recurring;
        task.Recurrence = recurring
            ? RecurrenceSerializer.Serialize(new RecurrenceRule(RecurrenceKind.Daily, 1, TimeOfDayMinutes: 9 * 60))
            : null;
        await _tasks.AddAsync(task);
        return task;
    }

    [Fact]
    public async Task Delivered_marks_the_task_completed()
    {
        var task = await AddAsync(ScheduledTaskStatus.NeedsUserAction);

        var result = await CreateService().ResolveUnknownAsync(task.Id, delivered: true);

        Assert.True(result.Succeeded);
        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.Equal(ScheduledTaskStatus.Completed, saved.Status);
        Assert.Null(saved.LastError);
    }

    [Fact]
    public async Task Not_delivered_marks_the_task_failed_with_a_note()
    {
        var task = await AddAsync(ScheduledTaskStatus.NeedsUserAction);

        var result = await CreateService().ResolveUnknownAsync(task.Id, delivered: false);

        Assert.True(result.Succeeded);
        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.Equal(ScheduledTaskStatus.Failed, saved.Status);
        Assert.Contains("не было выполнено", saved.LastError);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.Pending)]
    [InlineData(ScheduledTaskStatus.Scheduled)]
    [InlineData(ScheduledTaskStatus.Completed)]
    [InlineData(ScheduledTaskStatus.Failed)]
    [InlineData(ScheduledTaskStatus.Cancelled)]
    public async Task Other_statuses_are_refused_and_left_untouched(ScheduledTaskStatus status)
    {
        var task = await AddAsync(status);

        var result = await CreateService().ResolveUnknownAsync(task.Id, delivered: true);

        Assert.False(result.Succeeded);
        Assert.Equal(status, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }

    [Fact]
    public async Task Unknown_task_id_is_refused()
    {
        var result = await CreateService().ResolveUnknownAsync(Guid.NewGuid(), delivered: true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Recurring_task_is_rearmed_after_the_decision()
    {
        var task = await AddAsync(ScheduledTaskStatus.NeedsUserAction, recurring: true);

        var result = await CreateService().ResolveUnknownAsync(task.Id, delivered: false);

        Assert.True(result.Succeeded);
        var saved = (await _tasks.GetByIdAsync(task.Id))!;
        Assert.True(saved.Status is ScheduledTaskStatus.Pending or ScheduledTaskStatus.Scheduled);
        Assert.Equal(new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc), saved.ScheduledAt);
        Assert.Contains(task.Id, _registrar.Registered);
    }

    [Fact]
    public async Task Second_confirmation_is_refused()
    {
        var task = await AddAsync(ScheduledTaskStatus.NeedsUserAction);
        var service = CreateService();

        Assert.True((await service.ResolveUnknownAsync(task.Id, delivered: true)).Succeeded);
        Assert.False((await service.ResolveUnknownAsync(task.Id, delivered: false)).Succeeded);
        Assert.Equal(ScheduledTaskStatus.Completed, (await _tasks.GetByIdAsync(task.Id))!.Status);
    }
}
