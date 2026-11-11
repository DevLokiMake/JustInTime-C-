using JustInTime.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace JustInTime.App.Services;

/// <summary>
/// Уведомления Windows (тосты) через Microsoft.Toolkit.Uwp.Notifications. Пакет сам регистрирует
/// классическое приложение для уведомлений, поэтому работает и из окна, и из фонового запуска --run-due.
/// Любой сбой только пишется в журнал: уведомление не должно ломать выполнение задач.
/// </summary>
public sealed class ToastNotificationService(ILogger<ToastNotificationService> logger) : INotificationService
{
    public void Show(NotificationKind kind, string title, string message)
    {
        try
        {
            var prefix = kind switch
            {
                NotificationKind.Error => "Ошибка: ",
                NotificationKind.Warning => "Внимание: ",
                _ => string.Empty
            };

            new ToastContentBuilder()
                .AddText(prefix + title)
                .AddText(message)
                .Show();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось показать уведомление «{Title}»", title);
        }
    }
}
