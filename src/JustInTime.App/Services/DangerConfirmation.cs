using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.App.Services;

/// <summary>Режим подтверждений для этого запуска: Force (ключ --force / -y) отключает вопросы.</summary>
public sealed record ConfirmationOptions(bool Force);

/// <summary>Подтверждение опасных операций в окне приложения.</summary>
public interface IDangerConfirmation
{
    /// <summary>true, если можно продолжать: причин нет, указан --force / -y или пользователь согласился.</summary>
    bool Confirm(IReadOnlyList<DangerReason> reasons);
}

public sealed class DangerConfirmation(IDialogService dialogs, ConfirmationOptions options) : IDangerConfirmation
{
    public bool Confirm(IReadOnlyList<DangerReason> reasons)
    {
        if (!ConfirmationPolicy.NeedsConfirmation(reasons, options.Force))
        {
            return true;
        }

        var message = string.Join(Environment.NewLine + Environment.NewLine, reasons.Select(r => r.Text))
                      + Environment.NewLine + Environment.NewLine + Loc.T("danger_confirm_question");

        return dialogs.Confirm(Loc.T("danger_confirm_title"), message);
    }
}
