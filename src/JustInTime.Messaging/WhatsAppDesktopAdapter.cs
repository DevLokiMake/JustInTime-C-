using JustInTime.Automation;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Messaging;

public sealed class WhatsAppDesktopAdapter(
    IWindowFinder windows,
    IWhatsAppProtocolLauncher launcher,
    TimeProvider time) : IMessengerAdapter
{
    private static readonly TimeSpan ComposerTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReopenAfter = TimeSpan.FromSeconds(10);
    private const int MaxReopens = 2;
    private static readonly TimeSpan SendButtonTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendConfirmationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    public MessengerApp App => MessengerApp.WhatsApp;

    public async Task<SendResult> SendAsync(MessageRequest request, CancellationToken cancellationToken)
    {
        if (request.Recipient.Kind != RecipientKind.Phone)
        {
            return SendResult.NotSent("Для WhatsApp укажите номер телефона в международном формате.");
        }

        var phoneDigits = Digits(request.Recipient.Value);

        if (phoneDigits.Length is < RecipientParser.MinPhoneDigits or > RecipientParser.MaxPhoneDigits)
        {
            return SendResult.NotSent("Номер WhatsApp имеет неверную длину.");
        }

        return await Task.Run(
            () => SendCoreAsync(request.Text, phoneDigits, cancellationToken),
            cancellationToken);
    }

    private async Task<SendResult> SendCoreAsync(string text, string phoneDigits, CancellationToken cancellationToken)
    {
        IUiElement? composer = null;
        var sendWasAttempted = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            launcher.OpenChat(phoneDigits);

            var search = await WaitForComposerAsync(phoneDigits, cancellationToken);

            if (search.Match is null)
            {
                return SendResult.NotSent(search.Error!);
            }

            var (window, field) = search.Match;
            composer = field;

            var existing = field.GetValue();

            if (Normalize(existing).Length > 0)
            {
                return SendResult.NotSent(
                    $"В поле WhatsApp уже есть текст (символов: {existing.Length}). Он не изменён; очистите черновик и повторите задачу.");
            }

            field.SetValue(text);
            await Task.Delay(SettleDelay, time, cancellationToken);

            if (!string.Equals(Normalize(field.GetValue()), Normalize(text), StringComparison.Ordinal))
            {
                return NotSentAfterCleanup(
                    field, text, "Не удалось проверить текст в поле WhatsApp до отправки.");
            }

            var buttonSearch = await WaitForSendButtonAsync(window.Window, cancellationToken);

            if (buttonSearch.Button is null)
            {
                return NotSentAfterCleanup(field, text, buttonSearch.Error!);
            }

            sendWasAttempted = true;
            buttonSearch.Button.Invoke();

            if (await WaitForComposerToClearAsync(field, cancellationToken))
            {
                return SendResult.Sent(
                    "WhatsApp очистил поле после нажатия «Отправить». Доставка получателю не проверяется.");
            }

            return SendResult.Uncertain(
                "Кнопка отправки нажата, но WhatsApp не подтвердил очистку поля. " +
                "Проверьте чат вручную; задача не будет отправлена повторно автоматически.");
        }
        catch (OperationCanceledException) when (!sendWasAttempted)
        {
            var cleanup = composer is null ? null : ClearDraftIfUnchanged(composer, text);
            var message = "Выполнение отменено до нажатия кнопки отправки; сообщение не отправлено.";
            return SendResult.NotSent(AppendCleanupMessage(message, cleanup));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (sendWasAttempted)
            {
                return SendResult.Uncertain(
                    $"Сбой после нажатия кнопки отправки: {ex.Message} " +
                    "Неизвестно, ушло ли сообщение. Проверьте чат вручную; автоматического повтора не будет.");
            }

            var cleanup = composer is null ? null : ClearDraftIfUnchanged(composer, text);
            var message = $"WhatsApp не отправил сообщение: {ex.Message}";
            return SendResult.NotSent(AppendCleanupMessage(message, cleanup));
        }
    }

    private async Task<ComposerSearch> WaitForComposerAsync(string phoneDigits, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + ComposerTimeout;
        var lastLaunch = time.GetUtcNow();
        var reopens = 0;
        string lastError = "Окно WhatsApp Desktop не найдено. Откройте и авторизуйте WhatsApp.";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var appWindows = FindWhatsAppWindows();

            if (appWindows.Count > 0)
            {
                lastError = "Не найдено поле ввода чата WhatsApp. Откройте нужный чат и повторите.";
                var composers = appWindows
                    .SelectMany(window => FindComposers(window)
                        .Select(field => new ComposerMatch(window, field)))
                    .ToList();
                var matching = composers
                    .Where(match => Digits(match.Field.Name).Contains(phoneDigits, StringComparison.Ordinal))
                    .ToList();

                if (matching.Count == 1)
                {
                    return new ComposerSearch(matching[0], null);
                }

                if (matching.Count > 1)
                {
                    return new ComposerSearch(
                        null, "Найдено несколько подходящих чатов WhatsApp. Отправка остановлена.");
                }

                if (composers.Count > 0)
                {
                    lastError = "Открытый чат не совпадает с номером получателя. Сообщение не отправлено.";
                }
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new ComposerSearch(null, lastError);
            }

            // Приложение могло только что запуститься и не открыть чат по ссылке. Повторное открытие
            // той же ссылки безопасно: оно лишь открывает чат и ничего не отправляет.
            if (reopens < MaxReopens && time.GetUtcNow() - lastLaunch >= ReopenAfter)
            {
                launcher.OpenChat(phoneDigits);
                reopens++;
                lastLaunch = time.GetUtcNow();
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private IReadOnlyList<WindowInfo> FindWhatsAppWindows()
    {
        var found = windows.FindWindows("WhatsApp");

        if (!found.Any(window => IsWhatsAppProcess(window.ProcessName)))
        {
            return [];
        }

        return found
            .Where(window => IsWhatsAppProcess(window.ProcessName)
                             || (window.ProcessName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase)
                                 && window.Title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static IReadOnlyList<IUiElement> FindComposers(WindowInfo window) =>
        UiSearch.FindAll(
                window.Window,
                new ElementQuery(ControlType: "Edit"),
                maxDepth: 20,
                maxNodes: 20000)
            .Where(field => !field.IsOffscreen && IsComposerName(field.Name))
            .ToList();

    private async Task<ButtonSearch> WaitForSendButtonAsync(IUiElement window, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + SendButtonTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var buttons = UiSearch.FindAll(
                    window,
                    new ElementQuery(ControlType: "Button"),
                    maxDepth: 20,
                    maxNodes: 20000)
                .Where(button => button.IsEnabled
                                 && !button.IsOffscreen
                                 && IsSendButtonName(button.Name))
                .ToList();

            if (buttons.Count == 1)
            {
                return new ButtonSearch(buttons[0], null);
            }

            if (buttons.Count > 1)
            {
                return new ButtonSearch(null, "Кнопка отправки неоднозначна; сообщение не отправлено.");
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new ButtonSearch(null, "Кнопка «Отправить» не найдена; сообщение не отправлено.");
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private async Task<bool> WaitForComposerToClearAsync(IUiElement composer, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + SendConfirmationTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Normalize(composer.GetValue()).Length == 0)
            {
                return true;
            }

            if (time.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private static SendResult NotSentAfterCleanup(IUiElement field, string text, string message) =>
        SendResult.NotSent(AppendCleanupMessage(message, ClearDraftIfUnchanged(field, text)));

    private static string? ClearDraftIfUnchanged(IUiElement field, string text)
    {
        try
        {
            if (!string.Equals(Normalize(field.GetValue()), Normalize(text), StringComparison.Ordinal))
            {
                return null;
            }

            field.SetValue(string.Empty);
            return Normalize(field.GetValue()).Length == 0
                ? null
                : "Поле WhatsApp не удалось очистить; проверьте оставшийся черновик.";
        }
        catch (UiAutomationException ex)
        {
            return $"Поле WhatsApp не удалось очистить: {ex.Message}";
        }
    }

    private static string AppendCleanupMessage(string message, string? cleanup) =>
        cleanup is null ? message : message + " " + cleanup;

    private static bool IsWhatsAppProcess(string processName) =>
        processName.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase);

    private static bool IsComposerName(string name) =>
        name.Contains("Введите сообщение в чат", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Type a message", StringComparison.OrdinalIgnoreCase);

    private static bool IsSendButtonName(string name) =>
        string.Equals(name, "Отправить", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Send", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Поле ввода WhatsApp (contenteditable в WebView2) может отдавать «пустое» значение как перевод строки,
    /// неразрывный пробел или невидимый символ нулевой ширины. Для проверок «пусто» и «совпадает» такие символы не считаются.
    /// </summary>
    private static string Normalize(string value) =>
        new string(value.Where(c => c is not ('\u200B' or '\u200C' or '\u200D' or '\uFEFF' or '\u00A0')).ToArray())
            .Trim();

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    private sealed record ComposerMatch(WindowInfo Window, IUiElement Field);

    private sealed record ComposerSearch(ComposerMatch? Match, string? Error);

    private sealed record ButtonSearch(IUiElement? Button, string? Error);
}
