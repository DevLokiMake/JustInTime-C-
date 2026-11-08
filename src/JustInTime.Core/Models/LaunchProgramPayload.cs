namespace JustInTime.Core.Models;

public sealed record LaunchProgramPayload(string ProgramPath, string? Arguments) : TaskPayload
{
    public override ActionType GetActionType() => ActionType.LaunchProgram;

    public override TaskPayload Normalize() => this with
    {
        ProgramPath = (ProgramPath ?? string.Empty).Trim(),
        Arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments.Trim()
    };
}
