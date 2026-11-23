using JustInTime.Automation;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Messaging;

namespace JustInTime.Tests;

public sealed class WhatsAppDesktopAdapterTests
{
    [Fact]
    public async Task Opens_the_number_chat_and_sends_only_after_the_composer_is_verified()
    {
        var fixture = CreateFixture();
        fixture.SendButton.OnInvoke = () =>
        {
            Assert.Equal("Привет", fixture.Composer.Value);
            fixture.Composer.Value = string.Empty;
        };
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(string.Empty, fixture.Composer.Value);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
        Assert.Equal(["77011234567"], fixture.Launcher.OpenedPhones);
    }

    [Fact]
    public async Task Invisible_placeholder_characters_in_the_field_are_not_treated_as_a_draft()
    {
        var fixture = CreateFixture();
        fixture.Composer.Value = "\n\u200B";
        fixture.SendButton.OnInvoke = () => fixture.Composer.Value = "\n";
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Falls_back_to_keyboard_input_when_the_page_ignores_value_writes()
    {
        var fixture = CreateFixture();
        fixture.Composer.IgnoreSetValue = true;
        fixture.SendButton.OnInvoke = () => fixture.Composer.Value = string.Empty;
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(1, fixture.Composer.TypeTextCalls);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Does_not_send_when_neither_value_nor_keyboard_input_puts_the_text_in_the_field()
    {
        var fixture = CreateFixture();
        fixture.Composer.IgnoreSetValue = true;
        fixture.Composer.TypeTextWorks = false;
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal(0, fixture.SendButton.InvokeCount);
        Assert.Contains("в поле: 0", result.Message);
    }

    [Fact]
    public async Task Multiline_text_is_typed_from_the_keyboard_and_not_written_through_value()
    {
        var fixture = CreateFixture();
        fixture.SendButton.OnInvoke = () => fixture.Composer.Value = string.Empty;
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request("Первая\nвторая\n\nчетвёртая"), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(1, fixture.Composer.TypeTextCalls);
        Assert.Equal(0, fixture.Composer.SetValueCalls);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Multiline_text_matches_even_when_the_field_returns_crlf()
    {
        var fixture = CreateFixture();
        fixture.Composer.TypeTextEffect = t => t.Replace("\n", "\r\n");
        fixture.SendButton.OnInvoke = () => fixture.Composer.Value = string.Empty;
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request("Первая\nвторая"), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Multiline_mismatch_is_uncertain_and_never_clicks_send()
    {
        // Имитация: Enter сработал как отправка первой строки, в поле осталась только вторая.
        var fixture = CreateFixture();
        fixture.Composer.TypeTextEffect = t => t[(t.IndexOf('\n') + 1)..];
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request("Первая\nвторая"), CancellationToken.None);

        Assert.Equal(SendStatus.Uncertain, result.Status);
        Assert.Equal(0, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Existing_draft_is_preserved_and_never_sent()
    {
        var fixture = CreateFixture();
        fixture.Composer.Value = "Мой черновик";
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal("Мой черновик", fixture.Composer.Value);
        Assert.Equal(0, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Failure_while_clicking_send_is_uncertain()
    {
        var fixture = CreateFixture();
        fixture.SendButton.OnInvoke = () => throw new UiAutomationException("кнопка исчезла");
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.Uncertain, result.Status);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Changed_interface_without_send_button_is_not_sent_and_draft_is_cleared()
    {
        var fixture = CreateFixture();
        fixture.SendButton.Name = "Что-то другое";
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal(0, fixture.SendButton.InvokeCount);
        Assert.Equal(string.Empty, fixture.Composer.Value);
    }

    [Fact]
    public async Task Click_without_cleared_field_is_uncertain_and_clicked_only_once()
    {
        var fixture = CreateFixture();   // кнопка нажимается, но поле не очищается
        var adapter = CreateAdapter(fixture);

        var result = await adapter.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(SendStatus.Uncertain, result.Status);
        Assert.Equal(1, fixture.SendButton.InvokeCount);
    }

    private static WhatsAppDesktopAdapter CreateAdapter(Fixture fixture) =>
        new(fixture.Windows, fixture.Launcher, TimeProvider.System);

    private static MessageRequest Request(string text = "Привет")
    {
        var recipient = RecipientParser.Parse(MessengerApp.WhatsApp, "+7 701 123 45 67").Recipient!;
        return new MessageRequest(recipient, text);
    }

    private static Fixture CreateFixture()
    {
        var composer = new FakeUiElement(
            "Edit", "Введите сообщение в чат '+7 701 123 45 67'")
        {
            Patterns = ["Value"],
            Value = string.Empty
        };
        var sendButton = new FakeUiElement("Button", "Отправить")
        {
            Patterns = ["Invoke"]
        };
        var webView = new FakeUiElement("Document", "WhatsApp");
        webView.Add(composer);
        webView.Add(sendButton);

        var windows = new FakeWindowFinder(
            new WindowInfo(new FakeUiElement("Window", "WhatsApp"), "WhatsApp", "WhatsApp.Root", 10),
            new WindowInfo(webView, "WhatsApp — веб-содержимое", "msedgewebview2", 11));
        var launcher = new FakeWhatsAppProtocolLauncher();

        return new Fixture(windows, launcher, composer, sendButton);
    }

    private sealed record Fixture(
        FakeWindowFinder Windows,
        FakeWhatsAppProtocolLauncher Launcher,
        FakeUiElement Composer,
        FakeUiElement SendButton);

    private sealed class FakeWhatsAppProtocolLauncher : IWhatsAppProtocolLauncher
    {
        public List<string> OpenedPhones { get; } = [];

        public void OpenChat(string phoneDigits) => OpenedPhones.Add(phoneDigits);
    }
}
