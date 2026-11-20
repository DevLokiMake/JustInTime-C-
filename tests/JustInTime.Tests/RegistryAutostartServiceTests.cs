using JustInTime.Core.Models;
using JustInTime.SystemControl;
using Microsoft.Win32;

namespace JustInTime.Tests;

/// <summary>Автозапуск проверяется на отдельных тестовых разделах реестра, настоящая автозагрузка не затрагивается.</summary>
public sealed class RegistryAutostartServiceTests : IDisposable
{
    private readonly string _root = $@"Software\JustInTime.Tests\{Guid.NewGuid():N}";
    private readonly string _exe = Path.Combine(Path.GetTempPath(), $"jit-{Guid.NewGuid():N}.exe");

    public RegistryAutostartServiceTests() => File.WriteAllText(_exe, string.Empty);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\JustInTime.Tests", throwOnMissingSubKey: false);
        File.Delete(_exe);
    }

    private RegistryAutostartService Create(string? exe = null) =>
        new(exe ?? _exe, "JustInTimeTest", $@"{_root}\Run", $@"{_root}\Approved");

    [Fact]
    public void Is_off_by_default()
    {
        Assert.Equal(AutostartStatus.Off, Create().GetStatus());
    }

    [Fact]
    public void Enable_writes_a_quoted_command_with_the_background_flag()
    {
        var service = Create();

        service.SetEnabled(true);

        Assert.Equal(AutostartStatus.On, service.GetStatus());
        using var run = Registry.CurrentUser.OpenSubKey($@"{_root}\Run")!;
        Assert.Equal($"\"{_exe}\" --background", run.GetValue("JustInTimeTest"));
    }

    [Fact]
    public void Disable_removes_the_entry()
    {
        var service = Create();
        service.SetEnabled(true);

        service.SetEnabled(false);

        Assert.Equal(AutostartStatus.Off, service.GetStatus());
    }

    [Fact]
    public void Disabling_when_nothing_is_set_is_harmless()
    {
        Create().SetEnabled(false);
    }

    [Fact]
    public void Entry_pointing_to_another_copy_counts_as_off_and_is_replaced_on_enable()
    {
        var other = Path.Combine(Path.GetTempPath(), $"jit-other-{Guid.NewGuid():N}.exe");
        File.WriteAllText(other, string.Empty);

        try
        {
            Create(other).SetEnabled(true);
            var service = Create();

            Assert.Equal(AutostartStatus.Off, service.GetStatus());

            service.SetEnabled(true);

            Assert.Equal(AutostartStatus.On, service.GetStatus());
        }
        finally
        {
            File.Delete(other);
        }
    }

    [Fact]
    public void Entry_disabled_in_windows_settings_is_reported_and_enable_restores_it()
    {
        var service = Create();
        service.SetEnabled(true);
        using (var approved = Registry.CurrentUser.CreateSubKey($@"{_root}\Approved"))
        {
            approved.SetValue("JustInTimeTest", new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.Equal(AutostartStatus.DisabledInWindows, service.GetStatus());

        service.SetEnabled(true);

        Assert.Equal(AutostartStatus.On, service.GetStatus());
    }

    [Fact]
    public void Entry_marked_enabled_by_windows_stays_on()
    {
        var service = Create();
        service.SetEnabled(true);
        using (var approved = Registry.CurrentUser.CreateSubKey($@"{_root}\Approved"))
        {
            approved.SetValue("JustInTimeTest", new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }

        Assert.Equal(AutostartStatus.On, service.GetStatus());
    }

    [Fact]
    public void Enable_is_refused_for_dotnet_host_and_missing_files()
    {
        Assert.Throws<InvalidOperationException>(() => Create(@"C:\Program Files\dotnet\dotnet.exe").SetEnabled(true));
        Assert.Throws<InvalidOperationException>(() => Create(@"C:\no\such\JustInTime.exe").SetEnabled(true));
    }
}
