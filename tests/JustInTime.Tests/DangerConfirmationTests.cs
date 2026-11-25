using JustInTime.Core.Cli;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

/// <summary>Умное подтверждение: что считается опасным, политика с --force / -y, разбор команд и их выполнение.</summary>
public sealed class DangerConfirmationTests
{
    // Что опасно

    [Fact]
    public void Launching_a_program_needs_confirmation()
    {
        var reasons = DangerAssessor.ForTask(new LaunchProgramPayload(@"C:\Tools\clean.exe", null), recurring: false);

        Assert.Equal(DangerKind.LaunchProgram, Assert.Single(reasons).Kind);
    }

    [Fact]
    public void Recurring_message_needs_confirmation_but_a_single_one_does_not()
    {
        var message = new SendMessagePayload(MessengerApp.Telegram, "@anna_test", "Привет");

        Assert.Equal(DangerKind.RecurringMessage, Assert.Single(DangerAssessor.ForTask(message, recurring: true)).Kind);
        Assert.Empty(DangerAssessor.ForTask(message, recurring: false));
    }

    [Fact]
    public void Ordinary_actions_never_ask()
    {
        Assert.Empty(DangerAssessor.ForTask(new SystemSettingPayload(SystemSettingKind.Bluetooth, false), recurring: true));
        Assert.Empty(DangerAssessor.ForTask(new SetVolumePayload(70), recurring: true));
        Assert.Empty(DangerAssessor.ForTask(new OpenUrlPayload("https://example.com"), recurring: false));
        Assert.Empty(DangerAssessor.ForTask(null, recurring: false));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(50, true)]
    public void Cancelling_is_mass_from_two_tasks(int count, bool dangerous)
    {
        Assert.Equal(dangerous, DangerAssessor.ForCancelMany(count).Count == 1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Clearing_history_is_dangerous_only_when_there_is_something_to_delete(int entries, bool dangerous)
    {
        Assert.Equal(dangerous, DangerAssessor.ForClearHistory(entries).Count == 1);
    }

    [Fact]
    public void Force_switches_confirmation_off_and_no_reasons_never_ask()
    {
        var reasons = DangerAssessor.ForCancelMany(5);

        Assert.True(ConfirmationPolicy.NeedsConfirmation(reasons, force: false));
        Assert.False(ConfirmationPolicy.NeedsConfirmation(reasons, force: true));
        Assert.False(ConfirmationPolicy.NeedsConfirmation([], force: false));
    }

    // Разбор команд

    [Fact]
    public void No_command_means_a_normal_window_start_and_force_is_still_read()
    {
        var plain = CliParser.Parse(["--background"]);
        var forced = CliParser.Parse(["-y"]);

        Assert.Equal(CliCommand.None, plain.Command);
        Assert.False(plain.Force);
        Assert.Equal(CliCommand.None, forced.Command);
        Assert.True(forced.Force);
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("-y")]
    [InlineData("--FORCE")]
    [InlineData("-Y")]
    public void Both_force_spellings_are_accepted(string flag)
    {
        var request = CliParser.Parse(["--cancel-all", flag]);

        Assert.Equal(CliCommand.CancelAll, request.Command);
        Assert.True(request.Force);
    }

    [Fact]
    public void Cancel_takes_a_task_id()
    {
        var id = Guid.NewGuid();

        var request = CliParser.Parse(["--cancel", id.ToString()]);

        Assert.Equal(CliCommand.Cancel, request.Command);
        Assert.Equal(id, request.TaskId);
    }

    [Theory]
    [InlineData("--cancel")]
    [InlineData("--cancel", "не-гуид")]
    public void Cancel_without_a_valid_id_is_invalid(params string[] args)
    {
        Assert.Equal(CliCommand.Invalid, CliParser.Parse(args).Command);
    }

    [Fact]
    public void Two_commands_at_once_are_invalid()
    {
        Assert.Equal(CliCommand.Invalid, CliParser.Parse(["--list", "--cancel-all"]).Command);
    }

    [Fact]
    public void List_can_include_finished_tasks()
    {
        var request = CliParser.Parse(["--list", "--all"]);

        Assert.Equal(CliCommand.List, request.Command);
        Assert.True(request.IncludeAll);
    }

    // Выполнение команд

    private sealed class FakeConsole(bool canPrompt, params string[] answers) : ICliConsole
    {
        private readonly Queue<string> _answers = new(answers);

        public bool CanPrompt => canPrompt;

        public List<string> Lines { get; } = [];

        public int Prompts { get; private set; }

        public void WriteLine(string text) => Lines.Add(text);

        public void Write(string text)
        {
            Prompts++;
            Lines.Add(text);
        }

        public string? ReadLine() => _answers.Count > 0 ? _answers.Dequeue() : null;
    }

    private sealed class FakeMaintenance(int cancellable, int history) : IMaintenanceService
    {
        public int CancelAllCalls { get; private set; }

        public int ClearCalls { get; private set; }

        public Task<int> CountCancellableAsync(CancellationToken cancellationToken = default) => Task.FromResult(cancellable);

        public Task<BulkResult> CancelAllAsync(CancellationToken cancellationToken = default)
        {
            CancelAllCalls++;
            return Task.FromResult(new BulkResult(cancellable, 0, []));
        }

        public Task<int> CountFinishedHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(history);

        public Task<int> ClearHistoryAsync(CancellationToken cancellationToken = default)
        {
            ClearCalls++;
            return Task.FromResult(history);
        }
    }

    private sealed class FakeTaskService : ITaskService
    {
        public List<ScheduledTask> Tasks { get; } = [];

        public HashSet<Guid> CancelFails { get; } = [];

        public List<Guid> Cancelled { get; } = [];

        public Task<ServiceResult<ScheduledTask>> CreateAsync(TaskInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult> UpdateAsync(Guid id, TaskInput input, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult> CancelAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (CancelFails.Contains(id))
            {
                return Task.FromResult(ServiceResult.Fail("нельзя отменить"));
            }

            Cancelled.Add(id);
            return Task.FromResult(ServiceResult.Ok());
        }

        public Task<ServiceResult> RetryAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult> ResolveUnknownAsync(Guid id, bool delivered, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ScheduledTask?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));

        public Task<IReadOnlyList<ScheduledTask>> GetListAsync(
            IReadOnlyCollection<ScheduledTaskStatus>? statuses = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScheduledTask>>(
                Tasks.Where(t => statuses is null || statuses.Contains(t.Status)).ToList());
    }

    private static (CliRunner Runner, FakeMaintenance Maintenance, FakeTaskService Tasks) Create(
        FakeConsole console, int cancellable = 3, int history = 10)
    {
        var maintenance = new FakeMaintenance(cancellable, history);
        var tasks = new FakeTaskService();
        return (new CliRunner(tasks, maintenance, console), maintenance, tasks);
    }

    [Fact]
    public async Task Cancel_all_with_force_does_not_ask()
    {
        var console = new FakeConsole(canPrompt: true);
        var (runner, maintenance, _) = Create(console);

        var code = await runner.RunAsync(CliParser.Parse(["--cancel-all", "-y"]));

        Assert.Equal(CliExitCode.Success, code);
        Assert.Equal(0, console.Prompts);
        Assert.Equal(1, maintenance.CancelAllCalls);
    }

    [Fact]
    public async Task Cancel_all_without_force_and_without_a_console_does_nothing()
    {
        var console = new FakeConsole(canPrompt: false);
        var (runner, maintenance, _) = Create(console);

        var code = await runner.RunAsync(CliParser.Parse(["--cancel-all"]));

        Assert.Equal(CliExitCode.NotConfirmed, code);
        Assert.Equal(0, maintenance.CancelAllCalls);
    }

    [Theory]
    [InlineData("y")]
    [InlineData("YES")]
    [InlineData("да")]
    public async Task Cancel_all_runs_after_a_yes_answer(string answer)
    {
        var console = new FakeConsole(canPrompt: true, answer);
        var (runner, maintenance, _) = Create(console);

        var code = await runner.RunAsync(CliParser.Parse(["--cancel-all"]));

        Assert.Equal(CliExitCode.Success, code);
        Assert.Equal(1, console.Prompts);
        Assert.Equal(1, maintenance.CancelAllCalls);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("")]
    [InlineData("может быть")]
    public async Task Any_other_answer_is_a_no_and_changes_nothing(string answer)
    {
        var console = new FakeConsole(canPrompt: true, answer);
        var (runner, maintenance, _) = Create(console);

        var code = await runner.RunAsync(CliParser.Parse(["--cancel-all"]));

        Assert.Equal(CliExitCode.NotConfirmed, code);
        Assert.Equal(0, maintenance.CancelAllCalls);
    }

    [Fact]
    public async Task A_single_cancellable_task_is_not_a_mass_operation_and_needs_no_confirmation()
    {
        var console = new FakeConsole(canPrompt: false);
        var (runner, maintenance, _) = Create(console, cancellable: 1);

        var code = await runner.RunAsync(CliParser.Parse(["--cancel-all"]));

        Assert.Equal(CliExitCode.Success, code);
        Assert.Equal(1, maintenance.CancelAllCalls);
    }

    [Fact]
    public async Task Nothing_to_cancel_is_a_quiet_success()
    {
        var console = new FakeConsole(canPrompt: false);
        var (runner, maintenance, _) = Create(console, cancellable: 0);

        var code = await runner.RunAsync(CliParser.Parse(["--cancel-all"]));

        Assert.Equal(CliExitCode.Success, code);
        Assert.Equal(0, maintenance.CancelAllCalls);
    }

    [Fact]
    public async Task Clearing_history_is_always_confirmed_unless_forced()
    {
        var noConsole = new FakeConsole(canPrompt: false);
        var (blocked, blockedMaintenance, _) = Create(noConsole);
        var forcedConsole = new FakeConsole(canPrompt: false);
        var (forced, forcedMaintenance, _) = Create(forcedConsole);

        Assert.Equal(CliExitCode.NotConfirmed, await blocked.RunAsync(CliParser.Parse(["--clear-history"])));
        Assert.Equal(0, blockedMaintenance.ClearCalls);

        Assert.Equal(CliExitCode.Success, await forced.RunAsync(CliParser.Parse(["--clear-history", "--force"])));
        Assert.Equal(1, forcedMaintenance.ClearCalls);
    }

    [Fact]
    public async Task Cancel_by_id_needs_no_confirmation_and_reports_failures_with_an_error_code()
    {
        var console = new FakeConsole(canPrompt: false);
        var (runner, _, tasks) = Create(console);
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        tasks.CancelFails.Add(bad);

        Assert.Equal(CliExitCode.Success, await runner.RunAsync(CliParser.Parse(["--cancel", ok.ToString()])));
        Assert.Equal(CliExitCode.Error, await runner.RunAsync(CliParser.Parse(["--cancel", bad.ToString()])));
        Assert.Equal([ok], tasks.Cancelled);
    }

    [Fact]
    public async Task List_hides_finished_tasks_unless_all_is_given()
    {
        var console = new FakeConsole(canPrompt: false);
        var (runner, _, tasks) = Create(console);
        tasks.Tasks.Add(new ScheduledTask { Title = "Активная", Status = ScheduledTaskStatus.Scheduled, ScheduledAt = DateTime.UtcNow });
        tasks.Tasks.Add(new ScheduledTask { Title = "Готовая", Status = ScheduledTaskStatus.Completed, ScheduledAt = DateTime.UtcNow });

        await runner.RunAsync(CliParser.Parse(["--list"]));
        var short_ = string.Join("\n", console.Lines);
        console.Lines.Clear();
        await runner.RunAsync(CliParser.Parse(["--list", "--all"]));
        var full = string.Join("\n", console.Lines);

        Assert.Contains("Активная", short_);
        Assert.DoesNotContain("Готовая", short_);
        Assert.Contains("Готовая", full);
    }

    [Fact]
    public async Task Invalid_command_prints_usage_and_returns_code_2()
    {
        var console = new FakeConsole(canPrompt: false);
        var (runner, _, _) = Create(console);

        var code = await runner.RunAsync(CliParser.Parse(["--list", "--cancel-all"]));

        Assert.Equal(CliExitCode.InvalidCommand, code);
        Assert.NotEmpty(console.Lines);
    }

    // Массовая отмена в сервисе

    private sealed class NoHistory : IExecutionHistoryRepository
    {
        public Task AddAsync(ExecutionHistory entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ExecutionHistory>> GetByTaskAsync(Guid taskId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExecutionHistory>>([]);

        public Task<IReadOnlyList<ExecutionHistory>> GetRecentAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExecutionHistory>>([]);

        public Task<int> CountFinishedAsync(CancellationToken cancellationToken = default) => Task.FromResult(4);

        public Task<int> DeleteFinishedAsync(CancellationToken cancellationToken = default) => Task.FromResult(4);

        public Task<IReadOnlyList<ExecutionHistory>> GetRecentByResultAsync(
            IReadOnlyCollection<ExecutionResult> results, int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExecutionHistory>>([]);
    }

    [Fact]
    public async Task Cancel_all_counts_successes_and_failures_and_touches_only_cancellable_tasks()
    {
        var tasks = new FakeTaskService();
        var a = new ScheduledTask { Title = "A", Status = ScheduledTaskStatus.Scheduled };
        var b = new ScheduledTask { Title = "B", Status = ScheduledTaskStatus.Pending };
        var running = new ScheduledTask { Title = "Идёт", Status = ScheduledTaskStatus.Running };
        var done = new ScheduledTask { Title = "Готова", Status = ScheduledTaskStatus.Completed };
        tasks.Tasks.AddRange([a, b, running, done]);
        tasks.CancelFails.Add(b.Id);
        var maintenance = new MaintenanceService(tasks, new NoHistory());

        Assert.Equal(2, await maintenance.CountCancellableAsync());

        var result = await maintenance.CancelAllAsync();

        Assert.Equal(1, result.Done);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Problems);
        Assert.Equal([a.Id], tasks.Cancelled);
    }

    [Fact]
    public async Task History_operations_go_to_the_repository()
    {
        var maintenance = new MaintenanceService(new FakeTaskService(), new NoHistory());

        Assert.Equal(4, await maintenance.CountFinishedHistoryAsync());
        Assert.Equal(4, await maintenance.ClearHistoryAsync());
    }
}
