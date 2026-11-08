using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

public sealed class TaskValidatorTests
{
    private static readonly SendMessagePayload ValidMessage = new(MessengerApp.WhatsApp, "Анна", "Привет");

    [Fact]
    public void Valid_message_has_no_errors()
    {
        Assert.Empty(TaskValidator.Validate("Поздравить", ValidMessage));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_title_is_rejected(string? title)
    {
        Assert.Single(TaskValidator.Validate(title, ValidMessage));
    }

    [Fact]
    public void Too_long_title_is_rejected()
    {
        Assert.Single(TaskValidator.Validate(new string('я', TaskValidator.MaxTitleLength + 1), ValidMessage));
        Assert.Empty(TaskValidator.Validate(new string('я', TaskValidator.MaxTitleLength), ValidMessage));
    }

    [Fact]
    public void Missing_payload_is_rejected()
    {
        Assert.Single(TaskValidator.Validate("задача", null));
    }

    [Fact]
    public void Message_requires_recipient_and_text()
    {
        Assert.Single(TaskValidator.Validate("t", new SendMessagePayload(MessengerApp.Telegram, "", "текст")));
        Assert.Single(TaskValidator.Validate("t", new SendMessagePayload(MessengerApp.Telegram, "Анна", "  ")));
        Assert.Equal(2, TaskValidator.Validate("t", new SendMessagePayload(MessengerApp.Telegram, "", "")).Count);
    }

    [Fact]
    public void Message_with_unknown_messenger_is_rejected()
    {
        Assert.Single(TaskValidator.Validate("t", new SendMessagePayload((MessengerApp)99, "Анна", "Привет")));
    }

    [Fact]
    public void Several_problems_are_all_reported_at_once()
    {
        var errors = TaskValidator.Validate("", new SendMessagePayload(MessengerApp.WhatsApp, "", ""));

        Assert.Equal(3, errors.Count);
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com/path?q=1", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/system32/calc.exe", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("example.com", false)]
    [InlineData("не ссылка", false)]
    [InlineData("", false)]
    public void Url_must_be_absolute_http_or_https(string url, bool valid)
    {
        var errors = TaskValidator.Validate("t", new OpenUrlPayload(url));

        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void Program_path_is_required_and_must_not_contain_control_characters()
    {
        Assert.Empty(TaskValidator.Validate("t", new LaunchProgramPayload(@"C:\Windows\notepad.exe", null)));
        Assert.Single(TaskValidator.Validate("t", new LaunchProgramPayload("", null)));
        Assert.Single(TaskValidator.Validate("t", new LaunchProgramPayload("a\0b.exe", null)));
        Assert.Single(TaskValidator.Validate("t", new LaunchProgramPayload("a.exe", "line1\nline2")));
    }

    [Fact]
    public void System_setting_with_unknown_value_is_rejected()
    {
        Assert.Empty(TaskValidator.Validate("t", new SystemSettingPayload(SystemSettingKind.WiFi, true)));
        Assert.Single(TaskValidator.Validate("t", new SystemSettingPayload((SystemSettingKind)77, true)));
    }
}
