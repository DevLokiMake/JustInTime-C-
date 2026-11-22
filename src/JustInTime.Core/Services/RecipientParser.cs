using System.Text.RegularExpressions;
using JustInTime.Core.Models;
using JustInTime.Core.Localization;

namespace JustInTime.Core.Services;

public enum RecipientKind
{
    /// <summary>Номер телефона в международном формате. Самый надёжный способ: однозначно определяет чат.</summary>
    Phone,

    /// <summary>Имя пользователя Telegram вида @name. Однозначно определяет аккаунт.</summary>
    Username,

    /// <summary>Имя контакта из списка чатов. Может совпасть у нескольких контактов, адаптер обязан это проверить.</summary>
    Name
}

public sealed record ParsedRecipient(RecipientKind Kind, string Value);

public sealed record RecipientParseResult(ParsedRecipient? Recipient, string? Error)
{
    public bool IsValid => Recipient is not null;
}

/// <summary>
/// Разбирает строку получателя. Номер принимается только в международном формате с плюсом: «8 701...» или «701...»
/// без кода страны программа не угадывает, чтобы не отправить сообщение не тому человеку.
/// </summary>
public static class RecipientParser
{
    public const int MinPhoneDigits = 8;
    public const int MaxPhoneDigits = 15;

    private static readonly Regex UsernameRegex = new(@"^@[A-Za-z][A-Za-z0-9_]{4,31}$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"^\+[\d\s\-().]+$", RegexOptions.Compiled);
    private static readonly Regex NumberLikeRegex = new(@"^[\d\s\-().+]+$", RegexOptions.Compiled);

    public static RecipientParseResult Parse(MessengerApp app, string? raw)
    {
        var text = (raw ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return Fail(Loc.T("rcp_empty"));
        }

        if (PhoneRegex.IsMatch(text))
        {
            var digits = new string(text.Where(char.IsDigit).ToArray());

            return digits.Length is < MinPhoneDigits or > MaxPhoneDigits
                ? Fail(Loc.T("rcp_digits", MinPhoneDigits, MaxPhoneDigits))
                : Ok(RecipientKind.Phone, "+" + digits);
        }

        if (NumberLikeRegex.IsMatch(text))
        {
            return Fail(Loc.T("rcp_plus"));
        }

        if (text.StartsWith('@'))
        {
            if (app == MessengerApp.WhatsApp)
            {
                return Fail(Loc.T("rcp_wa_username"));
            }

            return UsernameRegex.IsMatch(text)
                ? Ok(RecipientKind.Username, text)
                : Fail(Loc.T("rcp_tg_username"));
        }

        return text.Any(char.IsLetter)
            ? Ok(RecipientKind.Name, text)
            : Fail(Loc.T("rcp_unrecognized"));
    }

    private static RecipientParseResult Ok(RecipientKind kind, string value) =>
        new(new ParsedRecipient(kind, value), null);

    private static RecipientParseResult Fail(string error) => new(null, error);
}
