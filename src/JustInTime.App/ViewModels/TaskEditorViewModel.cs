using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public enum ScheduleMode
{
    AfterDelay,
    AtTime
}

/// <summary>Форма задачи. Один экран и для создания, и для редактирования.</summary>
public sealed partial class TaskEditorViewModel : ObservableObject, IInitializableViewModel
{
    private static readonly string[] DateFormats = ["d.M.yyyy", "dd.MM.yyyy"];
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];

    private readonly ITaskService _tasks;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _time;
    private readonly ILogger<TaskEditorViewModel> _logger;

    private Guid? _editingId;

    // Часовой пояс редактируемой задачи. Для новой задачи null, берётся пояс устройства.
    private string? _zoneId;

    public TaskEditorViewModel(
        ITaskService tasks,
        INavigationService navigation,
        TimeProvider time,
        IDialogService dialogs,
        ILogger<TaskEditorViewModel> logger)
    {
        _tasks = tasks;
        _dialogs = dialogs;
        _navigation = navigation;
        _time = time;
        _logger = logger;

        _selectedAction = ActionOptions[0];
        _selectedApp = AppOptions[0];
        _selectedSetting = SettingOptions[0];
        _selectedSwitch = SwitchOptions[0];
        _selectedMode = ModeOptions[0];

        var suggested = time.GetLocalNow().AddMinutes(30);
        _dateText = suggested.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        _timeText = suggested.ToString("HH:mm", CultureInfo.InvariantCulture);
        _timeZoneText = $"Часовой пояс: {time.LocalTimeZone.DisplayName}";
    }

    public IReadOnlyList<Option<ActionType>> ActionOptions { get; } =
    [
        new(ActionType.SendMessage, UiNames.Action(ActionType.SendMessage)),
        new(ActionType.ToggleSystemSetting, UiNames.Action(ActionType.ToggleSystemSetting)),
        new(ActionType.LaunchProgram, UiNames.Action(ActionType.LaunchProgram)),
        new(ActionType.OpenUrl, UiNames.Action(ActionType.OpenUrl))
    ];

    public IReadOnlyList<Option<MessengerApp>> AppOptions { get; } =
    [
        new(MessengerApp.WhatsApp, UiNames.Messenger(MessengerApp.WhatsApp)),
        new(MessengerApp.Telegram, UiNames.Messenger(MessengerApp.Telegram)),
        new(MessengerApp.TelegramDesktop, UiNames.Messenger(MessengerApp.TelegramDesktop))
    ];

    public IReadOnlyList<Option<SystemSettingKind>> SettingOptions { get; } =
    [
        new(SystemSettingKind.Bluetooth, UiNames.Setting(SystemSettingKind.Bluetooth)),
        new(SystemSettingKind.WiFi, UiNames.Setting(SystemSettingKind.WiFi)),
        new(SystemSettingKind.AirplaneMode, UiNames.Setting(SystemSettingKind.AirplaneMode)),
        new(SystemSettingKind.MobileData, UiNames.Setting(SystemSettingKind.MobileData))
    ];

    public IReadOnlyList<Option<bool>> SwitchOptions { get; } =
    [
        new(true, "Включить"),
        new(false, "Выключить")
    ];

    public IReadOnlyList<Option<ScheduleMode>> ModeOptions { get; } =
    [
        new(ScheduleMode.AfterDelay, "Через несколько минут"),
        new(ScheduleMode.AtTime, "В указанное время")
    ];

    public ObservableCollection<string> Errors { get; } = [];

    [ObservableProperty]
    private string _pageTitle = "Создать задачу";

    [ObservableProperty]
    private string _saveButtonText = "Создать";

    [ObservableProperty]
    private bool _canChangeAction = true;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isEditable = true;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMessageAction))]
    [NotifyPropertyChangedFor(nameof(IsSystemAction))]
    [NotifyPropertyChangedFor(nameof(IsProgramAction))]
    [NotifyPropertyChangedFor(nameof(IsUrlAction))]
    private Option<ActionType>? _selectedAction;

    // Отправка сообщения
    [ObservableProperty]
    private Option<MessengerApp>? _selectedApp;

    [ObservableProperty]
    private string _recipient = string.Empty;

    [ObservableProperty]
    private string _messageText = string.Empty;

    // Системная настройка
    [ObservableProperty]
    private Option<SystemSettingKind>? _selectedSetting;

    [ObservableProperty]
    private Option<bool>? _selectedSwitch;

    // Запуск программы
    [ObservableProperty]
    private string _programPath = string.Empty;

    [ObservableProperty]
    private string _programArguments = string.Empty;

    // Открытие ссылки
    [ObservableProperty]
    private string _url = string.Empty;

    // Время
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDelayMode))]
    [NotifyPropertyChangedFor(nameof(IsAtTimeMode))]
    private Option<ScheduleMode>? _selectedMode;

    [ObservableProperty]
    private string _delayMinutesText = "15";

    [ObservableProperty]
    private string _dateText;

    [ObservableProperty]
    private string _timeText;

    [ObservableProperty]
    private string _timeZoneText;

    public bool IsMessageAction => SelectedAction?.Value == ActionType.SendMessage;

    public bool IsSystemAction => SelectedAction?.Value == ActionType.ToggleSystemSetting;

    public bool IsProgramAction => SelectedAction?.Value == ActionType.LaunchProgram;

    public bool IsUrlAction => SelectedAction?.Value == ActionType.OpenUrl;

    public bool IsDelayMode => SelectedMode?.Value == ScheduleMode.AfterDelay;

    public bool IsAtTimeMode => SelectedMode?.Value == ScheduleMode.AtTime;

    /// <summary>Переводит форму в режим редактирования. Данные загрузит InitializeAsync.</summary>
    public void BeginEdit(Guid taskId)
    {
        _editingId = taskId;
        PageTitle = "Редактировать задачу";
        SaveButtonText = "Сохранить";
        CanChangeAction = false;
    }

    public Task InitializeAsync() =>
        _editingId is { } id ? LoadAsync(id) : Task.CompletedTask;

    private bool CanSaveNow => !IsSaving && IsEditable;

    [RelayCommand(CanExecute = nameof(CanSaveNow))]
    private async Task SaveAsync()
    {
        ClearErrors();

        var errors = new List<string>();

        if (!TryBuildInput(errors, out var input))
        {
            ShowErrors(errors);
            return;
        }

        IsSaving = true;

        try
        {
            var result = _editingId is { } id
                ? await _tasks.UpdateAsync(id, input)
                : (await _tasks.CreateAsync(input)).ToResult();

            if (result.Succeeded)
            {
                // Задача уже сохранена. Предупреждение (например, Планировщик Windows недоступен)
                // показываем до перехода, чтобы пользователь его не пропустил.
                if (result.Warnings.Count > 0)
                {
                    _dialogs.Warn("Задача сохранена", result.WarningText);
                }

                _navigation.Navigate(NavSection.MyTasks);
                return;
            }

            ShowErrors(result.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось сохранить задачу");
            ShowErrors([$"Не удалось сохранить задачу: {ex.Message}"]);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _navigation.Navigate(NavSection.MyTasks);

    private bool TryBuildInput(List<string> errors, out TaskInput input)
    {
        input = null!;

        var payload = BuildPayload();
        var schedule = BuildSchedule(errors);

        if (payload is null)
        {
            errors.Add("Выберите тип действия.");
        }

        if (errors.Count > 0 || payload is null || schedule is null)
        {
            return false;
        }

        input = new TaskInput(Title, payload, schedule);
        return true;
    }

    private TaskPayload? BuildPayload() => SelectedAction?.Value switch
    {
        ActionType.SendMessage => new SendMessagePayload(
            SelectedApp?.Value ?? MessengerApp.WhatsApp, Recipient, MessageText),
        ActionType.ToggleSystemSetting => new SystemSettingPayload(
            SelectedSetting?.Value ?? SystemSettingKind.Bluetooth, SelectedSwitch?.Value ?? true),
        ActionType.LaunchProgram => new LaunchProgramPayload(ProgramPath, ProgramArguments),
        ActionType.OpenUrl => new OpenUrlPayload(Url),
        _ => null
    };

    private TaskSchedule? BuildSchedule(List<string> errors)
    {
        if (SelectedMode?.Value == ScheduleMode.AfterDelay)
        {
            if (int.TryParse(DelayMinutesText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                return new AfterDelaySchedule(minutes);
            }

            errors.Add("Укажите целое число минут, например 15.");
            return null;
        }

        var dateOk = DateOnly.TryParseExact(
            DateText?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
        var timeOk = TimeOnly.TryParseExact(
            TimeText?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time);

        if (!dateOk)
        {
            errors.Add("Дату укажите в формате дд.мм.гггг, например 25.12.2026.");
        }

        if (!timeOk)
        {
            errors.Add("Время укажите в формате чч:мм, например 09:30.");
        }

        return dateOk && timeOk
            ? new AtLocalTimeSchedule(date.ToDateTime(time), _zoneId)
            : null;
    }

    private async Task LoadAsync(Guid id)
    {
        try
        {
            var task = await _tasks.GetAsync(id);

            if (task is null)
            {
                IsEditable = false;
                ShowErrors(["Задача не найдена. Возможно, она была удалена."]);
                return;
            }

            var payload = TaskPayloadSerializer.Deserialize(task.ActionType, task.Payload);

            if (payload is null)
            {
                IsEditable = false;
                ShowErrors(["Не удалось прочитать параметры задачи."]);
                return;
            }

            Title = task.Title;
            SelectedAction = ActionOptions.First(o => o.Value == task.ActionType);
            Fill(payload);
            FillTime(task);

            if (!ScheduledTaskStatuses.Editable.Contains(task.Status))
            {
                IsEditable = false;
                ShowErrors([$"Задачу в статусе «{task.Status.ToDisplayName()}» нельзя изменить."]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось загрузить задачу {TaskId}", id);
            IsEditable = false;
            ShowErrors([$"Не удалось загрузить задачу: {ex.Message}"]);
        }
    }

    private void Fill(TaskPayload payload)
    {
        switch (payload)
        {
            case SendMessagePayload message:
                SelectedApp = AppOptions.First(o => o.Value == message.App);
                Recipient = message.Recipient;
                MessageText = message.Text;
                break;
            case SystemSettingPayload setting:
                SelectedSetting = SettingOptions.First(o => o.Value == setting.Setting);
                SelectedSwitch = SwitchOptions.First(o => o.Value == setting.Enable);
                break;
            case LaunchProgramPayload program:
                ProgramPath = program.ProgramPath;
                ProgramArguments = program.Arguments ?? string.Empty;
                break;
            case OpenUrlPayload url:
                Url = url.Url;
                break;
        }
    }

    private void FillTime(ScheduledTask task)
    {
        var zone = _time.LocalTimeZone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(task.TimeZone);
            _zoneId = zone.Id;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Пояс из задачи недоступен на этом устройстве: показываем время в поясе устройства.
            _logger.LogWarning("Часовой пояс {Zone} недоступен, используется пояс устройства", task.TimeZone);
            _zoneId = null;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(task.ScheduledAt, DateTimeKind.Utc), zone);

        SelectedMode = ModeOptions.First(o => o.Value == ScheduleMode.AtTime);
        DateText = local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        TimeText = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        TimeZoneText = $"Часовой пояс: {zone.DisplayName}";
    }

    private void ShowErrors(IEnumerable<string> errors)
    {
        Errors.Clear();
        foreach (var error in errors)
        {
            Errors.Add(error);
        }

        HasErrors = Errors.Count > 0;
    }

    private void ClearErrors()
    {
        Errors.Clear();
        HasErrors = false;
    }
}
