namespace JustInTime.Core.Models;

public sealed record TextRecognitionResult(bool Success, string Text, string? Language, string? Error)
{
    public static TextRecognitionResult Ok(string text, string language) => new(true, text, language, null);

    public static TextRecognitionResult Fail(string error) => new(false, string.Empty, null, error);
}
