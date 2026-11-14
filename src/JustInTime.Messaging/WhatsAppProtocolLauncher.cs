using System.Diagnostics;

namespace JustInTime.Messaging;

public interface IWhatsAppProtocolLauncher
{
    void OpenChat(string phoneDigits);
}

public sealed class WhatsAppProtocolLauncher : IWhatsAppProtocolLauncher
{
    public void OpenChat(string phoneDigits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneDigits);

        if (phoneDigits.Any(c => !char.IsAsciiDigit(c)))
        {
            throw new ArgumentException("Номер WhatsApp должен содержать только цифры без знака +.", nameof(phoneDigits));
        }

        Process.Start(new ProcessStartInfo($"whatsapp://send?phone={phoneDigits}")
        {
            UseShellExecute = true
        });
    }
}
