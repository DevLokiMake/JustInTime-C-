namespace JustInTime.Core.Models;

public enum AutostartStatus
{
    /// <summary>Автозапуск не настроен (или настроен для другого расположения программы).</summary>
    Off,

    /// <summary>Приложение запустится при входе в Windows.</summary>
    On,

    /// <summary>Запись есть, но пользователь отключил её в Параметрах Windows (Приложения, Автозагрузка). Включить может только он.</summary>
    DisabledInWindows
}
