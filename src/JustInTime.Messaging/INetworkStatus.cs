using System.Net.NetworkInformation;

namespace JustInTime.Messaging;

public interface INetworkStatus
{
    bool IsAvailable { get; }
}

/// <summary>
/// Проверка наличия сетевого подключения средствами Windows. Это не гарантия доступа к серверам мессенджера:
/// она лишь отсекает очевидный случай «интернета нет совсем».
/// </summary>
public sealed class SystemNetworkStatus : INetworkStatus
{
    public bool IsAvailable => NetworkInterface.GetIsNetworkAvailable();
}
