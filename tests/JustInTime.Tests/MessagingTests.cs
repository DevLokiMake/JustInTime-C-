using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Messaging;

namespace JustInTime.Tests;

public sealed class MessagingTests
{
    // RecipientParser

    [Theory]
    [InlineData("+7 701 123-45-67", "+77011234567")]
    [InlineData("+7(701)1234567", "+77011234567")]
    [InlineData("  +77011234567  ", "+77011234567")]
    public void Phone_is_normalized_to_international_digits(string raw, string expected)
    {
        var result = RecipientParser.Parse(MessengerApp.WhatsApp, raw);

        Assert.True(result.IsValid);
        Assert.Equal(RecipientKind.Phone, result.Recipient!.Kind);
        Assert.Equal(expected, result.Recipient.Value);
    }

    [Theory]
    [InlineData("87011234567")]
    [InlineData("701 123 45 67")]
    [InlineData("8 (701) 123-45-67")]
    public void Number_without_plus_is_rejected_instead_of_guessed(string raw)
    {
        var result = RecipientParser.Parse(MessengerApp.WhatsApp, raw);

        Assert.False(result.IsValid);
        Assert.Contains("международном формате", result.Error);
    }

    [Theory]
    [InlineData("+1234567")]
    [InlineData("+1234567890123456")]
    public void Phone_with_wrong_digit_count_is_rejected(string raw)
    {
        Assert.False(RecipientParser.Parse(MessengerApp.WhatsApp, raw).IsValid);
    }

    [Fact]
    public void Telegram_username_is_accepted_only_for_telegram()
    {
        var telegram = RecipientParser.Parse(MessengerApp.Telegram, "@makee_dev");
        var desktop = RecipientParser.Parse(MessengerApp.TelegramDesktop, "@makee_dev");
        var whatsApp = RecipientParser.Parse(MessengerApp.WhatsApp, "@makee_dev");

        Assert.Equal(RecipientKind.Username, telegram.Recipient!.Kind);
        Assert.True(desktop.IsValid);
        Assert.False(whatsApp.IsValid);
    }

    [Theory]
    [InlineData("@ab")]
    [InlineData("@1abcde")]
    [InlineData("@bad name")]
    public void Malformed_username_is_rejected(string raw)
    {
        Assert.False(RecipientParser.Parse(MessengerApp.Telegram, raw).IsValid);
    }

    [Fact]
    public void Contact_name_is_accepted_and_empty_or_symbols_are_not()
    {
        Assert.Equal(RecipientKind.Name, RecipientParser.Parse(MessengerApp.Telegram, "Анна Петрова").Recipient!.Kind);
        Assert.False(RecipientParser.Parse(MessengerApp.Telegram, "").IsValid);
        Assert.False(RecipientParser.Parse(MessengerApp.Telegram, "   ").IsValid);
        Assert.False(RecipientParser.Parse(MessengerApp.Telegram, "!!!").IsValid);
    }

    [Fact]
    public void Validator_rejects_message_with_unusable_recipient()
    {
        var errors = TaskValidator.Validate("t", new SendMessagePayload(MessengerApp.WhatsApp, "87011234567", "Привет"));

        Assert.Equal(1, errors.Count);
    }

    // MessageSendExecutor

    private sealed class FakeAdapter(MessengerApp app) : IMessengerAdapter
    {
        public MessengerApp App => app;
        public int Calls { get; private set; }
        public MessageRequest? Last { get; private set; }
        public Func<SendResult> Behavior { get; set; } = () => SendResult.Sent();

        public Task<SendResult> SendAsync(MessageRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request;
            return Task.FromResult(Behavior());
        }
    }

    private sealed class FakeNetwork(bool available = true) : INetworkStatus
    {
        public bool IsAvailable { get; set; } = available;
    }

    private static SendMessagePayload Message(string recipient = "+77011234567", string text = "Привет") =>
        new(MessengerApp.WhatsApp, recipient, text);

    private static Task<ExecutionOutcome> Run(
        FakeAdapter adapter, SendMessagePayload payload, FakeNetwork? network = null) =>
        new MessageSendExecutor([adapter], network ?? new FakeNetwork()).ExecuteAsync(payload, CancellationToken.None);

    [Fact]
    public void Executor_requires_interactive_desktop()
    {
        Assert.True(new MessageSendExecutor([], new FakeNetwork()).RequiresInteractiveDesktop);
    }

    [Fact]
    public async Task Sent_result_becomes_success_and_adapter_gets_parsed_recipient()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp);

        var outcome = await Run(adapter, Message("+7 701 123 45 67", "  Привет  "));

        Assert.Equal(OutcomeKind.Success, outcome.Kind);
        Assert.Equal("+77011234567", adapter.Last!.Recipient.Value);
        Assert.Equal("Привет", adapter.Last.Text);
    }

    [Fact]
    public async Task Success_message_does_not_claim_delivery()
    {
        var outcome = await Run(new FakeAdapter(MessengerApp.WhatsApp), Message());

        Assert.Contains("Доставка получателю не проверяется", outcome.Message);
    }

    [Fact]
    public async Task NotSent_becomes_failed()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp) { Behavior = () => SendResult.NotSent("Контакт не найден.") };

        var outcome = await Run(adapter, Message());

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Contains("Контакт не найден", outcome.Message);
    }

    [Fact]
    public async Task Uncertain_becomes_unknown_and_mentions_manual_check()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp) { Behavior = () => SendResult.Uncertain("Окно изменилось.") };

        var outcome = await Run(adapter, Message());

        Assert.Equal(OutcomeKind.Unknown, outcome.Kind);
        Assert.Contains("вручную", outcome.Message);
    }

    [Fact]
    public async Task Adapter_exception_becomes_unknown_never_failed()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp) { Behavior = () => throw new InvalidOperationException("упало") };

        var outcome = await Run(adapter, Message());

        Assert.Equal(OutcomeKind.Unknown, outcome.Kind);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp) { Behavior = () => throw new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(adapter, Message()));
    }

    [Fact]
    public async Task No_network_waits_and_adapter_is_not_called()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp);

        var outcome = await Run(adapter, Message(), new FakeNetwork(available: false));

        Assert.Equal(OutcomeKind.WaitForNetwork, outcome.Kind);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task Missing_adapter_fails_honestly()
    {
        var executor = new MessageSendExecutor([new FakeAdapter(MessengerApp.Telegram)], new FakeNetwork());

        var outcome = await executor.ExecuteAsync(Message(), CancellationToken.None);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Contains("не поддерживается", outcome.Message);
    }

    [Fact]
    public async Task Corrupt_recipient_or_empty_text_fails_without_calling_adapter()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp);

        Assert.Equal(OutcomeKind.Failed, (await Run(adapter, Message("87011234567"))).Kind);
        Assert.Equal(OutcomeKind.Failed, (await Run(adapter, Message(text: "  "))).Kind);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public async Task Message_is_sent_once_per_execution()
    {
        var adapter = new FakeAdapter(MessengerApp.WhatsApp);

        await Run(adapter, Message());

        Assert.Equal(1, adapter.Calls);
    }
}
