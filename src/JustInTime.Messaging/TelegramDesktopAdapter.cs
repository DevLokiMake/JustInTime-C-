using JustInTime.Automation;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Core.Localization;

namespace JustInTime.Messaging;

/// <summary>
/// Отправка через Telegram Desktop. Чат открывается официальной ссылкой tg://resolve (по номеру или @username),
/// текст вводится в поле через UI Automation и отправляется кнопкой. Названия чата в заголовке окна для сверки
/// недостаточно (там имя, а не номер), поэтому получателя определяет сама ссылка, а адаптер дополнительно
/// ждёт, пока окно и заголовок перестанут меняться.
/// </summary>
public sealed class TelegramDesktopAdapter(
    IWindowFinder windows,
    ITelegramLinkLauncher launcher,
    TimeProvider time,
    MessengerApp app = MessengerApp.TelegramDesktop,
    TimeSpan? windowTimeout = null) : IMessengerAdapter
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReopenAfter = TimeSpan.FromSeconds(10);
    private const int MaxReopens = 2;
    private static readonly TimeSpan StableFor = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan SendButtonTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendConfirmationTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    public MessengerApp App => app;

    public async Task<SendResult> SendAsync(MessageRequest request, CancellationToken cancellationToken)
    {
        string link;

        switch (request.Recipient.Kind)
        {
            case RecipientKind.Phone:
                var digits = new string(request.Recipient.Value.Where(char.IsAsciiDigit).ToArray());
                link = $"tg://resolve?phone={digits}";
                break;
            case RecipientKind.Username:
                link = $"tg://resolve?domain={request.Recipient.Value.TrimStart('@')}";
                break;
            default:
                return SendResult.NotSent(
                    Loc.T("msg_tg_need_phone_or_username"));
        }

        return await Task.Run(() => SendCoreAsync(request.Text, link, cancellationToken), cancellationToken);
    }

    private async Task<SendResult> SendCoreAsync(string text, string link, CancellationToken cancellationToken)
    {
        IUiElement? composer = null;
        var sendWasAttempted = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            launcher.Open(link);

            var found = await WaitForStableComposerAsync(link, cancellationToken);

            if (found.Match is null)
            {
                return SendResult.NotSent(found.Error!);
            }

            var (window, field) = found.Match;
            composer = field;

            var existing = field.GetValue();

            if (existing.Length > 0)
            {
                return SendResult.NotSent(
                    Loc.T("msg_field_not_empty", "Telegram", existing.Length));
            }

            field.SetValue(text);
            await Task.Delay(SettleDelay, time, cancellationToken);

            if (!string.Equals(field.GetValue(), text, StringComparison.Ordinal))
            {
                return NotSentAfterCleanup(field, text, Loc.T("msg_verify_failed", "Telegram"));
            }

            var buttonSearch = await WaitForSendButtonAsync(window, cancellationToken);

            if (buttonSearch.Button is null)
            {
                return NotSentAfterCleanup(field, text, buttonSearch.Error!);
            }

            sendWasAttempted = true;
            buttonSearch.Button.Invoke();

            if (await WaitForComposerToClearAsync(field, cancellationToken))
            {
                return SendResult.Sent(
                    Loc.T("msg_cleared_after_send", "Telegram"));
            }

            return SendResult.Uncertain(
                Loc.T("msg_not_confirmed", "Telegram"));
        }
        catch (OperationCanceledException) when (!sendWasAttempted)
        {
            var cleanup = composer is null ? null : ClearDraftIfUnchanged(composer, text);
            return SendResult.NotSent(AppendCleanup(
                Loc.T("msg_cancelled_before"), cleanup));
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
            return SendResult.NotSent(AppendCleanup(Loc.T("msg_not_sent_ex", "Telegram", ex.Message), cleanup));
        }
    }

    /// <summary>
    /// Ждёт, пока в окне Telegram появится ровно одно поле ввода и заголовок окна не меняется StableFor.
    /// Так мы не вводим текст в чат, который был открыт до перехода по ссылке.
    /// </summary>
    private async Task<ComposerSearch> WaitForStableComposerAsync(string link, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + (windowTimeout ?? WindowTimeout);
        var lastLaunch = time.GetUtcNow();
        var reopens = 0;
        string? lastTitle = null;
        DateTimeOffset stableSince = default;
        var error = Loc.T("msg_tg_window_missing");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates = windows.FindWindows("Telegram")
                .Where(w => w.ProcessName.Equals("Telegram", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count > 1)
            {
                return new ComposerSearch(null, Loc.T("msg_tg_many_windows"));
            }

            if (candidates.Count == 1)
            {
                var window = candidates[0];
                var composers = UiSearch.FindAll(
                        window.Window, new ElementQuery(ControlType: "Edit"), maxDepth: 20, maxNodes: 20000)
                    .Where(IsComposer)
                    .ToList();

                if (composers.Count > 1)
                {
                    return new ComposerSearch(null, Loc.T("msg_tg_many_fields"));
                }

                if (composers.Count == 1)
                {
                    var now = time.GetUtcNow();

                    if (window.Title != lastTitle)
                    {
                        lastTitle = window.Title;
                        stableSince = now;
                    }

                    if (now - stableSince >= StableFor)
                    {
                        return new ComposerSearch(new ComposerMatch(window.Window, composers[0]), null);
                    }

                    error = Loc.T("msg_tg_not_settled");
                }
                else
                {
                    lastTitle = null;
                    error = Loc.T("msg_tg_no_field");
                }
            }

            if (time.GetUtcNow() >= deadline)
            {
                return new ComposerSearch(null, error);
            }

            // Telegram мог быть закрыт или свёрнут в трей и не открыть чат по первой ссылке. Повтор той же ссылки
            // безопасен: он лишь открывает чат и ничего не отправляет.
            if (reopens < MaxReopens && lastTitle is null && time.GetUtcNow() - lastLaunch >= ReopenAfter)
            {
                launcher.Open(link);
                reopens++;
                lastLaunch = time.GetUtcNow();
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private async Task<ButtonSearch> WaitForSendButtonAsync(IUiElement window, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + SendButtonTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var buttons = UiSearch.FindAll(
                    window, new ElementQuery(ControlType: "Button"), maxDepth: 20, maxNodes: 20000)
                .Where(b => b.IsEnabled && !b.IsOffscreen && IsSendButtonName(b.Name))
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
                var seen = UiSearch.FindAll(
                        window, new ElementQuery(ControlType: "Button"), maxDepth: 20, maxNodes: 20000)
                    .Where(b => b.ClassName.EndsWith("Ui::SendButton", StringComparison.Ordinal))
                    .Select(b => $"«{b.Name}»{(b.IsEnabled ? string.Empty : Loc.T("msg_button_unavailable"))}")
                    .ToList();
                var hint = seen.Count == 0
                    ? Loc.T("msg_button_hint_none")
                    : Loc.T("msg_button_hint_seen", string.Join(", ", seen));

                return new ButtonSearch(null, Loc.T("msg_button_missing") + hint);
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

            if (composer.GetValue().Length == 0)
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
        SendResult.NotSent(AppendCleanup(message, ClearDraftIfUnchanged(field, text)));

    private static string? ClearDraftIfUnchanged(IUiElement field, string text)
    {
        try
        {
            if (!string.Equals(field.GetValue(), text, StringComparison.Ordinal))
            {
                return null;
            }

            field.SetValue(string.Empty);
            return field.GetValue().Length == 0
                ? null
                : Loc.T("msg_clear_failed", "Telegram");
        }
        catch (UiAutomationException ex)
        {
            return Loc.T("msg_clear_failed_ex", "Telegram", ex.Message);
        }
    }

    private static string AppendCleanup(string message, string? cleanup) =>
        cleanup is null ? message : message + " " + cleanup;

    /// <summary>
    /// Qt отдаёт имена классов с префиксом «class » и вкладывает поле в обёртку: Ui::InputField содержит
    /// Ui::InputField::Inner, и оба имеют шаблон Value. Берём только внутреннее поле, иначе поле «нашлось бы» дважды.
    /// </summary>
    private static bool IsComposer(IUiElement element) =>
        !element.IsOffscreen
        && element.Patterns.Contains("Value")
        && element.ClassName.EndsWith("Ui::InputField::Inner", StringComparison.Ordinal)
        && (element.Name.StartsWith("Сообщение", StringComparison.OrdinalIgnoreCase)
            || element.Name.StartsWith("Message", StringComparison.OrdinalIgnoreCase));

    private static bool IsSendButtonName(string name) =>
        string.Equals(name, "Отправить", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Send", StringComparison.OrdinalIgnoreCase);

    private sealed record ComposerMatch(IUiElement Window, IUiElement Field);

    private sealed record ComposerSearch(ComposerMatch? Match, string? Error);

    private sealed record ButtonSearch(IUiElement? Button, string? Error);
}
