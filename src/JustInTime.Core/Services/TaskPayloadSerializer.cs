using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Перевод параметров действия в JSON для колонки Tasks.Payload и обратно.</summary>
public static class TaskPayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(TaskPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return JsonSerializer.Serialize(payload, payload.GetType(), Options);
    }

    /// <summary>Параметры нужного типа или null, если JSON повреждён либо не подходит типу действия.</summary>
    public static TaskPayload? Deserialize(ActionType action, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            switch (action)
            {
                case ActionType.SendMessage:
                    return JsonSerializer.Deserialize<SendMessagePayload>(json, Options);
                case ActionType.ToggleSystemSetting:
                    return JsonSerializer.Deserialize<SystemSettingPayload>(json, Options);
                case ActionType.LaunchProgram:
                    return JsonSerializer.Deserialize<LaunchProgramPayload>(json, Options);
                case ActionType.OpenUrl:
                    return JsonSerializer.Deserialize<OpenUrlPayload>(json, Options);
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
