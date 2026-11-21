using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public sealed partial class SettingsViewModel(
    IUserSettingsService settings,
    IAutostartService autostart,
    IAppLifecycle lifecycle,
    ILogger<SettingsViewModel> logger) : ObservableObject, IInitializableViewModel
{
    public IReadOnlyList<Option<AppLanguage>> LanguageOptions { get; } =
    [
        new(AppLanguage.Ru, "Русский"),
        new(AppLanguage.En, "English")
    ];

    [ObservableProperty]
    private Option<AppLanguage>? _selectedLanguage;

    [ObservableProperty]
    private bool _automationEnabled = true;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private bool _notifyOnSuccess;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _autostartEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutostartNote))]
    private string? _autostartNote;

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

    public bool HasAutostartNote => !string.IsNullOrEmpty(AutostartNote);

    public async Task InitializeAsync()
    {
        try
        {
            var loaded = await settings.LoadAsync();

            AutomationEnabled = loaded.AutomationEnabled;
            NotificationsEnabled = loaded.NotificationsEnabled;
            NotifyOnSuccess = loaded.NotifyOnSuccess;
            MinimizeToTray = loaded.MinimizeToTray;
            SelectedLanguage = LanguageOptions.First(o => o.Value == loaded.Language);
            LoadAutostart();
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
                new UserSettings(
                    AutomationEnabled, NotificationsEnabled, NotifyOnSuccess, grace, MinimizeToTray,
                    SelectedLanguage?.Value ?? AppLanguage.Ru));

            if (!result.Succeeded)
            {
                Show(result.ErrorText, true);
                return;
            }

            lifecycle.MinimizeToTray = MinimizeToTray;

            // Язык меняется сразу: привязки в XAML обновятся сами, остальные страницы создаются заново при переходе.
            Loc.SetLanguage(SelectedLanguage?.Value ?? AppLanguage.Ru);

            var autostartError = ApplyAutostart();
            Show(autostartError ?? "Настройки сохранены.", autostartError is not null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось сохранить настройки");
            Show($"Не удалось сохранить настройки: {ex.Message}", true);
        }
    }

    private void LoadAutostart()
    {
        var status = autostart.GetStatus();
        AutostartEnabled = status == AutostartStatus.On;
        AutostartNote = status == AutostartStatus.DisabledInWindows
            ? "Автозапуск отключён в Параметрах Windows (Приложения, Автозагрузка). Если включить его здесь и сохранить, запись снова станет активной."
            : null;
    }

    /// <summary>Автозапуск хранится в реестре Windows, а не в базе. Возвращает текст ошибки или null.</summary>
    private string? ApplyAutostart()
    {
        var current = autostart.GetStatus();
        var wanted = AutostartEnabled;

        if (wanted == (current == AutostartStatus.On))
        {
            return null;
        }

        try
        {
            autostart.SetEnabled(wanted);
            LoadAutostart();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Не удалось изменить автозапуск");
            LoadAutostart();
            return $"Остальные настройки сохранены, но автозапуск не изменён: {ex.Message}";
        }
    }

    private void Show(string? text, bool isError)
    {
        Message = text;
        MessageIsError = isError;
    }
}
