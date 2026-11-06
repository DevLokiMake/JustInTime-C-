using System.IO;
using System.Windows;
using System.Windows.Threading;
using JustInTime.App.Services;
using JustInTime.App.ViewModels;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Services;
using JustInTime.Infrastructure;
using JustInTime.Infrastructure.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace JustInTime.App;

public partial class App : Application
{
    private IHost? _host;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var configuration = ConfigurationFactory.Create();
            ConfigureLogging(configuration);

            Log.Information("Запуск JustInTime");

            _host = BuildHost(configuration);
            await _host.StartAsync();

            await InitializeDatabaseAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Приложение не удалось запустить");
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

                services.AddSingleton<IDashboardService, DashboardService>();

                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private async Task InitializeDatabaseAsync()
    {
        var options = _host!.Services.GetRequiredService<DatabaseOptions>();

        try
        {
            Log.Information("Подключение к PostgreSQL: {Connection}", options.Describe());
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        }
        catch (Exception ex)
        {
            // Окно всё равно откроется: Dashboard покажет ту же ошибку, а не упадёт.
            Log.Error(ex, "Не удалось подготовить базу данных ({Connection})", options.Describe());
            MessageBox.Show(
                $"Не удалось подключиться к PostgreSQL ({options.Describe()}).\n\n{ex.Message}\n\n" +
                "Проверьте, что служба PostgreSQL запущена, пароль задан через user-secrets " +
                "и миграции созданы. Подробности в README.",
                "JustInTime",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необработанная ошибка в интерфейсе");
        MessageBox.Show(
            $"Произошла непредвиденная ошибка.\n\n{e.Exception.Message}",
            "JustInTime",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
