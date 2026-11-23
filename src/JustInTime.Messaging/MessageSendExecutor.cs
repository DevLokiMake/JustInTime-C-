using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Core.Localization;

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

    /// <summary>Запуск мессенджера с нуля, ожидание чата и отправка могут занять больше минуты.</summary>
    public override TimeSpan? Timeout => TimeSpan.FromSeconds(150);

    protected override async Task<ExecutionOutcome> ExecuteAsync(
        SendMessagePayload payload, CancellationToken cancellationToken)
    {
        // Данные из БД перепроверяем: их могли испортить или изменить вручную.
        var parsed = RecipientParser.Parse(payload.App, payload.Recipient);

        if (!parsed.IsValid)
        {
            return ExecutionOutcome.Failed(Loc.T("msg_bad_recipient", parsed.Error));
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            return ExecutionOutcome.Failed(Loc.T("msg_text_empty"));
        }

        if (payload.Text.Trim().Any(c => c is '\r' or '\n'))
        {
            return ExecutionOutcome.Failed(
                Loc.T("msg_multiline"));
        }

        if (!_adapters.TryGetValue(payload.App, out var adapter))
        {
            return ExecutionOutcome.Failed(
                Loc.T("msg_app_unsupported", payload.App));
        }

        if (!network.IsAvailable)
        {
            return ExecutionOutcome.WaitForNetwork(Loc.T("msg_no_network"));
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
                Loc.T("msg_adapter_crash", payload.App, ex.Message));
        }

        return result.Status switch
        {
            SendStatus.Sent => result.Message is { Length: > 0 }
                ? new ExecutionOutcome(OutcomeKind.Success, result.Message)
                : ExecutionOutcome.Success(),
            SendStatus.NotSent => ExecutionOutcome.Failed(result.Message),
            SendStatus.Uncertain => ExecutionOutcome.Unknown(
                result.Message + Loc.T("msg_uncertain_suffix")),
            _ => ExecutionOutcome.Unknown(Loc.T("msg_unknown_result"))
        };
    }
}
