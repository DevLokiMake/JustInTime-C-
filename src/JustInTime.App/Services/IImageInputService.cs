namespace JustInTime.App.Services;

/// <summary>Результат получения изображения: байты файла, либо ошибка, либо отмена пользователем.</summary>
public sealed record ImageInputResult(byte[]? Bytes, string? Error = null, bool Cancelled = false)
{
    public static ImageInputResult Cancel() => new(null, null, true);

    public static ImageInputResult Fail(string error) => new(null, error);
}

/// <summary>Откуда берётся изображение для распознавания текста. Вынесено, чтобы ViewModels не зависели от диалогов и буфера обмена.</summary>
public interface IImageInputService
{
    ImageInputResult PickFile();

    ImageInputResult ReadFile(string path);

    ImageInputResult FromClipboard();

    /// <summary>Кладёт текст в буфер обмена. Возвращает текст ошибки или null.</summary>
    string? CopyText(string text);
}
