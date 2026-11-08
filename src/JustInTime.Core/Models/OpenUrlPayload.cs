namespace JustInTime.Core.Models;

public sealed record OpenUrlPayload(string Url) : TaskPayload
{
    public override ActionType GetActionType() => ActionType.OpenUrl;

    public override TaskPayload Normalize() => this with { Url = (Url ?? string.Empty).Trim() };
}
