namespace JustInTime.Core.Models;

/// <summary>Ключи настроек. Все ключи только здесь, чтобы не разбрасывать строки по коду.</summary>
public static class SettingKeys
{
    /// <summary>Общий выключатель автоматизации. Если false, задачи не выполняются.</summary>
    public const string AutomationEnabled = "Automation.Enabled";

    /// <summary>
    /// На сколько минут задача может опоздать и всё равно выполниться. Если приложение или компьютер
    /// были выключены дольше, задача помечается как пропущенная, чтобы не сработать невпопад.
    /// </summary>
    public const string MissedGraceMinutes = "Scheduler.MissedGraceMinutes";

    /// <summary>Показывать ли уведомления Windows. По умолчанию да.</summary>
    public const string NotificationsEnabled = "Notifications.Enabled";

    /// <summary>Уведомлять ли об успешных задачах. По умолчанию нет: тихо, пока всё хорошо.</summary>
    public const string NotifyOnSuccess = "Notifications.OnSuccess";
}
