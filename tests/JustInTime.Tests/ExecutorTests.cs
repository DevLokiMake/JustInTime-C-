using JustInTime.Core.Models;
using JustInTime.SystemControl;

namespace JustInTime.Tests;

/// <summary>
/// Проверяются только отказы: реальный запуск браузера или программы в автотестах не нужен.
/// Успешный путь проверяется вручную (см. README).
/// </summary>
public sealed class ExecutorTests
{
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    public async Task OpenUrl_refuses_everything_except_http_and_https(string url)
    {
        var outcome = await new OpenUrlExecutor().ExecuteAsync(new OpenUrlPayload(url), CancellationToken.None);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
    }

    [Fact]
    public async Task LaunchProgram_reports_missing_file_without_starting_anything()
    {
        var payload = new LaunchProgramPayload(@"C:\definitely\not\here\app.exe", null);

        var outcome = await new LaunchProgramExecutor().ExecuteAsync(payload, CancellationToken.None);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Contains("не найден", outcome.Message);
    }

    [Fact]
    public async Task Executor_rejects_payload_of_another_action_type()
    {
        var outcome = await new OpenUrlExecutor().ExecuteAsync(
            new LaunchProgramPayload("x.exe", null), CancellationToken.None);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
    }
}
