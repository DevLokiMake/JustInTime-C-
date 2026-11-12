using System.Text;

namespace JustInTime.Automation;

public sealed record InspectResult(bool Success, IReadOnlyList<string> Files, string Message);

/// <summary>Сохраняет снимки деревьев окон в текстовые файлы. Ничего не нажимает и не вводит.</summary>
public sealed class UiInspector(IWindowFinder finder, string outputDirectory, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string OutputDirectory => outputDirectory;

    public async Task<InspectResult> InspectAsync(string processOrTitle, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processOrTitle))
        {
            return new InspectResult(false, [], "Укажите часть названия окна или процесса, например WhatsApp или Telegram.");
        }

        var query = processOrTitle.Trim();

        try
        {
            // UI Automation работает через другие процессы и может быть медленным: не занимаем поток интерфейса.
            return await Task.Run(() => InspectCore(query, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new InspectResult(false, [], "Снимок прерван.");
        }
        catch (Exception ex) when (ex is UiAutomationException or IOException or UnauthorizedAccessException)
        {
            return new InspectResult(false, [], $"Не удалось сделать снимок: {ex.Message}");
        }
    }

    private InspectResult InspectCore(string query, CancellationToken cancellationToken)
    {
        var windows = finder.FindWindows(query);

        if (windows.Count == 0)
        {
            return new InspectResult(
                false, [], $"Окно «{query}» не найдено. Откройте программу (не сворачивайте в трей) и повторите.");
        }

        Directory.CreateDirectory(outputDirectory);

        var files = new List<string>();
        var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss");
        var index = 0;

        foreach (var info in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;

            var header = new StringBuilder()
                .AppendLine($"Окно: {info.Title}")
                .AppendLine($"Процесс: {info.ProcessName} (PID {info.ProcessId})")
                .AppendLine($"Снято: {_time.GetLocalNow():yyyy-MM-dd HH:mm:ss}")
                .AppendLine("Значения полей ввода не читаются, имена обрезаны до 60 символов.")
                .AppendLine(new string('-', 60));

            var body = UiTreeDumper.Dump(info.Window);

            var path = Path.Combine(outputDirectory, $"{stamp}-{SafeName(query)}-{index}.txt");
            File.WriteAllText(path, header + body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            files.Add(path);
        }

        return new InspectResult(true, files, $"Сохранено снимков: {files.Count}.");
    }

    private static string SafeName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(text.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return clean.Length > 30 ? clean[..30] : clean;
    }
}
