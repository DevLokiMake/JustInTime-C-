using JustInTime.Core.Services;

namespace JustInTime.Core.Models;

/// <summary>Результат распознавания. Lines: строки как на картинке; Text: то же одной строкой (для однострочных сообщений).</summary>
public sealed record TextRecognitionResult(
    bool Success, IReadOnlyList<string> Lines, string Text, string? Language, string? Error)
{
    public static TextRecognitionResult Ok(IEnumerable<string> lines, string language)
    {
        var cleaned = lines.Select(l => OcrTextNormalizer.ToSingleLine([l])).Where(l => l.Length > 0).ToList();
        return new(true, cleaned, OcrTextNormalizer.ToSingleLine(cleaned), language, null);
    }

    public static TextRecognitionResult Fail(string error) => new(false, [], string.Empty, null, error);
}
