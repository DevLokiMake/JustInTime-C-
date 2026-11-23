using JustInTime.Automation;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Core.Localization;

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
            return SendResult.NotSent(Loc.T("msg_wa_phone_only"));
        }

        var phoneDigits = Digits(request.Recipient.Value);

        if (phoneDigits.Length is < RecipientParser.MinPhoneDigits or > RecipientParser.MaxPhoneDigits)
        {
            return SendResult.NotSent(Loc.T("msg_wa_bad_length"));
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
                    Loc.T("msg_field_not_empty", "WhatsApp", existing.Length));
            }

            if (text.Contains('\n'))
            {
                var failure = await EnterMultilineAsync(field, text, cancellationToken);

                if (failure is not null)
                {
                    return failure;
                }
            }
            else
            {
                var entered = await EnterTextAsync(field, text, cancellationToken);

                if (entered is not null)
                {
                    return NotSentAfterCleanup(field, text, entered);
                }
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
                    Loc.T("msg_cleared_after_send", "WhatsApp"));
            }

            return SendResult.Uncertain(
                Loc.T("msg_not_confirmed", "WhatsApp"));
        }
        catch (OperationCanceledException) when (!sendWasAttempted)
        {
            var cleanup = composer is null ? null : ClearDraftIfUnchanged(composer, text);
            var message = Loc.T("msg_cancelled_before");
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
                    Loc.T("msg_failure_after_click", ex.Message));
            }

            var cleanup = composer is null ? null : ClearDraftIfUnchanged(composer, text);
            var message = Loc.T("msg_not_sent_ex", "WhatsApp", ex.Message);
            return SendResult.NotSent(AppendCleanupMessage(message, cleanup));
        }
    }

    private async Task<ComposerSearch> WaitForComposerAsync(string phoneDigits, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + ComposerTimeout;
        var lastLaunch = time.GetUtcNow();
        var reopens = 0;
        string lastError = Loc.T("msg_wa_window_missing");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var appWindows = FindWhatsAppWindows();

            if (appWindows.Count > 0)
            {
                lastError = Loc.T("msg_wa_no_field");
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
                        null, Loc.T("msg_wa_many_chats"));
                }

                if (composers.Count > 0)
                {
                    lastError = Loc.T("msg_wa_wrong_chat");
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

    /// <summary>
    /// Вводит текст в поле и проверяет результат. Сначала через Value; если страница это игнорирует (поле осталось
    /// пустым), вводит с клавиатуры. Возвращает null при успехе или причину, по которой отправлять нельзя.
    /// </summary>
    private async Task<string?> EnterTextAsync(IUiElement field, string text, CancellationToken cancellationToken)
    {
        string? setError = null;

        try
        {
            field.SetValue(text);
        }
        catch (UiAutomationException ex)
        {
            setError = ex.Message;
        }

        await Task.Delay(SettleDelay, time, cancellationToken);

        if (Matches(field, text))
        {
            return null;
        }

        var current = Normalize(field.GetValue());

        if (current.Length == 0 && text.All(c => !char.IsControl(c)))
        {
            try
            {
                field.TypeText(text);
            }
            catch (UiAutomationException ex)
            {
                return Loc.T("msg_wa_value_failed", setError is null ? string.Empty : " (" + setError + ")", ex.Message);
            }

            await Task.Delay(SettleDelay, time, cancellationToken);

            if (Matches(field, text))
            {
                return null;
            }

            current = Normalize(field.GetValue());
        }

        return Loc.T("msg_verify_failed_counts", "WhatsApp", Normalize(text).Length, current.Length);
    }

    /// <summary>
    /// Многострочный текст вводится только с клавиатуры: перенос строки это Shift+Enter, обычный Enter отправил бы
    /// сообщение. Запись через Value здесь не используется. Если после ввода текст в поле не совпал, нельзя исключить,
    /// что Enter сработал как отправка, поэтому результат неопределённый, а не «не отправлено».
    /// </summary>
    private async Task<SendResult?> EnterMultilineAsync(IUiElement field, string text, CancellationToken cancellationToken)
    {
        try
        {
            field.TypeText(text);
        }
        catch (UiAutomationException ex)
        {
            return NotSentAfterCleanup(field, text, Loc.T("msg_multiline_type_failed", ex.Message));
        }

        await Task.Delay(SettleDelay, time, cancellationToken);

        return Matches(field, text) ? null : SendResult.Uncertain(Loc.T("msg_multiline_mismatch", "WhatsApp"));
    }

    private static bool Matches(IUiElement field, string text) =>
        string.Equals(Normalize(field.GetValue()), Normalize(text), StringComparison.Ordinal);

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
                return new ButtonSearch(null, Loc.T("msg_button_ambiguous"));
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new ButtonSearch(null, Loc.T("msg_button_missing"));
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
                : Loc.T("msg_clear_failed", "WhatsApp");
        }
        catch (UiAutomationException ex)
        {
            return Loc.T("msg_clear_failed_ex", "WhatsApp", ex.Message);
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
    private static string Normalize(string value) => ComposerText.Canonical(value);

    private static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    private sealed record ComposerMatch(WindowInfo Window, IUiElement Field);

    private sealed record ComposerSearch(ComposerMatch? Match, string? Error);

    private sealed record ButtonSearch(IUiElement? Button, string? Error);
}
