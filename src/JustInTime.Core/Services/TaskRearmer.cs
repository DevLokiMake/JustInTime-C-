using JustInTime.Core.Interfaces;
using JustInTime.Core.Localization;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>
/// Повторяющаяся задача после запуска получает время следующего запуска и снова встаёт в очередь.
/// Общий для движка (после выполнения) и сервиса (после того, как пользователь подтвердил неизвестный результат).
/// </summary>
/// <summary>Результат перевооружения: пояснение для журнала и признак «повтор остановлен».</summary>
public sealed record RearmResult(string Note, bool Stopped)
{
    public static RearmResult None { get; } = new(string.Empty, false);
}

public sealed class TaskRearmer(ITaskRepository tasks, ITaskRegistrar registrar, TimeProvider time)
{
    /// <summary>
    /// task: задача в том виде, как она была перед запуском (ScheduledAt означает предыдущий запуск).
    /// Возвращает пояснение для журнала; пусто, если задача не повторяющаяся или перевооружить не удалось по штатной причине.
    /// </summary>
    public async Task<RearmResult> RearmIfRecurringAsync(ScheduledTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (!task.IsRecurring)
        {
            return RearmResult.None;
        }

        var rule = RecurrenceSerializer.Deserialize(task.Recurrence);

        if (rule is null)
        {
            return new RearmResult(Loc.T("rearm_corrupt"), true);
        }

        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = time.LocalTimeZone;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var next = RecurrenceCalculator.Next(rule, zone, task.ScheduledAt, now);

        if (next is null)
        {
            return new RearmResult(Loc.T("rearm_no_next"), true);
        }

        if (!await tasks.TryRearmAsync(task.Id, next.Value, now, cancellationToken))
        {
            return RearmResult.None;
        }

        var current = await tasks.GetByIdAsync(task.Id, cancellationToken);

        if (current is not null)
        {
            var registration = await registrar.RegisterAsync(current, cancellationToken);

            if (registration.Succeeded)
            {
                await tasks.TryMarkScheduledAsync(task.Id, time.GetUtcNow().UtcDateTime, cancellationToken);
            }
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(next.Value, zone);
        return new RearmResult(Loc.T("rearm_next", local.ToString("dd.MM.yyyy HH:mm")), false);
    }
}
