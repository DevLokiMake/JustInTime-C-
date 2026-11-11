namespace JustInTime.Core.Models;

/// <summary>Настройки, которые пользователь меняет в разделе Settings.</summary>
public sealed record UserSettings(
    bool AutomationEnabled,
    bool NotificationsEnabled,
    bool NotifyOnSuccess,
    int MissedGraceMinutes);
