using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

public sealed class RunNotifierTests
{
    private sealed class FakeNotifications : INotificationService
    {
        public List<(NotificationKind Kind, string Title, string Message)> Shown { get; } = new();

        public void Show(NotificationKind kind, string title, string message) => Shown.Add((kind, title, message));
    }

    private readonly FakeNotifications _notifications = new();
    private readonly FakeSettingsStore _settings = new();

    private RunNotifier CreateNotifier() => new(_notifications, _settings);

    private static TaskRunEntry Entry(
        ExecutionResult result, ScheduledTaskStatus status, string title = "Задача", string? message = "сообщение") =>
        new(Guid.NewGuid(), title, status, result, message);

    private static RunReport Report(params TaskRunEntry[] entries) => new(false, entries, 0);

    [Fact]
    public async Task Failed_task_produces_an_error_notification_with_title_and_reason()
    {
        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Failed, ScheduledTaskStatus.Failed, "Выключить Wi-Fi", "нет адаптера")));

        var shown = Assert.Single(_notifications.Shown);
        Assert.Equal(NotificationKind.Error, shown.Kind);
        Assert.Equal("Выключить Wi-Fi", shown.Title);
        Assert.Equal("нет адаптера", shown.Message);
    }

    [Fact]
    public async Task Unknown_result_is_a_warning()
    {
        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Unknown, ScheduledTaskStatus.NeedsUserAction)));

        Assert.Equal(NotificationKind.Warning, Assert.Single(_notifications.Shown).Kind);
    }

    [Fact]
    public async Task Missed_task_is_reported_as_a_problem()
    {
        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Skipped, ScheduledTaskStatus.Failed, message: "Пропущена")));

        Assert.Equal("Пропущена", Assert.Single(_notifications.Shown).Message);
    }

    [Theory]
    [InlineData(ScheduledTaskStatus.WaitingForNetwork)]
    [InlineData(ScheduledTaskStatus.WaitingForUnlock)]
    public async Task Waiting_for_conditions_is_not_reported_because_it_repeats_every_minute(ScheduledTaskStatus status)
    {
        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Skipped, status)));

        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task Success_is_silent_by_default()
    {
        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Success, ScheduledTaskStatus.Completed)));

        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task Success_is_reported_when_enabled_in_settings()
    {
        _settings.Values[SettingKeys.NotifyOnSuccess] = "true";

        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Success, ScheduledTaskStatus.Completed, "Открыть сайт", null)));

        var shown = Assert.Single(_notifications.Shown);
        Assert.Equal(NotificationKind.Info, shown.Kind);
        Assert.Equal("Открыть сайт", shown.Title);
    }

    [Fact]
    public async Task Nothing_is_shown_when_notifications_are_disabled()
    {
        _settings.Values[SettingKeys.NotificationsEnabled] = "false";
        _settings.Values[SettingKeys.NotifyOnSuccess] = "true";

        await CreateNotifier().NotifyAsync(Report(
            Entry(ExecutionResult.Failed, ScheduledTaskStatus.Failed),
            Entry(ExecutionResult.Success, ScheduledTaskStatus.Completed)));

        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task Nothing_is_shown_when_automation_is_disabled_or_nothing_happened()
    {
        await CreateNotifier().NotifyAsync(RunReport.Disabled);
        await CreateNotifier().NotifyAsync(Report());

        Assert.Empty(_notifications.Shown);
    }

    [Fact]
    public async Task Many_problems_are_collapsed_into_one_summary()
    {
        var entries = Enumerable.Range(1, RunNotifier.MaxIndividual + 1)
            .Select(i => Entry(ExecutionResult.Failed, ScheduledTaskStatus.Failed, $"Задача {i}"))
            .ToArray();

        await CreateNotifier().NotifyAsync(Report(entries));

        var shown = Assert.Single(_notifications.Shown);
        Assert.Contains((RunNotifier.MaxIndividual + 1).ToString(), shown.Message);
    }

    [Fact]
    public async Task Up_to_the_limit_each_problem_gets_its_own_notification()
    {
        var entries = Enumerable.Range(1, RunNotifier.MaxIndividual)
            .Select(i => Entry(ExecutionResult.Failed, ScheduledTaskStatus.Failed, $"Задача {i}"))
            .ToArray();

        await CreateNotifier().NotifyAsync(Report(entries));

        Assert.Equal(RunNotifier.MaxIndividual, _notifications.Shown.Count);
    }

    [Fact]
    public async Task Stuck_tasks_are_reported_even_without_entries()
    {
        await CreateNotifier().NotifyAsync(new RunReport(false, Array.Empty<TaskRunEntry>(), 2));

        var shown = Assert.Single(_notifications.Shown);
        Assert.Equal(NotificationKind.Warning, shown.Kind);
        Assert.Contains("2", shown.Message);
    }

    [Fact]
    public async Task Very_long_error_text_is_shortened()
    {
        await CreateNotifier().NotifyAsync(Report(Entry(ExecutionResult.Failed, ScheduledTaskStatus.Failed, message: new string('x', 1000))));

        Assert.True(Assert.Single(_notifications.Shown).Message.Length <= 220);
    }
}
