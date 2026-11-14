using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.Automation;
using JustInTime.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public sealed partial class AutomationViewModel(
    IDesktopSession desktop,
    UiInspector inspector,
    ComposeProbe probe,
    ILogger<AutomationViewModel> logger) : ObservableObject, IInitializableViewModel
{
    [ObservableProperty]
    private string _desktopStatus = string.Empty;

    [ObservableProperty]
    private bool _desktopAvailable;

    [ObservableProperty]
    private string _query = "WhatsApp";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SnapshotCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsError;

    [ObservableProperty]
    private string _probeWindow = "WhatsApp";

    [ObservableProperty]
    private string _probeComposerName = "Введите сообщение";

    [ObservableProperty]
    private string _probeComposerClass = string.Empty;

    [ObservableProperty]
    private string _probeText = "test123";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunProbeCommand))]
    private bool _isProbing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProbeReport))]
    private string? _probeReport;

    [ObservableProperty]
    private bool _probeFailed;

    public bool HasProbeReport => !string.IsNullOrEmpty(ProbeReport);

    public ObservableCollection<string> Files { get; } = [];

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public string OutputDirectory => inspector.OutputDirectory;

    public Task InitializeAsync()
    {
        RefreshDesktop();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RefreshDesktop()
    {
        try
        {
            DesktopAvailable = desktop.IsAvailable(out var reason);
            DesktopStatus = DesktopAvailable
                ? "Рабочий стол доступен: автоматизация программ возможна."
                : "Рабочий стол недоступен: " + reason;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось определить состояние рабочего стола");
            DesktopAvailable = false;
            DesktopStatus = "Не удалось определить состояние рабочего стола: " + ex.Message;
        }
    }

    [RelayCommand]
    private void UseQuery(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Query = text;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSnapshot))]
    private async Task SnapshotAsync()
    {
        IsBusy = true;
        Message = null;
        Files.Clear();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = await inspector.InspectAsync(Query, cts.Token);

            foreach (var file in result.Files)
            {
                Files.Add(file);
            }

            Message = result.Message;
            MessageIsError = !result.Success;

            logger.LogInformation(
                "Снимок окна «{Query}»: успех={Success}, файлов={Count}", Query, result.Success, result.Files.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Снимок окна «{Query}» завершился ошибкой", Query);
            Message = "Не удалось сделать снимок: " + ex.Message;
            MessageIsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSnapshot() => !IsBusy;

    [RelayCommand]
    private void UseProbePreset(string? name)
    {
        if (name == "Telegram")
        {
            ProbeWindow = "Telegram";
            ProbeComposerName = "Сообщение...";
            ProbeComposerClass = "class Ui::InputField::Inner";
        }
        else
        {
            ProbeWindow = "WhatsApp";
            ProbeComposerName = "Введите сообщение";
            ProbeComposerClass = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanProbe))]
    private async Task RunProbeAsync()
    {
        IsProbing = true;
        ProbeReport = null;

        try
        {
            var composer = new ElementQuery(
                NameContains: string.IsNullOrWhiteSpace(ProbeComposerName) ? null : ProbeComposerName.Trim(),
                ControlType: "Edit",
                ClassName: string.IsNullOrWhiteSpace(ProbeComposerClass) ? null : ProbeComposerClass.Trim());

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = await probe.RunAsync(new ProbeRequest(ProbeWindow, composer, ProbeText), cts.Token);

            ProbeFailed = !result.Success;
            ProbeReport = result.File is null
                ? result.Report
                : $"{(result.Success ? "Текст записан и прочитан обратно." : "Проверка не прошла.")} Отчёт: {result.File}";

            logger.LogInformation("Проверка ввода «{Window}»: успех={Success}", ProbeWindow, result.Success);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Проверка ввода «{Window}» завершилась ошибкой", ProbeWindow);
            ProbeFailed = true;
            ProbeReport = "Проверка не удалась: " + ex.Message;
        }
        finally
        {
            IsProbing = false;
        }
    }

    private bool CanProbe() => !IsProbing;

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OutputDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось открыть папку снимков");
            Message = "Не удалось открыть папку: " + ex.Message;
            MessageIsError = true;
        }
    }
}
