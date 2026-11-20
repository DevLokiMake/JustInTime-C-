using System.Text.RegularExpressions;

namespace JustInTime.Core.Services;

/// <summary>Приводит распознанные строки к одной строке: сообщения пока поддерживаются только однострочные.</summary>
public static partial class OcrTextNormalizer
{
    public static string ToSingleLine(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // Невидимые и управляющие символы (в том числе переносы) заменяем пробелом, потом схлопываем пробелы.
        var joined = string.Join(' ', lines.Select(l => l ?? string.Empty));
        var cleaned = new string(joined.Select(c => char.IsControl(c) || c is '​' or '‎' or '‏' or '﻿' ? ' ' : c).ToArray());

        return Spaces().Replace(cleaned, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
