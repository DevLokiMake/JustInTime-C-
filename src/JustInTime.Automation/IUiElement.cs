namespace JustInTime.Automation;

/// <summary>
/// Элемент интерфейса чужой программы. Узкий интерфейс над UI Automation: поиск, дамп и проверки
/// можно тестировать на поддельном дереве, а адаптеры мессенджеров не зависят от System.Windows.Automation.
/// </summary>
public interface IUiElement
{
    string Name { get; }

    string AutomationId { get; }

    /// <summary>Тип элемента без префикса, например "Button", "Edit", "Document".</summary>
    string ControlType { get; }

    string ClassName { get; }

    int ProcessId { get; }

    bool IsEnabled { get; }

    bool IsOffscreen { get; }

    /// <summary>Поддерживаемые шаблоны UI Automation, например "Invoke", "Value".</summary>
    IReadOnlyList<string> Patterns { get; }

    IReadOnlyList<IUiElement> GetChildren();

    /// <summary>Нажимает элемент через шаблон Invoke. Бросает UiAutomationException, если шаблона нет или элемент исчез.</summary>
    void Invoke();

    /// <summary>Записывает текст через шаблон Value. Бросает UiAutomationException, если шаблона нет, поле только для чтения или элемент исчез.</summary>
    void SetValue(string text);

    /// <summary>Читает текст через шаблон Value. Бросает UiAutomationException, если шаблона нет.</summary>
    string GetValue();

    void SetFocus();

    /// <summary>
    /// Вводит текст как клавиатурные события Unicode. Перевод строки ('\n') вводится как Shift+Enter, а не Enter,
    /// чтобы мессенджер не отправил сообщение раньше времени; остальные управляющие символы запрещены. Перед вводом ставит фокус на элемент и проверяет,
    /// что фокус действительно на нём; иначе бросает UiAutomationException и ничего не вводит. Нужен для полей,
    /// которые игнорируют запись через Value (редакторы на веб-странице). Работает только на разблокированном рабочем столе.
    /// </summary>
    void TypeText(string text);
}

/// <summary>Ожидаемая ошибка при работе с чужим интерфейсом: нет шаблона, элемент исчез, окно закрылось.</summary>
public sealed class UiAutomationException(string message, Exception? inner = null) : Exception(message, inner);
