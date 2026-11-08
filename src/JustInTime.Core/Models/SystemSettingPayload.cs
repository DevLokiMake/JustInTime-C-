namespace JustInTime.Core.Models;

public sealed record SystemSettingPayload(SystemSettingKind Setting, bool Enable) : TaskPayload
{
    public override ActionType GetActionType() => ActionType.ToggleSystemSetting;

    public override TaskPayload Normalize() => this;
}
