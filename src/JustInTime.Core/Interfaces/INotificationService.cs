namespace JustInTime.Core.Interfaces;

public enum NotificationKind
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Показ уведомления пользователю. Реализация не должна бросать исключения: сбой уведомления
/// не может ломать выполнение задач.
/// </summary>
public interface INotificationService
{
    void Show(NotificationKind kind, string title, string message);
}
