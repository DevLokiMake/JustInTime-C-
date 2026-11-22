using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;
using JustInTime.Core.Localization;

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
        new(MessageFilter.Active, Loc.T("filter_msg_active")),
        new(MessageFilter.NeedsAttention, Loc.T("filter_msg_attention")),
        new(MessageFilter.Finished, Loc.T("filter_finished"))
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
            ? Loc.T("msgs_cancel_confirm_repeat", item.Title)
            : Loc.T("msgs_cancel_confirm", item.Title);

        if (_dialogs.Confirm(Loc.T("msgs_cancel_title"), text))
        {
            await RunAsync(() => _tasks.CancelAsync(item.Id), Loc.T("desc_cancel_message"));
        }
    }

    /// <summary>Только для Failed: сообщение точно не отправлено, повтор не создаст дубль.</summary>
    [RelayCommand]
    private async Task RetryAsync(MessageItemViewModel? item)
    {
        if (item is { CanRetry: true })
        {
            await RunAsync(() => _tasks.RetryAsync(item.Id), Loc.T("desc_retry_message"));
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
                Loc.T("msgs_sent_title"),
                Loc.T("msgs_sent_text", item.Recipient, item.Title) +
                (item.IsRepeating ? Loc.T("msgs_next_run_assigned") : string.Empty)))
        {
            return;
        }

        _logger.LogInformation("Пользователь подтвердил: сообщение по задаче {TaskId} отправлено", item.Id);
        await RunAsync(() => _tasks.ResolveUnknownAsync(item.Id, delivered: true), Loc.T("desc_confirm_sent"));
    }

    [RelayCommand]
    private async Task MarkNotSentAsync(MessageItemViewModel? item)
    {
        if (item is not { NeedsResolve: true })
        {
            return;
        }

        if (!_dialogs.Confirm(
                Loc.T("msgs_not_sent_title"),
                Loc.T("msgs_not_sent_text", item.Recipient, item.Title) +
                (item.IsRepeating ? Loc.T("msgs_next_run_assigned") : string.Empty)))
        {
            return;
        }

        _logger.LogInformation("Пользователь подтвердил: сообщение по задаче {TaskId} не отправлено", item.Id);
        await RunAsync(() => _tasks.ResolveUnknownAsync(item.Id, delivered: false), Loc.T("desc_confirm_not_sent"));
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
            ErrorMessage = Loc.T("op_failed", description, ex.Message);
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
            ErrorMessage = Loc.T("msgs_load_failed", ex.Message);
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
