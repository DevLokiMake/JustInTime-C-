using JustInTime.Core.Models;
using JustInTime.SystemControl;

namespace JustInTime.Tests;

/// <summary>
/// Исполнитель с поддельным IRadioService. Реальное переключение Bluetooth и Wi-Fi автотестами не проверить,
/// оно проверяется вручную (см. README).
/// </summary>
public sealed class SystemSettingExecutorTests
{
    private sealed class FakeRadioService : IRadioService
    {
        public RadioSwitchResult Result { get; set; } = RadioSwitchResult.Done();

        public List<(SystemRadio Radio, bool Enable)> Calls { get; } = new();

        public Task<RadioSwitchResult> SetAsync(SystemRadio radio, bool enable, CancellationToken cancellationToken)
        {
            Calls.Add((radio, enable));
            return Task.FromResult(Result);
        }
    }

    private readonly FakeRadioService _radios = new();

    private Task<ExecutionOutcome> RunAsync(SystemSettingKind setting, bool enable) =>
        new SystemSettingExecutor(_radios).ExecuteAsync(new SystemSettingPayload(setting, enable), CancellationToken.None);

    [Fact]
    public void Executor_handles_system_setting_action()
    {
        Assert.Equal(ActionType.ToggleSystemSetting, new SystemSettingExecutor(_radios).ActionType);
    }

    [Theory]
    [InlineData(SystemSettingKind.Bluetooth, SystemRadio.Bluetooth, false)]
    [InlineData(SystemSettingKind.Bluetooth, SystemRadio.Bluetooth, true)]
    [InlineData(SystemSettingKind.WiFi, SystemRadio.WiFi, false)]
    [InlineData(SystemSettingKind.WiFi, SystemRadio.WiFi, true)]
    public async Task Confirmed_switch_is_success_and_uses_the_right_radio(
        SystemSettingKind setting, SystemRadio expectedRadio, bool enable)
    {
        var outcome = await RunAsync(setting, enable);

        Assert.Equal(OutcomeKind.Success, outcome.Kind);
        Assert.Equal((expectedRadio, enable), Assert.Single(_radios.Calls));
    }

    [Theory]
    [InlineData(RadioSwitchStatus.NoAdapter)]
    [InlineData(RadioSwitchStatus.AccessDenied)]
    [InlineData(RadioSwitchStatus.Blocked)]
    [InlineData(RadioSwitchStatus.NotConfirmed)]
    [InlineData(RadioSwitchStatus.Failed)]
    public async Task Any_unconfirmed_result_is_a_failure_with_the_reason(RadioSwitchStatus status)
    {
        _radios.Result = RadioSwitchResult.Error(status, "причина");

        var outcome = await RunAsync(SystemSettingKind.Bluetooth, false);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Equal("причина", outcome.Message);
    }

    [Theory]
    [InlineData(SystemSettingKind.AirplaneMode)]
    [InlineData(SystemSettingKind.MobileData)]
    public async Task Unsupported_settings_fail_honestly_without_touching_radios(SystemSettingKind setting)
    {
        var outcome = await RunAsync(setting, true);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Contains("не поддерживается", outcome.Message);
        Assert.Empty(_radios.Calls);
    }

    [Fact]
    public async Task Payload_of_another_action_type_is_rejected()
    {
        var outcome = await new SystemSettingExecutor(_radios).ExecuteAsync(new OpenUrlPayload("https://a.com"), CancellationToken.None);

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Empty(_radios.Calls);
    }
}
