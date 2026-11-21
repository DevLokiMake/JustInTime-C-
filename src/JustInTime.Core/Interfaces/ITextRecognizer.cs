using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

/// <summary>Распознавание текста на изображении. Всё выполняется на устройстве, изображение никуда не отправляется.</summary>
public interface ITextRecognizer
{
    /// <summary>Языки, для которых в системе установлено распознавание.</summary>
    IReadOnlyList<RecognizerLanguage> AvailableLanguages { get; }

    /// <summary>
    /// image: содержимое файла изображения (PNG, JPEG, BMP, GIF, TIFF). languageTag: null означает автоматический выбор.
    /// Исключения наружу не бросает (кроме отмены).
    /// </summary>
    Task<TextRecognitionResult> RecognizeAsync(
        byte[] image, string? languageTag = null, CancellationToken cancellationToken = default);
}
