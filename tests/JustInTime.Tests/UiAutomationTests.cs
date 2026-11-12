using JustInTime.Automation;

namespace JustInTime.Tests;

public sealed class UiAutomationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jit-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static FakeUiElement Tree()
    {
        var window = new FakeUiElement("Window", "Чат");
        var list = window.Add(new FakeUiElement("List", "Список чатов"));
        list.Add(new FakeUiElement("ListItem", "Анна"));
        list.Add(new FakeUiElement("ListItem", "Анна Петрова"));
        var input = window.Add(new FakeUiElement("Edit", "Сообщение", "composer") { Patterns = ["Value"] });
        input.Value = "секретный черновик";
        window.Add(new FakeUiElement("Button", "Отправить", "send") { Patterns = ["Invoke"] });
        return window;
    }

    // ElementQuery

    [Fact]
    public void Empty_query_matches_nothing_and_cannot_search()
    {
        var query = new ElementQuery();

        Assert.True(query.IsEmpty);
        Assert.False(query.Matches(new FakeUiElement("Button", "x")));
        Assert.Throws<ArgumentException>(() => UiSearch.FindAll(Tree(), query));
    }

    [Fact]
    public void Query_requires_all_conditions_and_ignores_case()
    {
        var button = new FakeUiElement("Button", "Отправить", "send");

        Assert.True(new ElementQuery(Name: "отправить", ControlType: "button").Matches(button));
        Assert.True(new ElementQuery(NameContains: "прав").Matches(button));
        Assert.False(new ElementQuery(Name: "Отправить", ControlType: "Edit").Matches(button));
        Assert.False(new ElementQuery(AutomationId: "other").Matches(button));
    }

    // UiSearch

    [Fact]
    public void FindSingle_finds_unique_element_by_id()
    {
        var result = UiSearch.FindSingle(Tree(), new ElementQuery(AutomationId: "send"));

        Assert.True(result.IsFound);
        Assert.Equal("Отправить", result.Element!.Name);
    }

    [Fact]
    public void FindSingle_reports_ambiguous_when_several_match()
    {
        // «Анна» подходит и к «Анна», и к «Анна Петрова»: отправлять нельзя.
        var result = UiSearch.FindSingle(Tree(), new ElementQuery(NameContains: "Анна"));

        Assert.Equal(UiSearchStatus.Ambiguous, result.Status);
        Assert.Null(result.Element);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void FindSingle_exact_name_resolves_the_ambiguity()
    {
        var result = UiSearch.FindSingle(Tree(), new ElementQuery(Name: "Анна", ControlType: "ListItem"));

        Assert.True(result.IsFound);
    }

    [Fact]
    public void FindSingle_reports_not_found()
    {
        var result = UiSearch.FindSingle(Tree(), new ElementQuery(Name: "Нет такого"));

        Assert.Equal(UiSearchStatus.NotFound, result.Status);
    }

    [Fact]
    public void Search_does_not_include_the_root_itself()
    {
        var root = new FakeUiElement("Button", "корень");

        Assert.Empty(UiSearch.FindAll(root, new ElementQuery(Name: "корень")));
    }

    [Fact]
    public void Search_respects_depth_limit()
    {
        var root = new FakeUiElement("Window");
        var level1 = root.Add(new FakeUiElement("Pane"));
        var level2 = level1.Add(new FakeUiElement("Pane"));
        level2.Add(new FakeUiElement("Button", "глубоко"));

        Assert.Empty(UiSearch.FindAll(root, new ElementQuery(Name: "глубоко"), maxDepth: 2));
        Assert.Single(UiSearch.FindAll(root, new ElementQuery(Name: "глубоко"), maxDepth: 3));
    }

    [Fact]
    public void Search_respects_node_limit()
    {
        var root = new FakeUiElement("Window");
        for (var i = 0; i < 50; i++)
        {
            root.Add(new FakeUiElement("Button", "b" + i));
        }

        Assert.Equal(10, UiSearch.Descendants(root, maxNodes: 10).Count());
    }

    [Fact]
    public void Search_skips_branch_that_disappeared_instead_of_failing()
    {
        var root = new FakeUiElement("Window");
        root.Add(new FakeUiElement("Pane") { ChildrenThrow = true });
        root.Add(new FakeUiElement("Button", "жив"));

        Assert.True(UiSearch.FindSingle(root, new ElementQuery(Name: "жив")).IsFound);
    }

    [Fact]
    public async Task WaitForSingle_returns_element_that_appears_later()
    {
        var root = new FakeUiElement("Window");
        var polls = 0;
        root.OnGetChildren = () =>
        {
            if (++polls == 3)
            {
                root.Add(new FakeUiElement("Button", "появилась"));
            }
        };

        var result = await UiSearch.WaitForSingleAsync(
            root, new ElementQuery(Name: "появилась"),
            TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.True(result.IsFound);
        Assert.True(polls >= 3);
    }

    [Fact]
    public async Task WaitForSingle_times_out_with_not_found()
    {
        var result = await UiSearch.WaitForSingleAsync(
            new FakeUiElement("Window"), new ElementQuery(Name: "нет"),
            TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(20), CancellationToken.None);

        Assert.Equal(UiSearchStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task WaitForSingle_returns_ambiguous_immediately()
    {
        var result = await UiSearch.WaitForSingleAsync(
            Tree(), new ElementQuery(NameContains: "Анна"),
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.Equal(UiSearchStatus.Ambiguous, result.Status);
    }

    [Fact]
    public async Task WaitForSingle_honours_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UiSearch.WaitForSingleAsync(
            new FakeUiElement("Window"), new ElementQuery(Name: "нет"),
            TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10), cts.Token));
    }

    // Действия через шаблоны

    [Fact]
    public void Invoke_without_pattern_throws_expected_exception()
    {
        var label = new FakeUiElement("Text", "просто текст");

        Assert.Throws<UiAutomationException>(label.Invoke);
        Assert.Throws<UiAutomationException>(() => label.SetValue("x"));
    }

    // UiTreeDumper

    [Fact]
    public void Dump_lists_tree_with_indentation_and_properties()
    {
        var text = UiTreeDumper.Dump(Tree());
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("[Window] name=\"Чат\"", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("  [Button]") && l.Contains("id=\"send\"") && l.Contains("patterns=Invoke"));
        Assert.Contains(lines, l => l.StartsWith("    [ListItem] name=\"Анна\""));
    }

    [Fact]
    public void Dump_never_contains_values_of_input_fields()
    {
        Assert.DoesNotContain("секретный черновик", UiTreeDumper.Dump(Tree()));
    }

    [Fact]
    public void Dump_truncates_long_names_and_flattens_line_breaks()
    {
        var root = new FakeUiElement("Window", new string('я', 200) + "\nвторая строка");

        var line = UiTreeDumper.Dump(root, new DumpOptions(MaxNameLength: 20)).TrimEnd();

        Assert.DoesNotContain("вторая", line);
        Assert.Contains(new string('я', 20) + "…", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void Dump_stops_at_node_limit_and_says_so()
    {
        var root = new FakeUiElement("Window");
        for (var i = 0; i < 20; i++)
        {
            root.Add(new FakeUiElement("Button", "b" + i));
        }

        var text = UiTreeDumper.Dump(root, new DumpOptions(MaxNodes: 5));

        Assert.Contains("снимок обрезан", text);
        Assert.Equal(5, text.Split(Environment.NewLine).Count(l => l.Contains("[Window]") || l.Contains("[Button]")));
    }

    [Fact]
    public void Dump_survives_element_that_disappears()
    {
        var root = new FakeUiElement("Window");
        root.Add(new FakeUiElement("Pane") { ChildrenThrow = true });

        Assert.Contains("потомки недоступны", UiTreeDumper.Dump(root));
    }

    // UiInspector

    [Fact]
    public async Task Inspector_saves_one_file_per_window()
    {
        var finder = new FakeWindowFinder(
            new WindowInfo(Tree(), "WhatsApp", "WhatsApp.Root", 10),
            new WindowInfo(Tree(), "Telegram", "Telegram", 11));

        var result = await new UiInspector(finder, _dir).InspectAsync("whatsapp");

        Assert.True(result.Success);
        var file = Assert.Single(result.Files);
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("Окно: WhatsApp", content);
        Assert.Contains("[Button]", content);
        Assert.DoesNotContain("секретный черновик", content);
    }

    [Fact]
    public async Task Inspector_reports_when_window_is_not_found()
    {
        var result = await new UiInspector(new FakeWindowFinder(), _dir).InspectAsync("Telegram");

        Assert.False(result.Success);
        Assert.Empty(result.Files);
        Assert.Contains("не найдено", result.Message);
        Assert.False(Directory.Exists(_dir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Inspector_requires_a_query(string query)
    {
        var result = await new UiInspector(new FakeWindowFinder(), _dir).InspectAsync(query);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Inspector_sanitizes_file_name()
    {
        var finder = new FakeWindowFinder(new WindowInfo(Tree(), "a/b: окно", "p", 1));

        var result = await new UiInspector(finder, _dir).InspectAsync("a/b: окно");

        Assert.True(result.Success);
        Assert.Equal(_dir, Path.GetDirectoryName(Assert.Single(result.Files)));
    }
}
