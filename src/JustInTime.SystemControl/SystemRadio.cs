namespace JustInTime.SystemControl;

/// <summary>Радиомодули, которыми Windows даёт управлять обычному приложению.</summary>
public enum SystemRadio
{
    Bluetooth,
    WiFi
}

public enum RadioSwitchStatus
{
    /// <summary>Радиомодуль в нужном состоянии, и это подтверждено повторным чтением состояния.</summary>
    Done,

    /// <summary>В компьютере нет такого адаптера.</summary>
    NoAdapter,

    /// <summary>Windows не разрешила приложению менять радиомодули (настройки конфиденциальности или политика).</summary>
    AccessDenied,

    /// <summary>Адаптер отключён системой: аппаратный переключатель, авиарежим или политика. Включить его программно нельзя.</summary>
    Blocked,

    /// <summary>Запрос принят, но за отведённое время состояние не изменилось.</summary>
    NotConfirmed,

    /// <summary>Неожиданная ошибка системы.</summary>
    Failed
}

public sealed record RadioSwitchResult(RadioSwitchStatus Status, string? Message)
{
    public static RadioSwitchResult Done(string? message = null) => new(RadioSwitchStatus.Done, message);

    public static RadioSwitchResult Error(RadioSwitchStatus status, string message) => new(status, message);
}

/// <summary>Переключение радиомодулей. Вынесено в интерфейс, чтобы исполнитель проверялся без Windows API.</summary>
public interface IRadioService
{
    /// <summary>
    /// Включает или выключает все адаптеры указанного вида и проверяет итоговое состояние.
    /// Ожидаемые сбои возвращаются в результате, а не бросаются.
    /// </summary>
    Task<RadioSwitchResult> SetAsync(SystemRadio radio, bool enable, CancellationToken cancellationToken);
}
