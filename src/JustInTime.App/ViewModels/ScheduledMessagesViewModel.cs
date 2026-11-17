using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public enum MessageFilter
{
    Active,
    NeedsAttention,
    Finished
}

/// <summary>
/// Раздел «Scheduled Messages»: задачи отправки сообщений. Главное отличие от My Tasks: для задач с неизвестным
/// результатом пользователь проверяет чат и подтверждает, ушло ли сообщение, а не повторяет вслепую.
/// </summary>
public sealed partial class ScheduledMessagesViewModel : ObservableObject, IInitializableViewModel
{
    private readonly ITaskService _tasks;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ILogger<ScheduledMessagesViewModel> _logger;

    private int _loadVersion;

    public ScheduledMessagesViewModel(
        ITaskService tasks,
        INavigationService navigation,
        IDialogService dialogs,
        ILogger<ScheduledMessagesViewModel> logger)
    {
        _tasks = tasks;
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger;

        _selectedFilter = Filters[0];
    }

    public IReadOnlyList<Option<MessageFilter>> Filters { get; } =
    [
        new(MessageFilter.Active, "Запланированные"),
        new(MessageFilter.NeedsAttention, "Требуют проверки"),
        new(MessageFilter.Finished, "Завершённые")
    ];

    public ObservableCollection<MessageItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private Option<MessageFilter>? _selectedFilter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public Task InitializeAsync() => RefreshAsync();

    partial void OnSelectedFilterChanged(Option<MessageFilter>? value) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = null;
        await LoadAsync();
    }

    [RelayCommand]
    private void CreateMessage() => _navigation.Navigate(NavSection.CreateTask);

    [RelayCommand]
    private void Edit(MessageItemViewModel? item)
    {
        if (item is { CanEdit: true })
        {
            _navigation.NavigateToEditor(item.Id);
        }
    }

    [RelayCommand]
    private async Task CancelMessageAsync(MessageItemViewModel? item)
    {
        if (item is not { CanCancel: true })
        {
            return;
        }

        var text = item.IsRepeating
            ? $"Отменить «{item.Title}»? Повтор тоже будет отменён."
            : $"Отменить «{item.Title}»?";

        if (_dialogs.Confirm("Отмена сообщения", text))
        {
            await RunAsync(() => _tasks.CancelAsync(item.Id), "отменить сообщение");
        }
    }

    /// <summary>Только для Failed: сообщение точно не отправлено, повтор не создаст дубль.</summary>
    [RelayCommand]
    private async Task RetryAsync(MessageItemViewModel? item)
    {
        if (item is { CanRetry: true })
        {
            await RunAsync(() => _tasks.RetryAsync(item.Id), "повторить сообщение");
        }
    }

    [RelayCommand]
    private async Task MarkSentAsync(MessageItemViewModel? item)
    {
        if (item is not { NeedsResolve: true })
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Сообщение отправлено?",
                $"Вы проверили чат с «{item.Recipient}»: сообщение «{item.Title}» там есть?\n\n" +
                "Задача будет отмечена как выполненная." +
                (item.IsRepeating ? " Следующий запуск по повтору будет назначен." : string.Empty)))
        {
            return;
        }

        _logger.LogInformation("Пользователь подтвердил: сообщение по задаче {TaskId} отправлено", item.Id);
        await RunAsync(() => _tasks.ResolveUnknownAsync(item.Id, delivered: true), "подтвердить отправку");
    }

    [RelayCommand]
    private async Task MarkNotSentAsync(MessageItemViewModel? item)
    {
        if (item is not { NeedsResolve: true })
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Сообщение не отправлено?",
                $"Вы проверили чат с «{item.Recipient}»: сообщения «{item.Title}» там нет?\n\n" +
                "Задача будет отмечена как неудавшаяся, после этого её можно будет повторить." +
                (item.IsRepeating ? " Следующий запуск по повтору будет назначен." : string.Empty)))
        {
            return;
        }

        _logger.LogInformation("Пользователь подтвердил: сообщение по задаче {TaskId} не отправлено", item.Id);
        await RunAsync(() => _tasks.ResolveUnknownAsync(item.Id, delivered: false), "подтвердить, что сообщение не ушло");
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
            var filter = SelectedFilter?.Value ?? MessageFilter.Active;

            IReadOnlyCollection<ScheduledTaskStatus>? statuses = filter switch
            {
                MessageFilter.Active => ScheduledTaskStatuses.Active
                    .Where(s => s != ScheduledTaskStatus.NeedsUserAction).ToArray(),
                MessageFilter.NeedsAttention =>
                [
                    ScheduledTaskStatus.NeedsUserAction,
                    ScheduledTaskStatus.Failed
                ],
                _ => [ScheduledTaskStatus.Completed, ScheduledTaskStatus.Cancelled]
            };

            var all = await _tasks.GetListAsync(statuses);

            if (version != _loadVersion)
            {
                return;
            }

            var messages = all.Where(t => t.ActionType == ActionType.SendMessage);

            IEnumerable<ScheduledTask> ordered = filter == MessageFilter.Active
                ? messages.OrderBy(t => t.ScheduledAt)
                : messages.OrderByDescending(t => t.UpdatedAt);

            Items.Clear();

            foreach (var task in ordered)
            {
                Items.Add(new MessageItemViewModel(task));
            }

            IsEmpty = Items.Count == 0;
        }
        catch (Exception ex)
        {
            if (version != _loadVersion)
            {
                return;
            }

            _logger.LogError(ex, "Не удалось загрузить сообщения");
            ErrorMessage = $"Не удалось загрузить сообщения из PostgreSQL: {ex.Message}";
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
