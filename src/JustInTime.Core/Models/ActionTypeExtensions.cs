using JustInTime.Core.Localization;

namespace JustInTime.Core.Models;

public static class ActionTypeExtensions
{
    /// <summary>Название действия для интерфейса, истории и сообщений об ошибках на текущем языке.</summary>
    public static string ToDisplayName(this ActionType action) => action switch
    {
        ActionType.SendMessage => Loc.T("action_send_message"),
        ActionType.ToggleSystemSetting => Loc.T("action_system_setting"),
        ActionType.LaunchProgram => Loc.T("action_launch_program"),
        ActionType.OpenUrl => Loc.T("action_open_url"),
        _ => action.ToString()
    };
}
