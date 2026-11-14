using System.Text.RegularExpressions;
using JustInTime.Core.Models;

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
            return Fail("Укажите получателя.");
        }

        if (PhoneRegex.IsMatch(text))
        {
            var digits = new string(text.Where(char.IsDigit).ToArray());

            return digits.Length is < MinPhoneDigits or > MaxPhoneDigits
                ? Fail($"В номере должно быть от {MinPhoneDigits} до {MaxPhoneDigits} цифр в международном формате, например +77011234567.")
                : Ok(RecipientKind.Phone, "+" + digits);
        }

        if (NumberLikeRegex.IsMatch(text))
        {
            return Fail("Номер нужно указывать в международном формате с плюсом, например +77011234567. Без кода страны получателя не определить.");
        }

        if (text.StartsWith('@'))
        {
            if (app == MessengerApp.WhatsApp)
            {
                return Fail("В WhatsApp нет имён пользователей @. Укажите номер телефона или имя контакта.");
            }

            return UsernameRegex.IsMatch(text)
                ? Ok(RecipientKind.Username, text)
                : Fail("Имя пользователя Telegram: @ и от 5 до 32 символов (латинские буквы, цифры, _), начинается с буквы.");
        }

        return text.Any(char.IsLetter)
            ? Ok(RecipientKind.Name, text)
            : Fail("Получатель не распознан. Укажите номер с плюсом, @имя или имя контакта.");
    }

    private static RecipientParseResult Ok(RecipientKind kind, string value) =>
        new(new ParsedRecipient(kind, value), null);

    private static RecipientParseResult Fail(string error) => new(null, error);
}
