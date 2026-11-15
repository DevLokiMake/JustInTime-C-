using System.Diagnostics;

namespace JustInTime.Messaging;

public interface ITelegramLinkLauncher
{
    /// <summary>Открывает ссылку вида tg://resolve?... в установленном Telegram Desktop.</summary>
    void Open(string link);
}

public sealed class TelegramLinkLauncher : ITelegramLinkLauncher
{
    public void Open(string link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        if (!link.StartsWith("tg://resolve?", StringComparison.Ordinal))
        {
            throw new ArgumentException("Разрешены только ссылки tg://resolve?...", nameof(link));
        }

        Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
    }
}
