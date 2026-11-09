using JustInTime.Core.Models;
using JustInTime.Scheduler;
using static JustInTime.Tests.TestData;

namespace JustInTime.Tests;

/// <summary>Регистратор с поддельным schtasks: проверяем, какие команды он отдаёт и как реагирует на сбои.</summary>
public sealed class WindowsTaskSchedulerRegistrarTests : IDisposable
{
    private readonly string _exe = Path.Combine(Path.GetTempPath(), $"jit-test-{Guid.NewGuid():N}.exe");
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly FakeProcessRunner _runner = new();
    private readonly WindowsTaskSchedulerRegistrar _registrar;

    public WindowsTaskSchedulerRegistrarTests()
    {
        File.WriteAllText(_exe, "stub");
        _registrar = new WindowsTaskSchedulerRegistrar(
            _runner, new WindowsSchedulerOptions { ExecutablePath = _exe }, _clock);
    }

    public void Dispose() => File.Delete(_exe);

    private static ScheduledTask NewScheduled(DateTime at) => new()
    {
        Title = "t",
        ScheduledAt = at,
        Status = ScheduledTaskStatus.Pending,
        Payload = "{}"
    };

    [Fact]
    public async Task Register_creates_task_from_xml_in_justintime_folder()
    {
        string? xml = null;
        _runner.Handler = args =>
        {
            xml = File.ReadAllText(args[Array.IndexOf(args, "/XML") + 1]);
            return new ProcessResult(0, "OK", "");
        };
        var task = NewScheduled(Now.AddMinutes(15));

        var result = await _registrar.RegisterAsync(task);

        Assert.True(result.Succeeded);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal("/Create", call[0]);
        Assert.Equal($@"\JustInTime\Task-{task.Id:D}", call[2]);
        Assert.Equal("/F", call[^1]);
        Assert.Contains("2026-11-06T12:15:00", xml);
        Assert.False(File.Exists(call[4]), "Временный XML должен быть удалён.");
    }

    [Fact]
    public async Task Register_moves_start_into_the_future_when_time_is_already_past()
    {
        string? xml = null;
        _runner.Handler = args =>
        {
            xml = File.ReadAllText(args[Array.IndexOf(args, "/XML") + 1]);
            return new ProcessResult(0, "", "");
        };

        await _registrar.RegisterAsync(NewScheduled(Now.AddMinutes(-5)));

        Assert.Contains("2026-11-06T12:00:10", xml);
    }

    [Fact]
    public async Task Register_reports_schtasks_error_text()
    {
        _runner.Handler = _ => new ProcessResult(1, "", "Отказано в доступе");

        var result = await _registrar.RegisterAsync(NewScheduled(Now.AddMinutes(5)));

        Assert.False(result.Succeeded);
        Assert.Equal("Отказано в доступе", result.Error);
    }

    [Fact]
    public async Task Register_does_not_throw_when_runner_fails()
    {
        _runner.Handler = _ => throw new TimeoutException("schtasks не ответил");

        var result = await _registrar.RegisterAsync(NewScheduled(Now.AddMinutes(5)));

        Assert.False(result.Succeeded);
        Assert.Contains("не ответил", result.Error);
    }

