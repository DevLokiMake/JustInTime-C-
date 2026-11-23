using JustInTime.Automation;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Messaging;

namespace JustInTime.Tests;

public sealed class TelegramDesktopAdapterTests
{
    [Fact]
    public async Task Opens_phone_link_and_sends_after_composer_is_verified()
    {
        var f = CreateFixture();
        f.SendButton.OnInvoke = () => f.Composer.Value = string.Empty;

        var result = await f.Adapter.SendAsync(Request("+7 701 123 45 67"), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(1, f.SendButton.InvokeCount);
        Assert.Equal(["tg://resolve?phone=77011234567"], f.Launcher.Opened);
    }

    [Fact]
    public async Task Multiline_text_is_typed_from_the_keyboard_and_not_written_through_value()
    {
        var f = CreateFixture();
        f.SendButton.OnInvoke = () => f.Composer.Value = string.Empty;

        var result = await f.Adapter.SendAsync(Request("@makee_dev", "Первая\nвторая"), CancellationToken.None);

        Assert.Equal(SendStatus.Sent, result.Status);
        Assert.Equal(1, f.Composer.TypeTextCalls);
        Assert.Equal(0, f.Composer.SetValueCalls);
        Assert.Equal(1, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Multiline_mismatch_is_uncertain_and_never_clicks_send()
    {
        var f = CreateFixture();
        f.Composer.TypeTextEffect = t => t[(t.IndexOf('\n') + 1)..];

        var result = await f.Adapter.SendAsync(Request("@makee_dev", "Первая\nвторая"), CancellationToken.None);

        Assert.Equal(SendStatus.Uncertain, result.Status);
        Assert.Equal(0, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Username_uses_domain_link()
    {
        var f = CreateFixture();
        f.SendButton.OnInvoke = () => f.Composer.Value = string.Empty;

        await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(["tg://resolve?domain=makee_dev"], f.Launcher.Opened);
    }

    [Fact]
    public async Task Contact_name_is_refused_without_opening_anything()
    {
        var f = CreateFixture();

        var result = await f.Adapter.SendAsync(Request("Анна Петрова"), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Empty(f.Launcher.Opened);
        Assert.Equal(0, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Existing_draft_is_preserved_and_never_sent()
    {
        var f = CreateFixture();
        f.Composer.Value = "Мой черновик";

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal("Мой черновик", f.Composer.Value);
        Assert.Equal(0, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Failure_while_clicking_send_is_uncertain()
    {
        var f = CreateFixture();
        f.SendButton.OnInvoke = () => throw new UiAutomationException("кнопка исчезла");

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.Uncertain, result.Status);
        Assert.Equal(1, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Browser_tab_with_telegram_in_title_is_not_treated_as_telegram()
    {
        var f = CreateFixture(telegramProcess: "chrome", windowTimeout: TimeSpan.FromMilliseconds(600));

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal(0, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Changed_interface_without_composer_is_not_sent()
    {
        var f = CreateFixture(windowTimeout: TimeSpan.FromMilliseconds(600));
        f.Composer.Name = "Что-то другое";   // Telegram переименовал поле: распознать его нельзя

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Contains("поле ввода", result.Message);
        Assert.Equal(0, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Changed_interface_without_send_button_is_not_sent_and_draft_is_cleared()
    {
        var f = CreateFixture();
        f.SendButton.Name = "Что-то другое";

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal(0, f.SendButton.InvokeCount);
        Assert.Equal(string.Empty, f.Composer.Value);
    }

    [Fact]
    public async Task Text_that_cannot_be_verified_is_not_sent()
    {
        var f = CreateFixture();
        f.Composer.IgnoreSetValue = true;

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.NotSent, result.Status);
        Assert.Equal(0, f.SendButton.InvokeCount);
    }

    [Fact]
    public async Task Click_without_cleared_field_is_uncertain_and_clicked_only_once()
    {
        var f = CreateFixture();   // кнопка нажимается, но поле не очищается

        var result = await f.Adapter.SendAsync(Request("@makee_dev"), CancellationToken.None);

        Assert.Equal(SendStatus.Uncertain, result.Status);
        Assert.Equal(1, f.SendButton.InvokeCount);
        Assert.Contains("повторно", result.Message);
    }

    private static MessageRequest Request(string raw, string text = "Привет")
    {
        var recipient = RecipientParser.Parse(MessengerApp.TelegramDesktop, raw).Recipient!;
        return new MessageRequest(recipient, text);
    }

    private static Fixture CreateFixture(string telegramProcess = "Telegram", TimeSpan? windowTimeout = null)
    {
        // Как в реальном дереве Telegram: обёртка и внутреннее поле, оба с Value, классы с префиксом «class ».
        var wrapper = new FakeUiElement("Edit", "Сообщение...", className: "class Ui::InputField")
        {
            Patterns = ["Invoke", "Value"],
            Value = string.Empty
        };
        var composer = new FakeUiElement("Edit", "Сообщение...", className: "class Ui::InputField::Inner")
        {
            Patterns = ["Invoke", "Value", "Text"],
            Value = string.Empty
        };
        wrapper.Add(composer);
        var search = new FakeUiElement("Edit", "Поиск", className: "class Ui::InputField::Inner")
        {
            Patterns = ["Value"],
            Value = string.Empty
        };
        var sendButton = new FakeUiElement("Button", "Отправить", className: "class Ui::SendButton")
        {
            Patterns = ["Invoke"]
        };
        var window = new FakeUiElement("Window", "Telegram");
        window.Add(wrapper);
        window.Add(search);
        window.Add(sendButton);

        var finder = new FakeWindowFinder(new WindowInfo(window, telegramProcess == "Telegram" ? "Анна – (1)" : "Telegram Web", telegramProcess, 20));
        var launcher = new FakeLauncher();
        var adapter = new TelegramDesktopAdapter(
            finder, launcher, TimeProvider.System, windowTimeout: windowTimeout);

        return new Fixture(adapter, launcher, composer, sendButton);
    }

    private sealed record Fixture(
        TelegramDesktopAdapter Adapter, FakeLauncher Launcher, FakeUiElement Composer, FakeUiElement SendButton);

    private sealed class FakeLauncher : ITelegramLinkLauncher
    {
        public List<string> Opened { get; } = [];

        public void Open(string link) => Opened.Add(link);
    }
}
