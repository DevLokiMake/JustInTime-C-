namespace JustInTime.SystemControl;

public enum VolumeStatus
{
    /// <summary>Громкость выставлена и подтверждена повторным чтением.</summary>
    Done,

    /// <summary>Нет устройства вывода звука по умолчанию.</summary>
    NoDevice,

    /// <summary>Запрос принят, но прочитанное значение не совпало (драйвер или политика).</summary>
    NotConfirmed,

    Failed
}

public sealed record VolumeResult(VolumeStatus Status, string? Message)
{
    public static VolumeResult Done() => new(VolumeStatus.Done, null);

    public static VolumeResult Error(VolumeStatus status, string message) => new(status, message);
}

/// <summary>Управление общей громкостью. Интерфейс нужен, чтобы исполнитель проверялся без Windows API.</summary>
public interface IVolumeService
{
    /// <summary>Выставляет громкость 0..100 устройства вывода по умолчанию, при unmute снимает «без звука», и проверяет итог.</summary>
    Task<VolumeResult> SetAsync(int levelPercent, bool unmute, CancellationToken cancellationToken);
}
