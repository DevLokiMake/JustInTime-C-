namespace JustInTime.Core.Models;

/// <summary>
/// Системная функция, которую можно включить или выключить по расписанию.
/// Какие из них реально доступны на конкретной платформе, определит модуль SystemControl.
/// </summary>
public enum SystemSettingKind
{
    AirplaneMode,
    MobileData,
    Bluetooth,
    WiFi
}
