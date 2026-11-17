using System.Text.Json;
using System.Text.Json.Serialization;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Правило повтора в JSON для колонки Tasks.Recurrence и обратно.</summary>
public static class RecurrenceSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(RecurrenceRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return JsonSerializer.Serialize(rule, Options);
    }

    /// <summary>Правило или null, если JSON пуст, повреждён или правило не проходит проверку.</summary>
    public static RecurrenceRule? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var rule = JsonSerializer.Deserialize<RecurrenceRule>(json, Options);
            return rule is not null && RecurrenceValidator.Validate(rule).Count == 0 ? rule : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
