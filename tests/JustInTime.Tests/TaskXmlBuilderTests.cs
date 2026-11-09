using System.Xml.Linq;
using JustInTime.Scheduler;

namespace JustInTime.Tests;

public sealed class TaskXmlBuilderTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static XDocument Build(
        string exe = @"C:\Program Files\A & B\JustInTime.exe", string description = "описание <x>")
    {
        var start = new DateTimeOffset(2026, 11, 8, 21, 30, 0, TimeSpan.FromHours(5));
        var xml = TaskXmlBuilder.Build(start, exe, "--run-due", Path.GetDirectoryName(exe) ?? "", description, TimeSpan.FromMinutes(15));
        return XDocument.Parse(xml);
    }

    private static string Value(XDocument doc, string name) => doc.Descendants(Ns + name).Single().Value;

    [Fact]
    public void Trigger_has_start_time_with_utc_offset()
    {
        Assert.Equal("2026-11-08T21:30:00+05:00", Value(Build(), "StartBoundary"));
    }

    [Fact]
    public void Negative_and_half_hour_offsets_are_formatted_correctly()
    {
        var text = TaskXmlBuilder.FormatStart(new DateTimeOffset(2026, 7, 1, 9, 5, 7, TimeSpan.FromHours(-3.5)));

        Assert.Equal("2026-07-01T09:05:07-03:30", text);
    }

    [Fact]
    public void Command_and_arguments_survive_special_characters()
    {
        var doc = Build();

        Assert.Equal(@"C:\Program Files\A & B\JustInTime.exe", Value(doc, "Command"));
        Assert.Equal("--run-due", Value(doc, "Arguments"));
        Assert.Equal("описание <x>", Value(doc, "Description"));
    }

    [Fact]
    public void Missed_start_is_run_when_computer_becomes_available()
    {
        Assert.Equal("true", Value(Build(), "StartWhenAvailable"));
    }

    [Fact]
    public void Runs_in_interactive_session_without_stored_credentials()
    {
        var doc = Build();

        Assert.Equal("InteractiveToken", Value(doc, "LogonType"));
        Assert.Equal("LeastPrivilege", Value(doc, "RunLevel"));
        Assert.Empty(doc.Descendants(Ns + "UserId"));
        Assert.Empty(doc.Descendants(Ns + "Password"));
    }

    [Fact]
    public void Execution_time_limit_is_an_xml_duration()
    {
        Assert.Equal("PT15M", Value(Build(), "ExecutionTimeLimit"));
    }

    [Fact]
    public void Executable_path_is_required()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            TaskXmlBuilder.Build(DateTimeOffset.UtcNow, " ", "", "", "d", TimeSpan.FromMinutes(1)));
    }
}
