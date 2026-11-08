using JustInTime.Core.Models;

namespace JustInTime.App.ViewModels;

/// <summary>Русские названия для перечислений и параметров действий.</summary>
public static class UiNames
{
    public static string Action(ActionType action) => action switch
    {
        ActionType.SendMessage => "Отправить сообщение",
        ActionType.ToggleSystemSetting => "Системная настройка",
        ActionType.LaunchProgram => "Запустить программу",
        ActionType.OpenUrl => "Открыть ссылку",
        _ => action.ToString()
    };

    public static string Messenger(MessengerApp app) => app switch
    {
        MessengerApp.WhatsApp => "WhatsApp",
        MessengerApp.Telegram => "Telegram",
        MessengerApp.TelegramDesktop => "Telegram Desktop",
        _ => app.ToString()
    };

    public static string Setting(SystemSettingKind setting) => setting switch
    {
        SystemSettingKind.AirplaneMode => "Авиарежим",
        SystemSettingKind.MobileData => "Мобильный интернет",
        SystemSettingKind.Bluetooth => "Bluetooth",
        SystemSettingKind.WiFi => "Wi-Fi",
        _ => setting.ToString()
    };

    /// <summary>Одна строка с сутью действия для списка задач.</summary>
    public static string Summary(TaskPayload? payload) => payload switch
    {
        SendMessagePayload m => $"{Messenger(m.App)}, получатель: {m.Recipient}",
        SystemSettingPayload s => $"{Setting(s.Setting)}: {(s.Enable ? "включить" : "выключить")}",
        LaunchProgramPayload l => l.ProgramPath,
        OpenUrlPayload u => u.Url,
        _ => "Параметры задачи не удалось прочитать"
    };
}
