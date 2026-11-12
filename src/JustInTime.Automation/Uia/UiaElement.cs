using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace JustInTime.Automation.Uia;

/// <summary>Адаптер IUiElement над System.Windows.Automation. Все сбои чужого интерфейса превращаются в UiAutomationException.</summary>
public sealed class UiaElement(AutomationElement element) : IUiElement
{
    private IReadOnlyList<string>? _patterns;

    public AutomationElement Native => element;

    public string Name => Read(() => element.Current.Name, string.Empty);

    public string AutomationId => Read(() => element.Current.AutomationId, string.Empty);

    public string ControlType => Read(
        () => Strip(element.Current.ControlType.ProgrammaticName, "ControlType."), string.Empty);

    public string ClassName => Read(() => element.Current.ClassName, string.Empty);

    public int ProcessId => Read(() => element.Current.ProcessId, 0);

    public bool IsEnabled => Read(() => element.Current.IsEnabled, false);

    public bool IsOffscreen => Read(() => element.Current.IsOffscreen, false);

    public IReadOnlyList<string> Patterns => _patterns ??= Read<IReadOnlyList<string>>(
        () => element.GetSupportedPatterns()
            .Select(p => p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", string.Empty))
            .ToList(),
        []);

    public IReadOnlyList<IUiElement> GetChildren() => Guard(() =>
    {
        var result = new List<IUiElement>();
        var walker = TreeWalker.ControlViewWalker;
        var child = walker.GetFirstChild(element);

        while (child is not null)
        {
            result.Add(new UiaElement(child));
            child = walker.GetNextSibling(child);
        }

        return (IReadOnlyList<IUiElement>)result;
    });

    public void Invoke() => Guard(() =>
    {
        if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            throw new UiAutomationException($"Элемент «{Name}» не поддерживает нажатие (Invoke).");
        }

        ((InvokePattern)pattern).Invoke();
        return 0;
    });

    public void SetValue(string text) => Guard(() =>
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            throw new UiAutomationException($"Элемент «{Name}» не поддерживает ввод текста (Value).");
        }

        var value = (ValuePattern)pattern;

        if (value.Current.IsReadOnly)
        {
            throw new UiAutomationException($"Элемент «{Name}» доступен только для чтения.");
        }

        value.SetValue(text);
        return 0;
    });

    public string GetValue() => Guard(() =>
    {
        if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
        {
            throw new UiAutomationException($"Элемент «{Name}» не поддерживает чтение значения (Value).");
        }

        return ((ValuePattern)pattern).Current.Value;
    });

    public void SetFocus() => Guard(() =>
    {
        element.SetFocus();
        return 0;
    });

    private static T Read<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsElementGone(ex))
        {
            // Элемент мог исчезнуть между обходом и чтением. Для свойств это не ошибка, берём пустое значение.
            return fallback;
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (UiAutomationException)
        {
            throw;
        }
        catch (Exception ex) when (IsElementGone(ex) || ex is InvalidOperationException or ArgumentException)
        {
            throw new UiAutomationException($"Операция с элементом не удалась: {ex.Message}", ex);
        }
    }

    private static bool IsElementGone(Exception ex) =>
        ex is ElementNotAvailableException or COMException or TimeoutException or InvalidOperationException;

    private static string Strip(string text, string prefix) =>
        prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
}
