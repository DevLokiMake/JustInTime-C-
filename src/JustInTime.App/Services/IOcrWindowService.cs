namespace JustInTime.App.Services;

/// <summary>Окно «Фото в текст».</summary>
public interface IOcrWindowService
{
    /// <summary>
    /// Открывает окно (или выводит уже открытое на передний план).
    /// insertText: если задан, в окне появляется кнопка «Вставить в сообщение», готовый текст (одной строкой) передаётся сюда.
    /// </summary>
    void Show(Action<string>? insertText = null);
}
