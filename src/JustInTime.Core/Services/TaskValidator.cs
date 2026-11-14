using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

/// <summary>Проверка названия и параметров действия. Время проверяется в ScheduleResolver.</summary>
public static class TaskValidator
{
    public const int MaxTitleLength = 200;
    public const int MaxRecipientLength = 200;
    public const int MaxTextLength = 4000;
    public const int MaxPathLength = 1000;
    public const int MaxArgumentsLength = 2000;
    public const int MaxUrlLength = 2000;

    public static IReadOnlyList<string> Validate(string? title, TaskPayload? payload)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add("Введите название задачи.");
        }
        else if (title.Trim().Length > MaxTitleLength)
        {
            errors.Add($"Название не должно быть длиннее {MaxTitleLength} символов.");
        }

        switch (payload)
        {
            case null:
                errors.Add("Не указаны параметры действия.");
                break;
            case SendMessagePayload message:
                ValidateMessage(message, errors);
                break;
            case SystemSettingPayload setting:
                ValidateSetting(setting, errors);
                break;
            case LaunchProgramPayload program:
                ValidateProgram(program, errors);
                break;
            case OpenUrlPayload url:
                ValidateUrl(url, errors);
                break;
            default:
                errors.Add("Неизвестный тип действия.");
                break;
        }

        return errors;
    }

    private static void ValidateMessage(SendMessagePayload payload, List<string> errors)
    {
        if (!Enum.IsDefined(payload.App))
        {
            errors.Add("Выберите мессенджер.");
        }

        if (string.IsNullOrWhiteSpace(payload.Recipient))
        {
            errors.Add("Укажите получателя.");
        }
        else if (payload.Recipient.Length > MaxRecipientLength)
        {
            errors.Add($"Имя получателя не должно быть длиннее {MaxRecipientLength} символов.");
        }
        else if (Enum.IsDefined(payload.App))
        {
            var parsed = RecipientParser.Parse(payload.App, payload.Recipient);

            if (!parsed.IsValid)
            {
                errors.Add(parsed.Error!);
            }
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            errors.Add("Введите текст сообщения.");
        }
        else if (payload.Text.Length > MaxTextLength)
        {
            errors.Add($"Сообщение не должно быть длиннее {MaxTextLength} символов.");
        }
    }

    private static void ValidateSetting(SystemSettingPayload payload, List<string> errors)
    {
        if (!Enum.IsDefined(payload.Setting))
        {
            errors.Add("Выберите системную настройку.");
        }
    }

    private static void ValidateProgram(LaunchProgramPayload payload, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(payload.ProgramPath))
        {
            errors.Add("Укажите путь к программе.");
        }
        else if (payload.ProgramPath.Length > MaxPathLength)
        {
            errors.Add($"Путь не должен быть длиннее {MaxPathLength} символов.");
        }
        else if (payload.ProgramPath.Any(char.IsControl))
        {
            errors.Add("Путь содержит недопустимые символы.");
        }

        if (payload.Arguments is { } arguments)
        {
            if (arguments.Length > MaxArgumentsLength)
            {
                errors.Add($"Аргументы не должны быть длиннее {MaxArgumentsLength} символов.");
            }
            else if (arguments.Any(c => char.IsControl(c) && c is not ('\t')))
            {
                errors.Add("Аргументы содержат недопустимые символы.");
            }
        }
    }

    private static void ValidateUrl(OpenUrlPayload payload, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(payload.Url))
        {
            errors.Add("Укажите ссылку.");
            return;
        }

        if (payload.Url.Length > MaxUrlLength)
        {
            errors.Add($"Ссылка не должна быть длиннее {MaxUrlLength} символов.");
            return;
        }

        // Только http и https: схемы вроде file: или javascript: открывать по расписанию небезопасно.
        var valid = Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (!valid)
        {
            errors.Add("Ссылка должна начинаться с http:// или https://.");
        }
    }
}
