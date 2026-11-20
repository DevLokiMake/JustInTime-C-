namespace JustInTime.Core.Services;

/// <summary>Какие файлы можно отдать на распознавание. Проверка до чтения файла, чтобы не грузить в память что попало.</summary>
public static class ImageFileRules
{
    public const long MaxBytes = 20 * 1024 * 1024;

    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>Возвращает текст ошибки или null, если файл подходит.</summary>
    public static string? Validate(string path, long length)
    {
        var extension = Path.GetExtension(path ?? string.Empty);

        if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return $"Формат «{extension}» не поддерживается. Подойдут: {string.Join(", ", Extensions)}.";
        }

        if (length <= 0)
        {
            return "Файл пустой.";
        }

        if (length > MaxBytes)
        {
            return $"Файл слишком большой (больше {MaxBytes / 1024 / 1024} МБ).";
        }

        return null;
    }
}
