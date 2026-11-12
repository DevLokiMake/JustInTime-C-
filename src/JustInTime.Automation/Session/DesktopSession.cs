using System.Runtime.InteropServices;
using JustInTime.Core.Interfaces;

namespace JustInTime.Automation.Session;

/// <summary>
/// Определяет, доступен ли рабочий стол пользователя для автоматизации. Через OpenInputDesktop узнаём, какой стол
/// сейчас получает ввод: на экране блокировки, при защищённом запросе UAC или в отключённом сеансе это не "Default",
/// либо дескриптор получить нельзя. Это принятый приём, а не отдельный официальный флаг «заблокировано»,
/// поэтому при любом сомнении считаем рабочий стол недоступным: безопаснее подождать, чем вводить текст вслепую.
/// </summary>
public sealed class DesktopSession : IDesktopSession
{
    private const uint DesktopReadObjects = 0x0001;
    private const int UserObjectName = 2;

    public bool IsAvailable(out string reason)
    {
        var desktop = OpenInputDesktop(0, false, DesktopReadObjects);

        if (desktop == IntPtr.Zero)
        {
            reason = "компьютер заблокирован, сеанс отключён или показан защищённый экран. Разблокируйте компьютер.";
            return false;
        }

        try
        {
            var name = ReadDesktopName(desktop);

            if (!string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"сейчас активен другой рабочий стол ({(name.Length == 0 ? "неизвестен" : name)}). Разблокируйте компьютер.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    private static string ReadDesktopName(IntPtr desktop)
    {
        var buffer = new char[256];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            return GetUserObjectInformation(
                desktop, UserObjectName, handle.AddrOfPinnedObject(), (uint)(buffer.Length * sizeof(char)), out _)
                ? new string(buffer).TrimEnd('\0')
                : string.Empty;
        }
        finally
        {
            handle.Free();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformation(
        IntPtr hObj, int nIndex, IntPtr pvInfo, uint nLength, out uint lpnLengthNeeded);
}
