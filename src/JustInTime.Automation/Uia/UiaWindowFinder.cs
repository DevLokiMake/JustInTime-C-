using System.Diagnostics;
using System.Windows.Automation;

namespace JustInTime.Automation.Uia;

/// <summary>Ищет окна верхнего уровня через корневой элемент рабочего стола UI Automation.</summary>
public sealed class UiaWindowFinder : IWindowFinder
{
    public IReadOnlyList<WindowInfo> FindWindows(string processOrTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processOrTitle);

        var ownPid = Environment.ProcessId;
        var result = new List<WindowInfo>();

        AutomationElementCollection top;

        try
        {
            top = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException)
        {
            throw new UiAutomationException($"Не удалось получить список окон: {ex.Message}", ex);
        }

        foreach (AutomationElement native in top)
        {
            var element = new UiaElement(native);

            // Окно самого JustInTime в список не берём: автоматизация не должна трогать собственный интерфейс.
            if (element.ProcessId == ownPid || element.ProcessId == 0)
            {
                continue;
            }

            var title = element.Name;
            var process = ProcessNameOrEmpty(element.ProcessId);

            var matches = process.Contains(processOrTitle, StringComparison.OrdinalIgnoreCase)
                          || title.Contains(processOrTitle, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                result.Add(new WindowInfo(element, title, process, element.ProcessId));
            }
        }

        return result;
    }

    private static string ProcessNameOrEmpty(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Процесс успел завершиться.
            return string.Empty;
        }
    }
}
