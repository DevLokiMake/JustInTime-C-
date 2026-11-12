namespace JustInTime.Core.Interfaces;

/// <summary>
/// Состояние пользовательского рабочего стола. Автоматизация интерфейса возможна только на разблокированном
/// интерактивном столе: на экране блокировки ввод имитировать нельзя и нельзя пытаться.
/// </summary>
public interface IDesktopSession
{
    /// <summary>true, если рабочий стол пользователя доступен для автоматизации интерфейса.</summary>
    bool IsAvailable(out string reason);
}
