using JustInTime.Automation;

namespace JustInTime.Tests;

/// <summary>Поддельный элемент интерфейса: дерево собирается в тесте, действия считаются.</summary>
internal sealed class FakeUiElement(
    string controlType = "Pane",
    string name = "",
    string automationId = "",
    string className = "") : IUiElement
{
    private readonly List<FakeUiElement> _children = [];

    public string Name { get; set; } = name;
    public string AutomationId { get; set; } = automationId;
    public string ControlType { get; set; } = controlType;
    public string ClassName { get; set; } = className;
    public int ProcessId { get; set; } = 100;
    public bool IsEnabled { get; set; } = true;
    public bool IsOffscreen { get; set; }
    public IReadOnlyList<string> Patterns { get; set; } = [];

    public string? Value { get; set; }
    public int InvokeCount { get; private set; }
    public bool ChildrenThrow { get; set; }

    /// <summary>Имитирует поле, которое принимает SetValue, но не меняет текст.</summary>
    public bool IgnoreSetValue { get; set; }

    public int SetValueCalls { get; private set; }

    /// <summary>Вызывается при каждом обходе потомков: позволяет менять дерево «на лету».</summary>
    public Action? OnGetChildren { get; set; }

    public Action? OnInvoke { get; set; }

    public IReadOnlyList<IUiElement> GetChildren()
    {
        OnGetChildren?.Invoke();

        if (ChildrenThrow)
        {
            throw new UiAutomationException("элемент исчез");
        }

        return _children;
    }

    public FakeUiElement Add(FakeUiElement child)
    {
        _children.Add(child);
        return child;
    }

    public void Invoke()
    {
        if (!Patterns.Contains("Invoke"))
        {
            throw new UiAutomationException("нет Invoke");
        }

        InvokeCount++;
        OnInvoke?.Invoke();
    }

    public void SetValue(string text)
    {
        if (!Patterns.Contains("Value"))
        {
            throw new UiAutomationException("нет Value");
        }

        SetValueCalls++;

        if (!IgnoreSetValue)
        {
            Value = text;
        }
    }

    public string GetValue() =>
        Patterns.Contains("Value") ? Value ?? string.Empty : throw new UiAutomationException("нет Value");

    public void SetFocus()
    {
    }
}

internal sealed class FakeWindowFinder(params WindowInfo[] windows) : IWindowFinder
{
    public IReadOnlyList<WindowInfo> FindWindows(string processOrTitle) =>
        windows.Where(w => w.Title.Contains(processOrTitle, StringComparison.OrdinalIgnoreCase)
                           || w.ProcessName.Contains(processOrTitle, StringComparison.OrdinalIgnoreCase)).ToList();
}

internal sealed class FakeDesktopSession(bool available = true, string reason = "компьютер заблокирован.")
    : JustInTime.Core.Interfaces.IDesktopSession
{
    public bool Available { get; set; } = available;

    public bool IsAvailable(out string why)
    {
        why = Available ? string.Empty : reason;
        return Available;
    }
}
