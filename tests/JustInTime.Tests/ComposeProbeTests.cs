using JustInTime.Automation;

namespace JustInTime.Tests;

public sealed class ComposeProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jit-probe-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static readonly ElementQuery Composer = new(NameContains: "Введите сообщение", ControlType: "Edit");

    private static (FakeUiElement Window, FakeUiElement Edit) Chat(string editValue = "")
    {
        var window = new FakeUiElement("Document", "WhatsApp");
        var edit = window.Add(new FakeUiElement("Edit", "Введите сообщение в чат 'Я'") { Patterns = ["Value"], Value = editValue });
        window.Add(new FakeUiElement("Button", "Голосовое сообщение") { Patterns = ["Invoke"] });
        window.Add(new FakeUiElement("DataItem", "секретное сообщение из чата"));
        return (window, edit);
    }

    private ComposeProbe Probe(params WindowInfo[] windows) =>
        new(new FakeWindowFinder(windows), _dir, TimeSpan.Zero);

    private static ProbeRequest Request(string text = "test123") => new("WhatsApp", Composer, text);

    [Fact]
    public async Task Probe_writes_reads_back_and_clears_without_touching_send()
    {
        var (window, edit) = Chat();

        var result = await Probe(new WindowInfo(window, "WhatsApp", "msedgewebview2", 1)).RunAsync(Request());

        Assert.True(result.Success);
        Assert.Equal("", edit.Value);
        Assert.Equal(2, edit.SetValueCalls);
        Assert.Contains("текст совпал", result.Report);
        Assert.Contains("Поле очищено", result.Report);
        Assert.NotNull(result.File);
        Assert.True(File.Exists(result.File));
    }

    [Fact]
    public async Task Report_lists_buttons_but_not_chat_content()
    {
        var (window, _) = Chat();

        var result = await Probe(new WindowInfo(window, "WhatsApp", "p", 1)).RunAsync(Request());

        Assert.Contains("[Button] name=\"Голосовое сообщение\"", result.Report);
        Assert.DoesNotContain("секретное сообщение", result.Report);
    }

    [Fact]
    public async Task Existing_draft_is_never_touched()
    {
        var (window, edit) = Chat("мой черновик");

        var result = await Probe(new WindowInfo(window, "WhatsApp", "p", 1)).RunAsync(Request());

        Assert.False(result.Success);
        Assert.Equal("мой черновик", edit.Value);
        Assert.Equal(0, edit.SetValueCalls);
    }

    [Fact]
    public async Task Field_that_ignores_text_is_reported_as_failure_and_cleared()
    {
        var (window, edit) = Chat();
        edit.IgnoreSetValue = true;

        var result = await Probe(new WindowInfo(window, "WhatsApp", "p", 1)).RunAsync(Request());

        Assert.False(result.Success);
        Assert.Contains("НЕ совпал", result.Report);
    }

    [Fact]
    public async Task Missing_composer_is_reported()
    {
        var window = new FakeUiElement("Document", "WhatsApp");

        var result = await Probe(new WindowInfo(window, "WhatsApp", "p", 1)).RunAsync(Request());

        Assert.False(result.Success);
        Assert.Contains("не найдено", result.Report);
    }

    [Fact]
    public async Task Two_matching_composers_stop_the_probe()
    {
        var (window, edit) = Chat();
        var other = window.Add(new FakeUiElement("Edit", "Введите сообщение в чат 'Другой'") { Patterns = ["Value"] });

        var result = await Probe(new WindowInfo(window, "WhatsApp", "p", 1)).RunAsync(Request());

        Assert.False(result.Success);
        Assert.Equal(0, edit.SetValueCalls);
        Assert.Equal(0, other.SetValueCalls);
        Assert.Contains("больше одного", result.Report);
    }

    [Fact]
    public async Task Only_windows_that_contain_the_composer_are_used()
    {
        var shell = new FakeUiElement("Window", "WhatsApp");
        var (content, edit) = Chat();

        var result = await Probe(
            new WindowInfo(shell, "WhatsApp", "WhatsApp.Root", 1),
            new WindowInfo(content, "WhatsApp", "msedgewebview2", 2)).RunAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(2, edit.SetValueCalls);
    }

    [Fact]
    public async Task Same_composer_in_two_windows_stops_the_probe()
    {
        var (a, editA) = Chat();
        var (b, editB) = Chat();

        var result = await Probe(
            new WindowInfo(a, "WhatsApp", "p", 1), new WindowInfo(b, "WhatsApp", "p", 2)).RunAsync(Request());

        Assert.False(result.Success);
        Assert.Equal(0, editA.SetValueCalls + editB.SetValueCalls);
    }

    [Fact]
    public async Task Composer_without_value_pattern_fails_cleanly()
    {
        var window = new FakeUiElement("Document", "WhatsApp");
        window.Add(new FakeUiElement("Edit", "Введите сообщение"));

        var result = await Probe(new WindowInfo(window, "WhatsApp", "p", 1)).RunAsync(Request());

        Assert.False(result.Success);
        Assert.Contains("Value", result.Report);
    }

    [Fact]
    public async Task Empty_window_or_text_is_rejected()
    {
        var probe = Probe();

        Assert.False((await probe.RunAsync(new ProbeRequest("", Composer, "x"))).Success);
        Assert.False((await probe.RunAsync(new ProbeRequest("WhatsApp", Composer, ""))).Success);
    }

    [Fact]
    public void Interactive_dump_skips_lists_and_keeps_controls()
    {
        var (window, _) = Chat();

        var text = UiTreeDumper.Dump(window, new DumpOptions(InteractiveOnly: true));

        Assert.Contains("[Edit]", text);
        Assert.Contains("[Button]", text);
        Assert.DoesNotContain("[DataItem]", text);
        Assert.DoesNotContain("[Document]", text);
    }
}
