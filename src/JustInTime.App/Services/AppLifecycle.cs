using System.ComponentModel;
using System.Windows;
using JustInTime.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.Services;

public sealed class AppLifecycle(IServiceProvider services, ILogger<AppLifecycle> logger) : IAppLifecycle, IDisposable
{
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;
    private bool _trayHintShown;

    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Значок есть и закрытие окна не завершает приложение.</summary>
    private bool KeepRunningOnClose => MinimizeToTray && _tray is not null && !_exiting;

    /// <summary>Создаёт значок в трее, читает настройку и показывает окно (если не задан тихий старт).</summary>
    public async Task StartAsync(bool startHidden, bool databaseReady)
    {
        if (databaseReady)
        {
            try
            {
                MinimizeToTray = (await services.GetRequiredService<IUserSettingsService>().LoadAsync()).MinimizeToTray;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не удалось прочитать настройку трея, используется значение по умолчанию");
            }
        }

        try
        {
            _tray = new TrayIcon(ShowMainWindow, ShowCreateTask, ShowImageToText, ExitApplication);
        }
        catch (Exception ex)
        {
            // Без значка прятать окно нельзя: пользователь потерял бы приложение. Закрытие окна просто завершит его.
            logger.LogError(ex, "Не удалось создать значок в трее");
            _tray = null;
        }

        // Если база недоступна, окно показываем всегда: именно там видна причина.
        if (!startHidden || !databaseReady || _tray is null)
        {
            ShowMainWindow();
        }
    }

    public void ShowMainWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_exiting)
            {
                return;
            }

            if (_window is null)
            {
                _window = services.GetRequiredService<MainWindow>();
                _window.Closing += OnWindowClosing;
                Application.Current.MainWindow = _window;
            }

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
        });
    }

    public void ShowCreateTask()
    {
        ShowMainWindow();
        Application.Current.Dispatcher.Invoke(() => services.GetRequiredService<INavigationService>().Navigate(NavSection.CreateTask));
    }

    private void ShowImageToText() =>
        Application.Current.Dispatcher.Invoke(() => services.GetRequiredService<IOcrWindowService>().Show());

    public void ExitApplication()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            MarkExiting();
            Application.Current.Shutdown();
        });
    }

    /// <summary>Вызывается при завершении сеанса Windows, чтобы окно не мешало выключению компьютера.</summary>
    public void MarkExiting() => _exiting = true;

    public void Dispose()
    {
        _tray?.Dispose();
        _tray = null;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (KeepRunningOnClose)
        {
            e.Cancel = true;
            _window?.Hide();

            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray?.ShowBalloon("JustInTime работает", "Окно скрыто, задачи продолжат выполняться. Выход: правый клик по значку.");
            }

            return;
        }

        // Окно закрывается по-настоящему: завершаем приложение. Shutdown откладываем, нельзя вызывать его посреди закрытия окна.
        MarkExiting();
        Application.Current.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
    }
}
