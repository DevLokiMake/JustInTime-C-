using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

/// <summary>Запуск приложения при входе пользователя в Windows (без прав администратора, только для текущего пользователя).</summary>
public interface IAutostartService
{
    AutostartStatus GetStatus();

    /// <summary>Включает или выключает автозапуск. Бросает InvalidOperationException с понятным текстом, если это невозможно.</summary>
    void SetEnabled(bool enabled);
}
