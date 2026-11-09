namespace JustInTime.Scheduler;

public sealed class WindowsSchedulerOptions
{
    /// <summary>Папка в Планировщике заданий, где лежат все запуски JustInTime.</summary>
    public string FolderName { get; init; } = "JustInTime";

    /// <summary>Полный путь к exe приложения. Планировщик запустит именно его.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>Аргументы запуска: приложение выполняет просроченные задачи и закрывается без окна.</summary>
    public string Arguments { get; init; } = "--run-due";

    /// <summary>Сколько ждать ответа schtasks.exe.</summary>
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Если время запуска уже прошло или совсем близко, запуск ставится на столько секунд вперёд.</summary>
    public int MinLeadSeconds { get; init; } = 10;

    /// <summary>Лимит времени на один запуск процесса планировщиком.</summary>
    public TimeSpan ExecutionTimeLimit { get; init; } = TimeSpan.FromMinutes(15);

    public string TaskNamePrefix => "Task-";

    public string GetTaskName(Guid taskId) => $@"\{FolderName}\{TaskNamePrefix}{taskId:D}";
}
