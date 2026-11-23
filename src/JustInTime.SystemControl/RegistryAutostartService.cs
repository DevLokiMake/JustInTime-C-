using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using Microsoft.Win32;
using JustInTime.Core.Localization;

namespace JustInTime.SystemControl;

/// <summary>
/// Автозапуск через раздел реестра текущего пользователя HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Это стандартный способ без прав администратора. Если пользователь отключил запись в Параметрах Windows,
/// система хранит это в StartupApproved\Run: такое состояние мы показываем и не обходим.
/// </summary>
public sealed class RegistryAutostartService : IAutostartService
{
    /// <summary>Ключ запуска, при котором приложение стартует сразу в трей, без окна.</summary>
    public const string BackgroundArgument = "--background";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly string _valueName;
    private readonly string _executablePath;
    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;

    public RegistryAutostartService(string executablePath, string valueName = "JustInTime")
        : this(executablePath, valueName, RunKeyPath, ApprovedKeyPath)
    {
    }

    /// <summary>Для тестов: разделы реестра можно подменить, чтобы не трогать настоящую автозагрузку.</summary>
    internal RegistryAutostartService(string executablePath, string valueName, string runKeyPath, string approvedKeyPath)
    {
        _executablePath = executablePath;
        _valueName = valueName;
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
    }

    /// <summary>Строка команды, которую запишет автозапуск.</summary>
    public string Command => $"\"{_executablePath}\" {BackgroundArgument}";

    public AutostartStatus GetStatus()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath);

        if (run?.GetValue(_valueName) is not string stored
            || !string.Equals(stored, Command, StringComparison.OrdinalIgnoreCase))
        {
            // Нет записи или она указывает на другую копию программы: для этой копии автозапуск выключен.
            return AutostartStatus.Off;
        }

        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath);

        // Первый байт: чётное значение (02, 06) значит «разрешено», нечётное (03) значит «отключено пользователем».
        if (approved?.GetValue(_valueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1)
        {
            return AutostartStatus.DisabledInWindows;
        }

        return AutostartStatus.On;
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var fileName = Path.GetFileNameWithoutExtension(_executablePath);

            if (string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    Loc.T("sys_autostart_exe"));
            }

            if (!File.Exists(_executablePath))
            {
                throw new InvalidOperationException(Loc.T("sys_autostart_nofile", _executablePath));
            }
        }

        try
        {
            if (enabled)
            {
                using var run = Registry.CurrentUser.CreateSubKey(_runKeyPath);
                run.SetValue(_valueName, Command, RegistryValueKind.String);

                // Если раньше запись отключали в Параметрах Windows, при явном включении возвращаем её в рабочее состояние.
                using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
                approved?.DeleteValue(_valueName, throwOnMissingValue: false);
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
                run?.DeleteValue(_valueName, throwOnMissingValue: false);

                using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
                approved?.DeleteValue(_valueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InvalidOperationException(Loc.T("sys_autostart_registry", ex.Message), ex);
        }
    }
}
