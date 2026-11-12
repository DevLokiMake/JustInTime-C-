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
