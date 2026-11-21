using System.Runtime.InteropServices.WindowsRuntime;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace JustInTime.SystemControl;

/// <summary>
/// Распознавание текста встроенным механизмом Windows (Windows.Media.Ocr). Работает без интернета.
/// Языки берутся из установленных в Windows языковых пакетов с пунктом «Распознавание текста».
/// </summary>
public sealed class WindowsTextRecognizer : ITextRecognizer
{
    // Порядок предпочтения: русский, затем английский, затем языки профиля пользователя.
    private static readonly string[] PreferredLanguages = ["ru", "en"];

    public IReadOnlyList<RecognizerLanguage> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages
            .Select(l => new RecognizerLanguage(l.LanguageTag, l.DisplayName))
            .ToList();

    public async Task<TextRecognitionResult> RecognizeAsync(
        byte[] image, string? languageTag = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image.Length == 0)
        {
            return TextRecognitionResult.Fail("Изображение пустое.");
        }

        try
        {
            var engine = languageTag is null ? CreateEngine() : CreateEngineFor(languageTag);

            if (engine is null && languageTag is not null)
            {
                return TextRecognitionResult.Fail($"Язык «{languageTag}» для распознавания в Windows не установлен.");
            }

            if (engine is null)
            {
                return TextRecognitionResult.Fail(
                    "В Windows не установлен язык для распознавания текста. Откройте Параметры, Время и язык, " +
                    "Язык и регион, добавьте язык (например, русский) и отметьте компонент «Распознавание текста».");
            }

            using var bitmap = await DecodeAsync(image, cancellationToken);
            var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);

            var recognized = TextRecognitionResult.Ok(result.Lines.Select(l => l.Text), engine.RecognizerLanguage.DisplayName);

            return recognized.Text.Length == 0
                ? TextRecognitionResult.Fail("Текст на изображении не найден. Попробуйте более чёткий снимок или крупнее.")
                : recognized;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return TextRecognitionResult.Fail($"Не удалось распознать изображение: {ex.Message}");
        }
    }

    private static OcrEngine? CreateEngineFor(string languageTag)
    {
        var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(
            l => l.LanguageTag.Equals(languageTag, StringComparison.OrdinalIgnoreCase));

        return language is null ? null : OcrEngine.TryCreateFromLanguage(language);
    }

    private static OcrEngine? CreateEngine()
    {
        var available = OcrEngine.AvailableRecognizerLanguages;

        foreach (var tag in PreferredLanguages)
        {
            var language = available.FirstOrDefault(l =>
                l.LanguageTag.Equals(tag, StringComparison.OrdinalIgnoreCase)
                || l.LanguageTag.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase));

            if (language is not null)
            {
                var engine = OcrEngine.TryCreateFromLanguage(language);

                if (engine is not null)
                {
                    return engine;
                }
            }
        }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static async Task<SoftwareBitmap> DecodeAsync(byte[] image, CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(image.AsBuffer()).AsTask(cancellationToken);
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        var transform = new BitmapTransform();
        var largest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);

        // Движок принимает изображение не больше MaxImageDimension по стороне: большое уменьшаем.
        if (largest > OcrEngine.MaxImageDimension)
        {
            var scale = (double)OcrEngine.MaxImageDimension / largest;
            transform.ScaledWidth = Math.Max(1, (uint)Math.Floor(decoder.PixelWidth * scale));
            transform.ScaledHeight = Math.Max(1, (uint)Math.Floor(decoder.PixelHeight * scale));
        }

        return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);
    }
}
