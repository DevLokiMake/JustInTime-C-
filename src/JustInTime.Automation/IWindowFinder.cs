namespace JustInTime.Automation;

public sealed record WindowInfo(IUiElement Window, string Title, string ProcessName, int ProcessId);

/// <summary>Ищет окна верхнего уровня программ пользователя.</summary>
public interface IWindowFinder
{
    /// <summary>Окна, у которых имя процесса или заголовок содержит запрос (без учёта регистра).</summary>
    IReadOnlyList<WindowInfo> FindWindows(string processOrTitle);
}
