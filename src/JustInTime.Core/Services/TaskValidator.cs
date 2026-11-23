using JustInTime.Core.Models;
using JustInTime.Core.Localization;

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

    public static IReadOnlyList<string> Validate(
        string? title, TaskPayload? payload, bool allowMultiline = MessagingFeatures.MultilineMessages)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(Loc.T("val_title_empty"));
        }
        else if (title.Trim().Length > MaxTitleLength)
        {
            errors.Add(Loc.T("val_title_long", MaxTitleLength));
        }

        switch (payload)
        {
            case null:
                errors.Add(Loc.T("val_no_params"));
                break;
            case SendMessagePayload message:
                ValidateMessage(message, errors, allowMultiline);
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
                errors.Add(Loc.T("val_unknown_action"));
                break;
        }

        return errors;
    }

    private static void ValidateMessage(SendMessagePayload payload, List<string> errors, bool allowMultiline)
    {
        if (!Enum.IsDefined(payload.App))
        {
            errors.Add(Loc.T("val_choose_messenger"));
        }

        if (string.IsNullOrWhiteSpace(payload.Recipient))
        {
            errors.Add(Loc.T("rcp_empty"));
        }
        else if (payload.Recipient.Length > MaxRecipientLength)
        {
            errors.Add(Loc.T("val_recipient_long", MaxRecipientLength));
        }
        else if (Enum.IsDefined(payload.App))
        {
            var parsed = RecipientParser.Parse(payload.App, payload.Recipient);

            if (!parsed.IsValid)
            {
                errors.Add(parsed.Error!);
            }
            else if (payload.App == MessengerApp.WhatsApp
                     && parsed.Recipient!.Kind != RecipientKind.Phone)
            {
                errors.Add(Loc.T("val_wa_phone"));
            }
            else if (payload.App is MessengerApp.Telegram or MessengerApp.TelegramDesktop
                     && parsed.Recipient!.Kind == RecipientKind.Name)
            {
                errors.Add(Loc.T("val_tg_recipient"));
            }
        }

        if (string.IsNullOrWhiteSpace(payload.Text))
        {
            errors.Add(Loc.T("val_text_empty"));
        }
        else if (!allowMultiline && payload.Text.Trim().Any(c => c is '\r' or '\n'))
        {
            errors.Add(Loc.T("val_text_multiline"));
        }
        else if (payload.Text.Length > MaxTextLength)
        {
            errors.Add(Loc.T("val_text_long", MaxTextLength));
        }
    }

    private static void ValidateSetting(SystemSettingPayload payload, List<string> errors)
    {
        if (!Enum.IsDefined(payload.Setting))
        {
            errors.Add(Loc.T("val_choose_setting"));
        }
    }

    private static void ValidateProgram(LaunchProgramPayload payload, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(payload.ProgramPath))
        {
            errors.Add(Loc.T("val_path_empty"));
        }
        else if (payload.ProgramPath.Length > MaxPathLength)
        {
            errors.Add(Loc.T("val_path_long", MaxPathLength));
        }
        else if (payload.ProgramPath.Any(char.IsControl))
        {
            errors.Add(Loc.T("val_path_chars"));
        }

        if (payload.Arguments is { } arguments)
        {
            if (arguments.Length > MaxArgumentsLength)
            {
                errors.Add(Loc.T("val_args_long", MaxArgumentsLength));
            }
            else if (arguments.Any(c => char.IsControl(c) && c is not ('\t')))
            {
                errors.Add(Loc.T("val_args_chars"));
            }
        }
    }

    private static void ValidateUrl(OpenUrlPayload payload, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(payload.Url))
        {
            errors.Add(Loc.T("val_url_empty"));
            return;
        }

        if (payload.Url.Length > MaxUrlLength)
        {
            errors.Add(Loc.T("val_url_long", MaxUrlLength));
            return;
        }

        // Только http и https: схемы вроде file: или javascript: открывать по расписанию небезопасно.
        var valid = Uri.TryCreate(payload.Url, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        if (!valid)
        {
            errors.Add(Loc.T("val_url_scheme"));
        }
    }
}
