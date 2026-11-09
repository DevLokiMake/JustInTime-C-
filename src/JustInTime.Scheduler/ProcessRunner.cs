using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace JustInTime.Scheduler;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var encoding = GetConsoleEncoding();

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        // ArgumentList сам экранирует кавычки и пробелы, ручная сборка строки не нужна.
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Не удалось запустить {fileName}: {ex.Message}", ex);
        }

        // Оба потока читаем параллельно, иначе заполненный буфер одного из них подвесит процесс.
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"{fileName} не ответил за {timeout.TotalSeconds:0} с.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Процесс уже завершился или недоступен: убивать нечего.
        }
    }

    /// <summary>
    /// Консольные утилиты Windows пишут в OEM-кодировке (для русской Windows это cp866).
    /// Без неё сообщения об ошибках приходят в виде кракозябр.
    /// </summary>
    private static Encoding GetConsoleEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }
}
