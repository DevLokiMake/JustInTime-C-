using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

public sealed class TaskPayloadSerializerTests
{
    public static TheoryData<TaskPayload> Payloads => new()
    {
        new SendMessagePayload(MessengerApp.TelegramDesktop, "Анна \"Ань\"", "Привет!\nКак дела? \\ ' <b>"),
        new SystemSettingPayload(SystemSettingKind.Bluetooth, false),
        new LaunchProgramPayload(@"C:\Program Files\App Name\app.exe", "--flag \"value\""),
        new LaunchProgramPayload("notepad.exe", null),
        new OpenUrlPayload("https://example.com/?q=1&r=2")
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Serialize_then_Deserialize_returns_equal_payload(TaskPayload payload)
    {
        var json = TaskPayloadSerializer.Serialize(payload);

        var restored = TaskPayloadSerializer.Deserialize(payload.GetActionType(), json);

        Assert.Equal(payload, restored);
    }

    [Fact]
    public void Serialize_writes_readable_json_without_action_type_field()
    {
        var json = TaskPayloadSerializer.Serialize(
            new SendMessagePayload(MessengerApp.TelegramDesktop, "Анна", "Привет"));

        Assert.Contains("\"recipient\"", json);
        Assert.Contains("TelegramDesktop", json);
        Assert.Contains("Анна", json);
        Assert.DoesNotContain("actionType", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_does_not_depend_on_key_order_because_jsonb_reorders_keys()
    {
        var json = """{"text":"b","app":"Telegram","recipient":"a"}""";

        var payload = TaskPayloadSerializer.Deserialize(ActionType.SendMessage, json);

        Assert.Equal(new SendMessagePayload(MessengerApp.Telegram, "a", "b"), payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("""{"app":"НеМессенджер","recipient":"a","text":"b"}""")]
    public void Deserialize_returns_null_for_broken_or_empty_json(string json)
    {
        Assert.Null(TaskPayloadSerializer.Deserialize(ActionType.SendMessage, json));
    }

    [Fact]
    public void Normalize_trims_text_fields()
    {
        var payload = new SendMessagePayload(MessengerApp.WhatsApp, "  Анна ", "  Привет \n");

        Assert.Equal(new SendMessagePayload(MessengerApp.WhatsApp, "Анна", "Привет"), payload.Normalize());
        Assert.Equal(new LaunchProgramPayload("a.exe", null), new LaunchProgramPayload(" a.exe ", "   ").Normalize());
    }
}
