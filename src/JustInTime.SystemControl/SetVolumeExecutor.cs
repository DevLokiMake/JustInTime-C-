using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.SystemControl;

/// <summary>
/// Выставляет общую громкость. Не требует интерактивного рабочего стола: Core Audio работает и при заблокированном экране.
/// Успех только если громкость подтверждена повторным чтением.
/// </summary>
public sealed class SetVolumeExecutor(IVolumeService volume) : ActionExecutor<SetVolumePayload>
{
    public override ActionType ActionType => ActionType.SetVolume;

    protected override async Task<ExecutionOutcome> ExecuteAsync(
        SetVolumePayload payload, CancellationToken cancellationToken)
    {
        if (payload.Level is < SetVolumePayload.MinLevel or > SetVolumePayload.MaxLevel)
        {
            return ExecutionOutcome.Failed(Loc.T("val_volume_range"));
        }

        var result = await volume.SetAsync(payload.Level, payload.Unmute, cancellationToken);

        return result.Status == VolumeStatus.Done
            ? ExecutionOutcome.Success()
            : ExecutionOutcome.Failed(result.Message ?? Loc.T("vol_failed", string.Empty));
    }
}
