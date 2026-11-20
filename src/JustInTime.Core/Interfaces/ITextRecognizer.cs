using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

/// <summary>Распознавание текста на изображении. Всё выполняется на устройстве, изображение никуда не отправляется.</summary>
public interface ITextRecognizer
{
    /// <summary>image: содержимое файла изображения (PNG, JPEG, BMP, GIF, TIFF). Исключения наружу не бросает.</summary>
    Task<TextRecognitionResult> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default);
}
