using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using JustInTime.Core.Cli;

namespace JustInTime.App.Services;

/// <summary>
/// Консоль для командного режима. Приложение собрано как оконное (WinExe), поэтому своей консоли у него нет:
/// подключаемся к консоли родителя (cmd, PowerShell). Если родителя с консолью нет (запуск из Планировщика заданий),
/// вывода не будет, а спросить подтверждение нельзя: тогда нужен ключ --force / -y.
/// </summary>
public sealed class WindowsCliConsole : ICliConsole, IDisposable
{
    private const int AttachParentProcess = -1;

    private readonly bool _attached;
    private readonly Encoding? _previousOutputEncoding;

    public WindowsCliConsole()
    {
        _attached = AttachConsole(AttachParentProcess);

        if (!_attached)
        {
            return;
        }

        _previousOutputEncoding = Console.OutputEncoding;
        var utf8 = new UTF8Encoding(false);
        Console.OutputEncoding = utf8;
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), Console.InputEncoding));
    }

    public bool CanPrompt => _attached && !Console.IsInputRedirected;

    public void WriteLine(string text)
    {
        if (_attached)
        {
            Console.Out.WriteLine(text);
        }
    }

    public void Write(string text)
    {
        if (_attached)
        {
            Console.Out.Write(text);
        }
    }

    public string? ReadLine() => _attached ? Console.In.ReadLine() : null;

    public void Dispose()
    {
        if (!_attached)
        {
            return;
        }

        if (_previousOutputEncoding is not null)
        {
            Console.OutputEncoding = _previousOutputEncoding;
        }

        FreeConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