    [Fact]
    public async Task Register_fails_clearly_when_application_exe_is_missing()
    {
        var registrar = new WindowsTaskSchedulerRegistrar(
            _runner, new WindowsSchedulerOptions { ExecutablePath = @"C:\no\such\JustInTime.exe" }, _clock);

        var result = await registrar.RegisterAsync(NewScheduled(Now.AddMinutes(5)));

        Assert.False(result.Succeeded);
        Assert.Contains("Не найден исполняемый файл", result.Error);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Unregister_of_missing_task_is_success_without_delete()
    {
        _runner.Handler = args => args[0] == "/Query" ? new ProcessResult(1, "", "ERROR") : new ProcessResult(0, "", "");

        var result = await _registrar.UnregisterAsync(Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal("/Query", Assert.Single(_runner.Calls)[0]);
    }

    [Fact]
    public async Task Unregister_of_existing_task_deletes_it()
    {
        var result = await _registrar.UnregisterAsync(Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "/Query", "/Delete" }, _runner.Calls.Select(c => c[0]));
    }

    [Fact]
    public async Task Unregister_reports_delete_failure()
    {
        _runner.Handler = args => args[0] == "/Delete" ? new ProcessResult(5, "", "denied") : new ProcessResult(0, "", "");

        var result = await _registrar.UnregisterAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("denied", result.Error);
    }

    [Fact]
    public void ParseRegisteredIds_reads_only_justintime_tasks()
    {
        var mine = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var csv =
            $"\"\\JustInTime\\Task-{mine}\",\"N/A\",\"Ready\"\r\n" +
            $"\"\\Other\\Task-{foreign}\",\"N/A\",\"Ready\"\r\n" +
            "\"\\JustInTime\\Task-not-a-guid\",\"N/A\",\"Ready\"\r\n" +
            "мусор\r\n" +
            $"\"\\JustInTime\\Task-{mine}\",\"N/A\",\"Ready\"\r\n";

        var ids = WindowsTaskSchedulerRegistrar.ParseRegisteredIds(csv, "JustInTime", "Task-");

        Assert.Equal(new[] { mine }, ids.ToArray());
    }

    [Fact]
    public async Task Reconcile_registers_tasks_and_removes_only_stale_launches()
    {
        var toRegister = NewScheduled(Now.AddHours(1));
        var running = Guid.NewGuid();
        var stale = Guid.NewGuid();
        _runner.Handler = args => args[0] == "/Query" && args.Contains("/FO")
            ? new ProcessResult(0,
                $"\"\\JustInTime\\Task-{toRegister.Id}\",\"x\",\"y\"\r\n" +
                $"\"\\JustInTime\\Task-{running}\",\"x\",\"y\"\r\n" +
                $"\"\\JustInTime\\Task-{stale}\",\"x\",\"y\"\r\n", "")
            : new ProcessResult(0, "", "");

        var result = await _registrar.ReconcileAsync([toRegister], [running], removeStale: true);

        Assert.Equal(new[] { toRegister.Id }, result.RegisteredIds.ToArray());
        Assert.Equal(1, result.Removed);
        Assert.Empty(result.Errors);

        var deleted = _runner.Calls.Where(c => c[0] == "/Delete").ToList();
        Assert.Single(deleted);
        Assert.EndsWith(stale.ToString("D"), deleted[0][2]);
    }

    [Fact]
    public async Task Reconcile_without_remove_stale_does_not_list_or_delete()
    {
        var result = await _registrar.ReconcileAsync([NewScheduled(Now.AddHours(1))], [], removeStale: false);

        Assert.Equal(0, result.Removed);
        Assert.DoesNotContain(_runner.Calls, c => c[0] is "/Delete" or "/Query");
    }

    [Fact]
    public async Task Reconcile_deletes_nothing_when_listing_fails()
    {
        _runner.Handler = args => args[0] == "/Query" && args.Contains("/FO")
            ? new ProcessResult(1, "", "fail")
            : new ProcessResult(0, "", "");

        var result = await _registrar.ReconcileAsync([], [], removeStale: true);

        Assert.Equal(0, result.Removed);
        Assert.Single(result.Errors);
        Assert.DoesNotContain(_runner.Calls, c => c[0] == "/Delete");
    }

    [Fact]
    public async Task Reconcile_reports_tasks_that_could_not_be_registered_but_keeps_going()
    {
        var first = NewScheduled(Now.AddHours(1));
        var second = NewScheduled(Now.AddHours(2));
        _runner.Handler = args => args.Contains($@"\JustInTime\Task-{first.Id:D}")
            ? new ProcessResult(1, "", "boom")
            : new ProcessResult(0, "", "");

        var result = await _registrar.ReconcileAsync([first, second], [], removeStale: false);

        Assert.Equal(new[] { second.Id }, result.RegisteredIds.ToArray());
        Assert.Single(result.Errors);
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<string[]> Calls { get; } = new();

        public Func<string[], ProcessResult> Handler { get; set; } = _ => new ProcessResult(0, "", "");

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();
            Calls.Add(args);
            return Task.FromResult(Handler(args));
        }
    }
}
