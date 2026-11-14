using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Messaging;

/// <summary>
/// Исполнитель SendMessage. Проверяет данные и интернет, выбирает адаптер и честно переводит его результат в итог задачи.
/// Результат «неизвестно» никогда не превращается в успех или ошибку: иначе возможен дубль сообщения.
/// </summary>
public sealed class MessageSendExecutor(IEnumerable<IMessengerAdapter> adapters, INetworkStatus network)
    : ActionExecutor<SendMessagePayload>
{
    private readonly Dictionary<MessengerApp, IMessengerAdapter> _adapters = adapters.ToDictionary(a => a.App);

    public override ActionType ActionType => ActionType.SendMessage;

    public override bool RequiresInteractiveDesktop => true;

    protected override async Task<ExecutionOutcome> ExecuteAsync(
        SendMessagePayload payload, CancellationToken cancellationToken)
    {
        // Данные из БД перепроверяем: их могли испортить или изменить вручную.
        var parsed = RecipientParser.Parse(payload.App, payload.Recipient);

        if (!parsed.IsValid)
        {
            return ExecutionOutcome.Failed("Получатель задан неверно: " + parsed.Error);
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            return ExecutionOutcome.Failed("Текст сообщения пустой.");
        }

        if (!_adapters.TryGetValue(payload.App, out var adapter))
        {
            return ExecutionOutcome.Failed(
                $"Отправка через {payload.App} пока не поддерживается в этой версии приложения. Сообщение не отправлено.");
        }

        if (!network.IsAvailable)
        {
            return ExecutionOutcome.WaitForNetwork("Нет подключения к интернету. Сообщение не отправлено, задача подождёт.");
        }

        SendResult result;

        try
        {
            result = await adapter.SendAsync(new MessageRequest(parsed.Recipient!, payload.Text.Trim()), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Тайм-аут или закрытие приложения посреди отправки: движок превратит это в «неизвестно».
            throw;
        }
        catch (Exception ex)
        {
            return ExecutionOutcome.Unknown(
                $"Сбой адаптера {payload.App}: {ex.Message} Неизвестно, ушло ли сообщение. Проверьте чат вручную.");
        }

        return result.Status switch
        {
            SendStatus.Sent => result.Message is { Length: > 0 }
                ? new ExecutionOutcome(OutcomeKind.Success, result.Message)
                : ExecutionOutcome.Success(),
            SendStatus.NotSent => ExecutionOutcome.Failed(result.Message),
            SendStatus.Uncertain => ExecutionOutcome.Unknown(
                result.Message + " Проверьте чат вручную: автоматически сообщение повторно не отправляется."),
            _ => ExecutionOutcome.Unknown("Адаптер вернул неизвестный результат. Проверьте чат вручную.")
        };
    }
}
