using JustInTime.Core.Localization;
using JustInTime.Core.Models;

namespace JustInTime.App.ViewModels;

/// <summary>Названия для перечислений и параметров действий на текущем языке интерфейса.</summary>
public static class UiNames
{
    public static string Action(ActionType action) => action.ToDisplayName();

    public static string Messenger(MessengerApp app) => app switch
    {
        MessengerApp.WhatsApp => "WhatsApp",
        MessengerApp.Telegram => "Telegram",
        MessengerApp.TelegramDesktop => "Telegram Desktop",
        _ => app.ToString()
    };

    public static string Setting(SystemSettingKind setting) => setting switch
    {
        SystemSettingKind.AirplaneMode => Loc.T("setting_airplane"),
        SystemSettingKind.MobileData => Loc.T("setting_mobile_data"),
        SystemSettingKind.Bluetooth => "Bluetooth",
        SystemSettingKind.WiFi => "Wi-Fi",
        _ => setting.ToString()
    };

    /// <summary>Одна строка с сутью действия для списка задач.</summary>
    public static string Summary(TaskPayload? payload) => payload switch
    {
        SendMessagePayload m => Loc.T("summary_message", Messenger(m.App), m.Recipient),
        SystemSettingPayload s => Loc.T("summary_setting", Setting(s.Setting), Loc.T(s.Enable ? "switch_on" : "switch_off")),
        LaunchProgramPayload l => l.ProgramPath,
        OpenUrlPayload u => u.Url,
        SetVolumePayload v => Loc.T("summary_volume", v.Level, v.Unmute ? Loc.T("summary_volume_unmute") : string.Empty),
        _ => Loc.T("summary_unreadable")
    };
}
