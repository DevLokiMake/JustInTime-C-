namespace JustInTime.Scheduler;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>Текст ошибки для пользователя: stderr, а если он пуст, то stdout.</summary>
    public string ErrorText
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            text = text.Trim();
            return text.Length > 0 ? text : $"код выхода {ExitCode}";
        }
    }
}

/// <summary>Запуск внешней консольной программы с ожиданием результата. Нужен, чтобы тестировать регистратор без schtasks.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
