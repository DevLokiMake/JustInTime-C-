using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>
/// Решает, о чём сообщить пользователю по итогам прогона планировщика. Сама ничего не показывает:
/// показ делает INotificationService. Работает одинаково из окна и из фонового запуска.
/// </summary>
public sealed class RunNotifier(INotificationService notifications, ISettingsStore settings)
{
    /// <summary>Не больше стольких отдельных уведомлений за прогон, дальше одно сводное.</summary>
    public const int MaxIndividual = 3;

    private const int MaxMessageLength = 220;

    public async Task NotifyAsync(RunReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.AutomationDisabled || !report.HasActivity)
        {
            return;
        }

        if (!await settings.GetBoolAsync(SettingKeys.NotificationsEnabled, true, cancellationToken))
        {
            return;
        }

        var onSuccess = await settings.GetBoolAsync(SettingKeys.NotifyOnSuccess, false, cancellationToken);

        if (report.RecoveredStuck > 0)
        {
            notifications.Show(
                NotificationKind.Warning,
                "Задачи прерваны",
                $"Задач с неизвестным результатом: {report.RecoveredStuck}. Проверьте результат вручную в разделе My Tasks.");
        }

        var problems = report.Entries.Where(IsProblem).ToList();
        Announce(problems, NotificationKind.Error, "Проблемы с задачами", "Задач с проблемами");

        if (onSuccess)
        {
            var done = report.Entries.Where(e => e.Result == ExecutionResult.Success).ToList();
            Announce(done, NotificationKind.Info, "Задачи выполнены", "Выполнено задач");
        }
    }

    /// <summary>
    /// Проблема это ошибка, неизвестный итог или пропуск. Ожидание разблокировки или интернета проблемой не считается:
    /// такая задача повторяется каждую минуту, и уведомления превратились бы в спам.
    /// </summary>
    private static bool IsProblem(TaskRunEntry entry) => entry.Result switch
    {
        ExecutionResult.Failed => true,
        ExecutionResult.Unknown => true,
        ExecutionResult.Skipped => entry.FinalStatus == ScheduledTaskStatus.Failed,
        _ => false
    };

    private void Announce(IReadOnlyList<TaskRunEntry> entries, NotificationKind kind, string summaryTitle, string summaryText)
    {
        if (entries.Count == 0)
        {
            return;
        }

        if (entries.Count > MaxIndividual)
        {
            notifications.Show(kind, summaryTitle, $"{summaryText}: {entries.Count}. Подробности в разделе History.");
            return;
        }

        foreach (var entry in entries)
        {
            notifications.Show(Kind(entry, kind), entry.Title, Describe(entry));
        }
    }

    private static NotificationKind Kind(TaskRunEntry entry, NotificationKind fallback) =>
        entry.Result == ExecutionResult.Unknown ? NotificationKind.Warning : fallback;

    private static string Describe(TaskRunEntry entry)
    {
        var text = entry.Result switch
        {
            ExecutionResult.Success => string.IsNullOrWhiteSpace(entry.Message) ? "Выполнена." : entry.Message,
            _ => string.IsNullOrWhiteSpace(entry.Message) ? "Подробности в разделе History." : entry.Message
        };

        return text!.Length <= MaxMessageLength ? text : text[..(MaxMessageLength - 1)] + "…";
    }
}
