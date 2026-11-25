using System.IO;
using System.Windows;
using System.Windows.Threading;
using JustInTime.App.Services;
using JustInTime.App.ViewModels;
using JustInTime.Automation;
using JustInTime.Automation.Session;
using JustInTime.Automation.Uia;
using JustInTime.Core.Cli;
using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Services;
using JustInTime.Infrastructure;
using JustInTime.Infrastructure.Database;
using JustInTime.Messaging;
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

    /// <summary>Ключ запуска: сохранить снимок дерева элементов окна и выйти. Пример: JustInTime.exe --dump-ui WhatsApp</summary>
    public const string DumpUiArgument = "--dump-ui";

    /// <summary>Ключ запуска (его пишет автозапуск): сразу в трей, без окна.</summary>
    public const string BackgroundArgument = RegistryAutostartService.BackgroundArgument;

    private IHost? _host;
    private SchedulerLoop? _loop;
    private bool _headless;
    private bool _force;
    private SingleInstanceGuard? _singleInstance;
    private AppLifecycle? _lifecycle;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dumpIndex = Array.FindIndex(e.Args, a => string.Equals(a, DumpUiArgument, StringComparison.OrdinalIgnoreCase));

        if (dumpIndex >= 0)
        {
            await DumpUiAndExitAsync(dumpIndex + 1 < e.Args.Length ? e.Args[dumpIndex + 1] : string.Empty);
            return;
        }

        _headless = e.Args.Contains(RunDueArgument, StringComparer.OrdinalIgnoreCase);

        var cli = CliParser.Parse(e.Args);
        _force = cli.Force;

        if (!_headless && cli.Command != CliCommand.None)
        {
            // Командный режим (--list, --cancel, --cancel-all, --clear-history): без окна, с кодом выхода.
            await RunCliAsync(cli);
            return;
        }

        if (!_headless)
        {
            // Приложение может жить без главного окна (в трее) или с одним окном «Фото в текст»: закрытие окон его не завершает.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        if (!_headless && !AcquireSingleInstance())
        {
            // Приложение уже запущено (возможно, скрыто в трее): показываем его окно и выходим.
            Shutdown(0);
            return;
        }

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

            _host = BuildHost(configuration, _force);
            await _host.StartAsync();

            var databaseReady = await InitializeDatabaseAsync(showDialog: !_headless);

            if (databaseReady)
            {
                await ApplyLanguageAsync();
            }

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

            _lifecycle = (AppLifecycle)_host.Services.GetRequiredService<IAppLifecycle>();
            _singleInstance?.StartListening(_lifecycle.ShowMainWindow);

            await _lifecycle.StartAsync(
                startHidden: e.Args.Contains(BackgroundArgument, StringComparer.OrdinalIgnoreCase),
                databaseReady: databaseReady);
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
                Loc.T("app_start_failed", ex.Message),
                "JustInTime",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Выключение или выход из Windows: окно не должно прятаться в трей и задерживать завершение.
        _lifecycle?.MarkExiting();
        base.OnSessionEnding(e);
    }

    /// <summary>Язык интерфейса из настроек. Нужен и окну, и фоновому запуску (тексты уведомлений).</summary>
    private async Task ApplyLanguageAsync()
    {
        try
        {
            var settings = await _host!.Services.GetRequiredService<IUserSettingsService>().LoadAsync();
            Loc.SetLanguage(settings.Language);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Не удалось прочитать язык интерфейса, используется русский");
        }
    }

    private bool AcquireSingleInstance()
    {
        _singleInstance = new SingleInstanceGuard();

        if (_singleInstance.TryAcquire())
        {
            return true;
        }

        _singleInstance.SignalExisting();
        _singleInstance.Dispose();
        _singleInstance = null;
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifecycle?.Dispose();

        // Сначала останавливаем цикл: он может быть посреди действия, и его результат ещё нужно сохранить.
        _loop?.StopAsync().GetAwaiter().GetResult();

        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        _singleInstance?.Dispose();

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

    private static IHost BuildHost(IConfiguration configuration, bool force)
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
                        ?? throw new InvalidOperationException(Loc.T("app_exe_path_unknown"))
                });
                services.AddSingleton<ITaskRegistrar, WindowsTaskSchedulerRegistrar>();
                services.AddSingleton(new SchedulerEngineOptions());
                services.AddSingleton<IActionExecutor, OpenUrlExecutor>();
                services.AddSingleton<IActionExecutor, LaunchProgramExecutor>();
                services.AddSingleton<IRadioService, WindowsRadioService>();
                services.AddSingleton<IActionExecutor, SystemSettingExecutor>();
                services.AddSingleton<IVolumeService, WindowsVolumeService>();
                services.AddSingleton<IActionExecutor, SetVolumeExecutor>();
                services.AddSingleton<INetworkStatus, SystemNetworkStatus>();
                services.AddSingleton<IWhatsAppProtocolLauncher, WhatsAppProtocolLauncher>();
                services.AddSingleton<IMessengerAdapter, WhatsAppDesktopAdapter>();
                services.AddSingleton<ITelegramLinkLauncher, TelegramLinkLauncher>();
                services.AddSingleton<IMessengerAdapter>(sp => new TelegramDesktopAdapter(
                    sp.GetRequiredService<IWindowFinder>(), sp.GetRequiredService<ITelegramLinkLauncher>(),
                    sp.GetRequiredService<TimeProvider>(), MessengerApp.Telegram));
                services.AddSingleton<IMessengerAdapter>(sp => new TelegramDesktopAdapter(
                    sp.GetRequiredService<IWindowFinder>(), sp.GetRequiredService<ITelegramLinkLauncher>(),
                    sp.GetRequiredService<TimeProvider>(), MessengerApp.TelegramDesktop));
                services.AddSingleton<IActionExecutor, MessageSendExecutor>();
                services.AddSingleton<IDesktopSession, DesktopSession>();
                services.AddSingleton<IWindowFinder, UiaWindowFinder>();
                services.AddSingleton(sp => new UiInspector(sp.GetRequiredService<IWindowFinder>(), UiDumpPaths.Directory));
                services.AddSingleton(sp => new ComposeProbe(sp.GetRequiredService<IWindowFinder>(), UiDumpPaths.Directory));
                services.AddSingleton<SchedulerEngine>();
                services.AddSingleton<INotificationService, ToastNotificationService>();
                services.AddSingleton<RunNotifier>();
                services.AddSingleton<IUserSettingsService, UserSettingsService>();
                services.AddSingleton<SchedulerLoop>();

                services.AddSingleton<IAutostartService>(_ => new RegistryAutostartService(
                    Environment.ProcessPath
                        ?? throw new InvalidOperationException(Loc.T("app_exe_path_unknown"))));
                services.AddSingleton<IAppLifecycle, AppLifecycle>();
                services.AddSingleton<ITextRecognizer, WindowsTextRecognizer>();
                services.AddSingleton<IImageInputService, ImageInputService>();
                services.AddSingleton<IOcrWindowService, OcrWindowService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton(new ConfirmationOptions(force));
                services.AddSingleton<IDangerConfirmation, DangerConfirmation>();
                services.AddSingleton<IMaintenanceService, MaintenanceService>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<MyTasksViewModel>();
                services.AddTransient<ScheduledMessagesViewModel>();
                services.AddTransient<TaskEditorViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<ImageToTextViewModel>();
                services.AddTransient<AutomationViewModel>();
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
                    Loc.T("app_db_failed", options.Describe(), ex.Message),
                    "JustInTime",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }

    /// <summary>Режим --dump-ui: база данных не нужна. Результат показываем окном сообщения, консоли у приложения нет.</summary>
    private async Task DumpUiAndExitAsync(string query)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var inspector = new UiInspector(new UiaWindowFinder(), UiDumpPaths.Directory);
            var result = await inspector.InspectAsync(query);

            var text = result.Success
                ? result.Message + "\n\n" + string.Join("\n", result.Files)
                : result.Message;

            MessageBox.Show(
                text, Loc.T("app_dump_title"), MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            Shutdown(result.Success ? 0 : 1);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("app_dump_failed", ex.Message), "JustInTime", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Командный режим без окна. Справка и ошибки разбора обходятся без базы. Остальные команды работают с задачами
    /// и историей, опасные спрашивают подтверждение в консоли, если не указан --force / -y.
    /// </summary>
    private async Task RunCliAsync(CliRequest cli)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var exitCode = CliExitCode.Error;
        using var console = new WindowsCliConsole();

        try
        {
            if (cli.Command is CliCommand.Help or CliCommand.Invalid)
            {
                exitCode = CliRunner.ShowUsage(cli, console);
            }
            else
            {
                var configuration = ConfigurationFactory.Create();
                ConfigureLogging(configuration);
                Log.Information("Командный режим: {Command}, force={Force}", cli.Command, cli.Force);

                _host = BuildHost(configuration, cli.Force);
                await _host.StartAsync();

                if (!await InitializeDatabaseAsync(showDialog: false))
                {
                    console.WriteLine(Loc.T("cli_db_not_ready"));
                }
                else
                {
                    await ApplyLanguageAsync();

                    var runner = new CliRunner(
                        _host.Services.GetRequiredService<ITaskService>(),
                        _host.Services.GetRequiredService<IMaintenanceService>(),
                        console);

                    exitCode = await runner.RunAsync(cli);
                    Log.Information("Командный режим завершён, код {Code}", exitCode);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Команда {Command} завершилась ошибкой", cli.Command);
            console.WriteLine(ex.Message);
        }

        Shutdown(exitCode);
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
            Loc.T("app_unexpected_error", e.Exception.Message),
            "JustInTime",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
