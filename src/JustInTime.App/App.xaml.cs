using System.IO;
using System.Windows;
using System.Windows.Threading;
using JustInTime.App.Services;
using JustInTime.App.ViewModels;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Services;
using JustInTime.Infrastructure;
using JustInTime.Infrastructure.Database;
using JustInTime.Scheduler;
using JustInTime.SystemControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace JustInTime.App;

public partial class App : Application
{
    /// <summary>Ключ запуска из Планировщика заданий: выполнить наступившие задачи и закрыться без окна.</summary>
    public const string RunDueArgument = "--run-due";

    private IHost? _host;
    private SchedulerLoop? _loop;
    private bool _headless;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _headless = e.Args.Contains(RunDueArgument, StringComparer.OrdinalIgnoreCase);

        if (_headless)
        {
            // Окна нет, поэтому закрываем приложение сами, когда работа закончена.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        try
        {
            var configuration = ConfigurationFactory.Create();
            ConfigureLogging(configuration);

            Log.Information("Запуск JustInTime{Mode}", _headless ? " (режим --run-due, без окна)" : string.Empty);

            _host = BuildHost(configuration);
            await _host.StartAsync();

            var databaseReady = await InitializeDatabaseAsync(showDialog: !_headless);

            if (_headless)
            {
                await RunDueOnceAsync(databaseReady);
                return;
            }

            if (databaseReady)
            {
                _loop = _host.Services.GetRequiredService<SchedulerLoop>();
                _loop.Start();
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение не удалось запустить");

            if (_headless)
            {
                Shutdown(1);
                return;
            }

            MessageBox.Show(
                $"Приложение не удалось запустить.\n\n{ex.Message}",
                "JustInTime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Сначала останавливаем цикл: он может быть посреди действия, и его результат ещё нужно сохранить.
        _loop?.StopAsync().GetAwaiter().GetResult();

        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.Information("JustInTime остановлен");
        Log.CloseAndFlush();

        base.OnExit(e);
    }

    private static void ConfigureLogging(IConfiguration configuration)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JustInTime",
            "logs",
            "justintime-.log");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }

    private static IHost BuildHost(IConfiguration configuration)
    {
        return Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((_, builder) =>
            {
                builder.Sources.Clear();
                builder.AddConfiguration(configuration);
            })
            .UseSerilog(logger: Log.Logger, dispose: false)
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);

                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<IDashboardService, DashboardService>();
                services.AddSingleton<ITaskService, TaskService>();

                // Планировщик: запуски в Планировщике заданий Windows и сам движок выполнения.
                services.AddSingleton<IProcessRunner, ProcessRunner>();
                services.AddSingleton(new WindowsSchedulerOptions
                {
                    ExecutablePath = Environment.ProcessPath
                        ?? throw new InvalidOperationException("Не удалось определить путь к исполняемому файлу приложения.")
                });
                services.AddSingleton<ITaskRegistrar, WindowsTaskSchedulerRegistrar>();
                services.AddSingleton(new SchedulerEngineOptions());
                services.AddSingleton<IActionExecutor, OpenUrlExecutor>();
                services.AddSingleton<IActionExecutor, LaunchProgramExecutor>();
                services.AddSingleton<IRadioService, WindowsRadioService>();
                services.AddSingleton<IActionExecutor, SystemSettingExecutor>();
                services.AddSingleton<SchedulerEngine>();
                services.AddSingleton<INotificationService, ToastNotificationService>();
                services.AddSingleton<RunNotifier>();
                services.AddSingleton<IUserSettingsService, UserSettingsService>();
                services.AddSingleton<SchedulerLoop>();

                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<MyTasksViewModel>();
                services.AddTransient<TaskEditorViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private async Task<bool> InitializeDatabaseAsync(bool showDialog)
    {
        var options = _host!.Services.GetRequiredService<DatabaseOptions>();

        try
        {
            Log.Information("Подключение к PostgreSQL: {Connection}", options.Describe());
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            return true;
        }
        catch (Exception ex)
        {
            // В обычном режиме окно всё равно откроется: Dashboard покажет ту же ошибку, а не упадёт.
            Log.Error(ex, "Не удалось подготовить базу данных ({Connection})", options.Describe());

            if (showDialog)
            {
                MessageBox.Show(
                    $"Не удалось подключиться к PostgreSQL ({options.Describe()}).\n\n{ex.Message}\n\n" +
                    "Проверьте, что служба PostgreSQL запущена, пароль задан через user-secrets " +
                    "и миграции созданы. Подробности в README.",
                    "JustInTime",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }

    /// <summary>Режим --run-due: один прогон планировщика без окна, затем выход.</summary>
    private async Task RunDueOnceAsync(bool databaseReady)
    {
        if (!databaseReady)
        {
            Shutdown(1);
            return;
        }

        try
        {
            var report = await _host!.Services.GetRequiredService<SchedulerEngine>().RunDueAsync();
            SchedulerLoop.LogReport(_host.Services.GetRequiredService<ILogger<App>>(), report);
            await _host.Services.GetRequiredService<RunNotifier>().NotifyAsync(report);
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Прогон планировщика (--run-due) завершился ошибкой");
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необработанная ошибка в интерфейсе");

        if (_headless)
        {
            // Окон нет, диалог никто не увидит и процесс завис бы навсегда.
            e.Handled = true;
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            $"Произошла непредвиденная ошибка.\n\n{e.Exception.Message}",
            "JustInTime",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
