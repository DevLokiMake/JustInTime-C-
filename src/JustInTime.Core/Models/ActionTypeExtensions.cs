namespace JustInTime.Core.Models;

public static class ActionTypeExtensions
{
    /// <summary>Русское название действия для интерфейса, истории и сообщений об ошибках.</summary>
    public static string ToDisplayName(this ActionType action) => action switch
    {
        ActionType.SendMessage => "Отправить сообщение",
        ActionType.ToggleSystemSetting => "Системная настройка",
        ActionType.LaunchProgram => "Запустить программу",
        ActionType.OpenUrl => "Открыть ссылку",
        _ => action.ToString()
    };
}
