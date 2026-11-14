using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Messaging;

public sealed record MessageRequest(ParsedRecipient Recipient, string Text);

public enum SendStatus
{
    /// <summary>Сообщение появилось в чате как отправленное. Это не подтверждение доставки.</summary>
    Sent,

    /// <summary>Сообщение точно не отправлено (нет программы, не найден или неоднозначен получатель, поле не найдено). Повтор безопасен.</summary>
    NotSent,

    /// <summary>Неизвестно, ушло ли сообщение (интерфейс изменился после нажатия отправки, сбой при проверке). Повтор запрещён.</summary>
    Uncertain
}

public sealed record SendResult(SendStatus Status, string Message)
{
    public static SendResult Sent(string message = "Сообщение отправлено из приложения. Доставка получателю не проверяется.") =>
        new(SendStatus.Sent, message);

    public static SendResult NotSent(string message) => new(SendStatus.NotSent, message);

    public static SendResult Uncertain(string message) => new(SendStatus.Uncertain, message);
}

/// <summary>
/// Адаптер одного мессенджера. Контракт: до нажатия отправки любой сбой возвращается как NotSent;
/// после нажатия, если результат проверить не удалось, как Uncertain. Угадывать получателя нельзя.
/// </summary>
public interface IMessengerAdapter
{
    MessengerApp App { get; }

    Task<SendResult> SendAsync(MessageRequest request, CancellationToken cancellationToken);
}
