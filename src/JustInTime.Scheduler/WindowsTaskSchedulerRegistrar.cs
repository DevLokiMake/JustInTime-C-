using System.Text;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Scheduler;

/// <summary>
/// Регистрирует запуск каждой задачи в Планировщике заданий Windows через schtasks.exe.
/// Для каждой задачи создаётся своё одноразовое задание \JustInTime\Task-{id}, которое в нужный момент
/// запускает приложение с ключом --run-due. Само действие выполняет приложение, а не Планировщик:
/// так результат попадает в БД и историю.
/// </summary>
public sealed class WindowsTaskSchedulerRegistrar(
    IProcessRunner runner,
    WindowsSchedulerOptions options,
    TimeProvider time) : ITaskRegistrar
{
    private const string SchtasksExe = "schtasks.exe";

    public async Task<RegistrationResult> RegisterAsync(
        ScheduledTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        string? xmlPath = null;

        try
        {
            if (!File.Exists(options.ExecutablePath))
            {
                return RegistrationResult.Fail(
                    $"Не найден исполняемый файл приложения: {options.ExecutablePath}.");
            }

            var xml = TaskXmlBuilder.Build(
                ComputeStart(task),
                options.ExecutablePath,
                options.Arguments,
                Path.GetDirectoryName(options.ExecutablePath) ?? string.Empty,
                $"JustInTime: запланированная задача {task.Id:D}",
                options.ExecutionTimeLimit);

            xmlPath = Path.Combine(Path.GetTempPath(), $"justintime-{Guid.NewGuid():N}.xml");

            // schtasks принимает XML в UTF-16 с BOM. Encoding.Unicode пишет именно такой файл.
            await File.WriteAllTextAsync(xmlPath, xml, Encoding.Unicode, cancellationToken);

            var result = await runner.RunAsync(
                SchtasksExe,
                ["/Create", "/TN", options.GetTaskName(task.Id), "/XML", xmlPath, "/F"],
                options.CommandTimeout,
                cancellationToken);

            return result.Succeeded
                ? RegistrationResult.Ok()
                : RegistrationResult.Fail(result.ErrorText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RegistrationResult.Fail(ex.Message);
        }
        finally
        {
            TryDelete(xmlPath);
        }
    }

    public async Task<RegistrationResult> UnregisterAsync(
        Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var name = options.GetTaskName(taskId);

            // Текст ошибки schtasks зависит от языка Windows, поэтому «задания нет» определяем не по тексту,
            // а отдельным запросом: Query возвращает ненулевой код, если задания не существует.
            var exists = await runner.RunAsync(
                SchtasksExe, ["/Query", "/TN", name], options.CommandTimeout, cancellationToken);

            if (!exists.Succeeded)
            {
                return RegistrationResult.Ok();
            }

            var deleted = await runner.RunAsync(
                SchtasksExe, ["/Delete", "/TN", name, "/F"], options.CommandTimeout, cancellationToken);

            return deleted.Succeeded
                ? RegistrationResult.Ok()
                : RegistrationResult.Fail(deleted.ErrorText);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return RegistrationResult.Fail(ex.Message);
        }
    }

    public async Task<ReconcileResult> ReconcileAsync(
        IReadOnlyCollection<ScheduledTask> toRegister,
        IReadOnlyCollection<Guid> keepIds,
        bool removeStale,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var registered = new List<Guid>();
        var removed = 0;

        foreach (var task in toRegister)
        {
            var result = await RegisterAsync(task, cancellationToken);

            if (result.Succeeded)
            {
                registered.Add(task.Id);
            }
            else
            {
                errors.Add($"Задача «{task.Title}»: {result.Error}");
            }
        }

        if (removeStale)
        {
            var existing = await ListRegisteredIdsAsync(cancellationToken);

            if (existing.Error is not null)
            {
                // Если список получить не удалось, ничего не удаляем: лучше оставить лишнее, чем удалить нужное.
                errors.Add(existing.Error);
            }
            else
            {
                var keep = new HashSet<Guid>(keepIds);
                keep.UnionWith(toRegister.Select(t => t.Id));

                foreach (var id in existing.Ids.Where(id => !keep.Contains(id)))
                {
                    var result = await UnregisterAsync(id, cancellationToken);

                    if (result.Succeeded)
                    {
                        removed++;
                    }
                    else
                    {
                        errors.Add($"Не удалось удалить лишний запуск {id:D}: {result.Error}");
                    }
                }
            }
        }

        return new ReconcileResult(registered, removed, errors);
    }

    /// <summary>Идентификаторы задач, для которых в папке JustInTime уже есть задания.</summary>
    private async Task<(IReadOnlyList<Guid> Ids, string? Error)> ListRegisteredIdsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await runner.RunAsync(
                SchtasksExe, ["/Query", "/FO", "CSV", "/NH"], options.CommandTimeout, cancellationToken);

            if (!result.Succeeded)
            {
                return (Array.Empty<Guid>(), "Не удалось получить список заданий Планировщика: " + result.ErrorText);
            }

            return (ParseRegisteredIds(result.StandardOutput, options.FolderName, options.TaskNamePrefix), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (Array.Empty<Guid>(), "Не удалось получить список заданий Планировщика: " + ex.Message);
        }
    }

    /// <summary>
    /// Разбирает вывод schtasks /Query /FO CSV /NH. Нужна только первая колонка с полным именем задания,
    /// она не зависит от языка Windows. Строки не из нашей папки игнорируются.
    /// </summary>
    public static IReadOnlyList<Guid> ParseRegisteredIds(string csv, string folderName, string taskNamePrefix)
    {
        var prefix = $@"\{folderName}\{taskNamePrefix}";
        var ids = new List<Guid>();

        foreach (var rawLine in csv.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length < 2 || line[0] != '"')
            {
                continue;
            }

            var end = line.IndexOf('"', 1);

            if (end < 0)
            {
                continue;
            }

            var name = line[1..end];

            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(name.AsSpan(prefix.Length), out var id)
                && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private DateTimeOffset ComputeStart(ScheduledTask task)
    {
        var earliest = time.GetUtcNow().AddSeconds(options.MinLeadSeconds);
        var scheduled = new DateTimeOffset(DateTime.SpecifyKind(task.ScheduledAt, DateTimeKind.Utc));
        var startUtc = scheduled > earliest ? scheduled : earliest;

        // Смещение берём по поясу компьютера на этот момент (с учётом летнего времени).
        return TimeZoneInfo.ConvertTime(startUtc, time.LocalTimeZone);
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Временный файл не удалился: это не ошибка задачи, система очистит папку Temp сама.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
