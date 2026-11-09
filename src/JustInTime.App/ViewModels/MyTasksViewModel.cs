using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public enum TaskFilter
{
    Active,
    Finished,
    All
}

public sealed partial class MyTasksViewModel : ObservableObject, IInitializableViewModel
{
    private readonly ITaskService _tasks;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MyTasksViewModel> _logger;

    // Защита от наложения запросов: показываем только результат самого свежего.
    private int _loadVersion;

    public MyTasksViewModel(
        ITaskService tasks,
        INavigationService navigation,
        IDialogService dialogs,
        ILogger<MyTasksViewModel> logger)
    {
        _tasks = tasks;
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger;

        _selectedFilter = Filters[0];
    }

    public IReadOnlyList<Option<TaskFilter>> Filters { get; } =
    [
        new(TaskFilter.Active, "Активные"),
        new(TaskFilter.Finished, "Завершённые"),
        new(TaskFilter.All, "Все")
    ];

    public ObservableCollection<TaskItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private Option<TaskFilter>? _selectedFilter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public Task InitializeAsync() => RefreshAsync();

    partial void OnSelectedFilterChanged(Option<TaskFilter>? value) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = null;
        await LoadAsync();
    }

    [RelayCommand]
    private void CreateTask() => _navigation.Navigate(NavSection.CreateTask);

    [RelayCommand]
    private void Edit(TaskItemViewModel? item)
    {
        if (item is { CanEdit: true })
        {
            _navigation.NavigateToEditor(item.Id);
        }
    }

    [RelayCommand]
    private async Task CancelTaskAsync(TaskItemViewModel? item)
    {
        if (item is not { CanCancel: true })
        {
            return;
        }

        if (!_dialogs.Confirm("Отмена задачи", $"Отменить задачу «{item.Title}»?"))
        {
            return;
        }

        await RunAsync(() => _tasks.CancelAsync(item.Id), "отменить задачу");
    }

    [RelayCommand]
    private async Task RetryAsync(TaskItemViewModel? item)
    {
        if (item is not { CanRetry: true })
        {
            return;
        }

        if (item.RetryNeedsDuplicateWarning &&
            !_dialogs.Confirm(
                "Повтор задачи",
                $"Результат прошлой попытки задачи «{item.Title}» неизвестен: действие могло уже выполниться. " +
                "Повтор может создать дубль, например отправить сообщение второй раз.\n\nПовторить всё равно?"))
        {
            return;
        }

        await RunAsync(() => _tasks.RetryAsync(item.Id), "повторить задачу");
    }

    private async Task RunAsync(Func<Task<ServiceResult>> operation, string description)
    {
        try
        {
            var result = await operation();
            ErrorMessage = result.Succeeded
                ? (result.Warnings.Count > 0 ? result.WarningText : null)
                : result.ErrorText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось {Action}", description);
            ErrorMessage = $"Не удалось {description}: {ex.Message}";
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;

        try
        {
            var filter = SelectedFilter?.Value ?? TaskFilter.Active;

            IReadOnlyCollection<ScheduledTaskStatus>? statuses = filter switch
            {
                TaskFilter.Active => ScheduledTaskStatuses.Active,
                TaskFilter.Finished => ScheduledTaskStatuses.Finished,
                _ => null
            };

            var tasks = await _tasks.GetListAsync(statuses);

            if (version != _loadVersion)
            {
                return;
            }

            // Активные идут по времени запуска, завершённые и общий список: свежие сверху.
            IEnumerable<ScheduledTask> ordered = filter == TaskFilter.Active
                ? tasks
                : tasks.OrderByDescending(t => t.UpdatedAt);

            Items.Clear();
            foreach (var task in ordered)
            {
                Items.Add(new TaskItemViewModel(task));
            }

            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logger.LogError(ex, "Не удалось загрузить список задач");
            ErrorMessage = $"Не удалось загрузить задачи из PostgreSQL: {ex.Message}";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }
}
