namespace JustInTime.Core.Models;

/// <summary>Установить общую громкость устройства вывода звука по умолчанию. Unmute: заодно снять режим «без звука».</summary>
public sealed record SetVolumePayload(int Level, bool Unmute = true) : TaskPayload
{
    public const int MinLevel = 0;
    public const int MaxLevel = 100;

    public override ActionType GetActionType() => ActionType.SetVolume;

    public override TaskPayload Normalize() => this;
}
