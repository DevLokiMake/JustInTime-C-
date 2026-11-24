using JustInTime.Core.Models;
using JustInTime.Core.Services;
using JustInTime.SystemControl;

namespace JustInTime.Tests;

/// <summary>
/// Исполнитель громкости с поддельным IVolumeService. Реальное изменение громкости (Core Audio) автотестами
/// не проверить, оно проверяется вручную (см. README).
/// </summary>
public sealed class VolumeTests
{
    private sealed class FakeVolumeService : IVolumeService
    {
        public VolumeResult Result { get; set; } = VolumeResult.Done();

        public List<(int Level, bool Unmute)> Calls { get; } = [];

        public Task<VolumeResult> SetAsync(int levelPercent, bool unmute, CancellationToken cancellationToken)
        {
            Calls.Add((levelPercent, unmute));
            return Task.FromResult(Result);
        }
    }

    private readonly FakeVolumeService _volume = new();

    private Task<ExecutionOutcome> RunAsync(SetVolumePayload payload) =>
        new SetVolumeExecutor(_volume).ExecuteAsync(payload, CancellationToken.None);

    [Fact]
    public void Executor_handles_set_volume_and_does_not_need_an_interactive_desktop()
    {
        var executor = new SetVolumeExecutor(_volume);

        Assert.Equal(ActionType.SetVolume, executor.ActionType);
        Assert.False(executor.RequiresInteractiveDesktop);
    }

    [Fact]
    public async Task Confirmed_volume_is_success_and_passes_level_and_unmute()
    {
        var outcome = await RunAsync(new SetVolumePayload(70, true));

        Assert.Equal(OutcomeKind.Success, outcome.Kind);
        Assert.Equal([(70, true)], _volume.Calls);
    }

    [Theory]
    [InlineData(VolumeStatus.NoDevice)]
    [InlineData(VolumeStatus.NotConfirmed)]
    [InlineData(VolumeStatus.Failed)]
    public async Task Any_non_confirmed_result_is_a_failure_with_the_reason(VolumeStatus status)
    {
        _volume.Result = VolumeResult.Error(status, "причина");

        var outcome = await RunAsync(new SetVolumePayload(30, false));

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Contains("причина", outcome.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Out_of_range_level_fails_without_touching_the_system(int level)
    {
        var outcome = await RunAsync(new SetVolumePayload(level));

        Assert.Equal(OutcomeKind.Failed, outcome.Kind);
        Assert.Empty(_volume.Calls);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(50, true)]
    public void Valid_levels_pass_validation(int level, bool ok)
    {
        Assert.Equal(ok, TaskValidator.Validate("t", new SetVolumePayload(level)).Count == 0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Out_of_range_level_is_rejected_by_validation(int level)
    {
        Assert.Single(TaskValidator.Validate("t", new SetVolumePayload(level)));
    }

    [Fact]
    public void Payload_round_trips_through_json_and_keeps_its_action_type()
    {
        var payload = new SetVolumePayload(65, false);

        var json = TaskPayloadSerializer.Serialize(payload);
        var back = TaskPayloadSerializer.Deserialize(ActionType.SetVolume, json);

        Assert.Equal(payload, back);
        Assert.Equal(ActionType.SetVolume, back!.GetActionType());
    }
}
