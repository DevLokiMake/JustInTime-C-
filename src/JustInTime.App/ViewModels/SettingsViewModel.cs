using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public sealed partial class SettingsViewModel(
    IUserSettingsService settings,
    ILogger<SettingsViewModel> logger) : ObservableObject, IInitializableViewModel
{
    [ObservableProperty]
    private bool _automationEnabled = true;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _notifyOnSuccess;

    [ObservableProperty]
    private string _graceMinutesText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsError;

    [ObservableProperty]
    private bool _isLoaded;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public async Task InitializeAsync()
    {
        try
        {
            var loaded = await settings.LoadAsync();

            AutomationEnabled = loaded.AutomationEnabled;
            NotificationsEnabled = loaded.NotificationsEnabled;
            NotifyOnSuccess = loaded.NotifyOnSuccess;
            GraceMinutesText = loaded.MissedGraceMinutes.ToString(CultureInfo.InvariantCulture);
            IsLoaded = true;
            Show(null, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось загрузить настройки");
            IsLoaded = false;
            Show($"Не удалось загрузить настройки из PostgreSQL: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!int.TryParse(GraceMinutesText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var grace))
        {
            Show("Допустимое опоздание нужно указать целым числом минут.", true);
            return;
        }

        try
        {
            var result = await settings.SaveAsync(
                new UserSettings(AutomationEnabled, NotificationsEnabled, NotifyOnSuccess, grace));

            Show(result.Succeeded ? "Настройки сохранены." : result.ErrorText, !result.Succeeded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось сохранить настройки");
            Show($"Не удалось сохранить настройки: {ex.Message}", true);
        }
    }

    private void Show(string? text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }
}
