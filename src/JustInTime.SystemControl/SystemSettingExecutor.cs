using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.Core.Localization;

namespace JustInTime.SystemControl;

/// <summary>
/// Включает и выключает Bluetooth и Wi-Fi. Авиарежим и мобильный интернет у Windows не имеют официального
/// API для обычных приложений, поэтому такие задачи завершаются честной ошибкой, а не имитацией.
/// </summary>
public sealed class SystemSettingExecutor(IRadioService radios) : ActionExecutor<SystemSettingPayload>
{
    public override ActionType ActionType => ActionType.ToggleSystemSetting;

    protected override async Task<ExecutionOutcome> ExecuteAsync(
        SystemSettingPayload payload, CancellationToken cancellationToken)
    {
        switch (payload.Setting)
        {
            case SystemSettingKind.Bluetooth:
                return Map(await radios.SetAsync(SystemRadio.Bluetooth, payload.Enable, cancellationToken));

            case SystemSettingKind.WiFi:
                return Map(await radios.SetAsync(SystemRadio.WiFi, payload.Enable, cancellationToken));

            case SystemSettingKind.AirplaneMode:
                return ExecutionOutcome.Failed(
                    Loc.T("sys_airplane_unsupported"));

            case SystemSettingKind.MobileData:
                return ExecutionOutcome.Failed(
                    Loc.T("sys_mobile_unsupported"));

            default:
                return ExecutionOutcome.Failed(Loc.T("sys_unknown_setting"));
        }
    }

    private static ExecutionOutcome Map(RadioSwitchResult result) =>
        result.Status == RadioSwitchStatus.Done
            ? ExecutionOutcome.Success()
            : ExecutionOutcome.Failed(result.Message ?? Loc.T("sys_adapter_failed"));
}
