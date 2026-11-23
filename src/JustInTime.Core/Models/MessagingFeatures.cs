namespace JustInTime.Core.Models;

/// <summary>
/// Переключатели возможностей отправки сообщений.
/// MultilineMessages: многострочные сообщения (перенос строки вводится как Shift+Enter). По умолчанию выключено,
/// пока ввод не проверен вручную на живых WhatsApp и Telegram. Для проверки поменяйте значение на true и пересоберите.
/// </summary>
public static class MessagingFeatures
{
    public const bool MultilineMessages = false;
}
