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

    private static WhatsAppDesktopAdapter CreateAdapter(Fixture fixture) =>
        new(fixture.Windows, fixture.Launcher, TimeProvider.System);

    private static MessageRequest Request()
    {
        var recipient = RecipientParser.Parse(MessengerApp.WhatsApp, "+7 701 123 45 67").Recipient!;
        return new MessageRequest(recipient, "Привет");
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
