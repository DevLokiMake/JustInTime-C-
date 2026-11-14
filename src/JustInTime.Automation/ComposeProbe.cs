using System.Text;

namespace JustInTime.Automation;

public sealed record ProbeRequest(string WindowQuery, ElementQuery Composer, string Text);

public sealed record ProbeResult(bool Success, string Report, string? File);

/// <summary>
/// Диагностика ввода текста в окно мессенджера БЕЗ отправки. Находит поле ввода, записывает пробный текст через шаблон Value,
/// читает его обратно, сохраняет список кнопок (чтобы увидеть, появилась ли кнопка «Отправить») и очищает поле.
/// Не трогает поле, если там уже есть чужой черновик, и не работает при неоднозначном поиске.
/// </summary>
public sealed class ComposeProbe(
    IWindowFinder finder, string outputDirectory, TimeSpan? settleDelay = null, TimeProvider? time = null)
{
    private const int SearchDepth = 20;
    private const int SearchNodes = 20000;

    private readonly TimeSpan _settle = settleDelay ?? TimeSpan.FromMilliseconds(700);
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<ProbeResult> RunAsync(ProbeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WindowQuery))
        {
            return new ProbeResult(false, "Укажите часть названия окна или процесса.", null);
        }

        if (string.IsNullOrEmpty(request.Text))
        {
            return new ProbeResult(false, "Укажите пробный текст.", null);
        }

        try
        {
            return await Task.Run(() => RunCoreAsync(request, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ProbeResult(false, "Проверка прервана. Поле ввода могло остаться с пробным текстом: очистите его вручную.", null);
        }
        catch (Exception ex) when (ex is UiAutomationException or IOException or UnauthorizedAccessException)
        {
            return new ProbeResult(false, $"Проверка не удалась: {ex.Message}", null);
        }
    }

    private async Task<ProbeResult> RunCoreAsync(ProbeRequest request, CancellationToken ct)
    {
        var windows = finder.FindWindows(request.WindowQuery.Trim());

        if (windows.Count == 0)
        {
            return new ProbeResult(false, $"Окно «{request.WindowQuery}» не найдено. Откройте программу и повторите.", null);
        }

        // Окон может быть несколько (оболочка, браузер с похожим заголовком): берём только те, где есть поле ввода.
        var candidates = windows
            .Select(w => (Window: w, Matches: UiSearch.FindAll(w.Window, request.Composer, SearchDepth, SearchNodes)))
            .Where(x => x.Matches.Count > 0)
            .ToList();

        if (candidates.Count == 0)
        {
            return new ProbeResult(
                false,
                $"Поле ввода не найдено ({request.Composer}). Откройте чат (не пустой экран) и разверните окно.", null);
        }

        if (candidates.Count > 1 || candidates[0].Matches.Count > 1)
        {
            return new ProbeResult(
                false,
                "Найдено больше одного подходящего поля ввода. Автоматизация остановлена: нельзя угадывать, куда вводить текст.", null);
        }

        var (window, matches) = candidates[0];
        var composer = matches[0];
        var report = new StringBuilder()
            .AppendLine($"Окно: {window.Title}")
            .AppendLine($"Процесс: {window.ProcessName} (PID {window.ProcessId})")
            .AppendLine($"Поле ввода: [{composer.ControlType}] \"{composer.Name}\"")
            .AppendLine($"Пробный текст: \"{request.Text}\"");

        string before;

        try
        {
            before = composer.GetValue();
        }
        catch (UiAutomationException ex)
        {
            return Finish(false, report.AppendLine($"Чтение значения поля не удалось: {ex.Message}"), request, null);
        }

        if (before.Length > 0)
        {
            return Finish(
                false,
                report.AppendLine("В поле уже есть текст (возможно, черновик). Чужой текст не трогаем, очистите поле и повторите."),
                request, null);
        }

        try
        {
            composer.SetValue(request.Text);
        }
        catch (UiAutomationException ex)
        {
            return Finish(false, report.AppendLine($"Запись текста через Value не удалась: {ex.Message}"), request, null);
        }

        await Task.Delay(_settle, _time, ct);

        var readback = TryRead(composer);
        var matched = readback == request.Text;
        report.AppendLine(matched
            ? "Чтение обратно: текст совпал."
            : $"Чтение обратно: текст НЕ совпал (получено {(readback is null ? "ошибка чтения" : readback.Length + " симв.")}).");

        report.AppendLine().AppendLine("Кнопки и поля ввода окна после ввода текста:");
        report.Append(UiTreeDumper.Dump(
            window.Window, new DumpOptions(SearchDepth, SearchNodes, 60, InteractiveOnly: true)));

        // Убираем за собой в любом случае: черновик в чате пользователя не должен остаться.
        var cleared = false;

        try
        {
            composer.SetValue(string.Empty);
            await Task.Delay(_settle, _time, ct);
            cleared = TryRead(composer) is { Length: 0 };
        }
        catch (UiAutomationException)
        {
            // Результат очистки покажем в отчёте.
        }

        report.AppendLine().AppendLine(cleared
            ? "Поле очищено."
            : "ВНИМАНИЕ: поле не удалось очистить автоматически. Удалите пробный текст вручную.");

        return Finish(matched, report, request, composer.Name);
    }

    private static string? TryRead(IUiElement element)
    {
        try
        {
            return element.GetValue();
        }
        catch (UiAutomationException)
        {
            return null;
        }
    }

    private ProbeResult Finish(bool success, StringBuilder report, ProbeRequest request, string? _)
    {
        var text = report.ToString();

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, $"probe-{_time.GetLocalNow():yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return new ProbeResult(success, text, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProbeResult(success, text + $"\nФайл отчёта не сохранён: {ex.Message}", null);
        }
    }
}
