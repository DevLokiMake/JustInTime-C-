using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.ViewModels;

public sealed partial class DashboardViewModel(
    IDashboardService dashboard,
    INavigationService navigation,
    ILogger<DashboardViewModel> logger) : ObservableObject, IInitializableViewModel
{
    private const string NoNextTask = "Нет запланированных задач";

    public ObservableCollection<HistoryItemViewModel> RecentActions { get; } = [];

    public ObservableCollection<HistoryItemViewModel> RecentErrors { get; } = [];

    [ObservableProperty]
    private int _activeTasks;

    [ObservableProperty]
    private int _failedTasks;

    [ObservableProperty]
    private string _nextTaskTitle = NoNextTask;

    [ObservableProperty]
    private string _nextTaskTime = string.Empty;

    [ObservableProperty]
    private bool _isRecentEmpty = true;

    [ObservableProperty]
    private bool _isErrorsEmpty = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public Task InitializeAsync() => RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;

        try
        {
            var snapshot = await dashboard.GetSnapshotAsync();

            ActiveTasks = snapshot.ActiveTasks;
            FailedTasks = snapshot.FailedTasks;

            if (snapshot.NextTask is { } next)
            {
                NextTaskTitle = next.Title;
                NextTaskTime = next.ScheduledAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            }
            else
            {
                NextTaskTitle = NoNextTask;
                NextTaskTime = string.Empty;
            }

            Replace(RecentActions, snapshot.RecentExecutions);
            Replace(RecentErrors, snapshot.RecentFailures);
            IsRecentEmpty = RecentActions.Count == 0;
            IsErrorsEmpty = RecentErrors.Count == 0;

            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось загрузить данные Dashboard");
            ErrorMessage = $"Не удалось загрузить данные из PostgreSQL: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CreateTask() => navigation.Navigate(NavSection.CreateTask);

    private static void Replace(
        ObservableCollection<HistoryItemViewModel> target, IEnumerable<ExecutionHistory> source)
    {
        target.Clear();
        foreach (var entry in source)
        {
            target.Add(Map(entry));
        }
    }

    private static HistoryItemViewModel Map(ExecutionHistory entry)
    {
        var when = (entry.FinishedAt ?? entry.StartedAt).ToLocalTime().ToString("dd.MM HH:mm");

        var result = entry.Result switch
        {
            null => "выполняется",
            ExecutionResult.Success => "успешно",
            ExecutionResult.Failed => "ошибка",
            ExecutionResult.Unknown => "результат неизвестен",
            ExecutionResult.Skipped => "пропущено",
            _ => entry.Result.ToString()
        };

        return new HistoryItemViewModel(
            entry.ScheduledTask?.Title ?? "Без названия",
            $"{when} · {result}",
            entry.ErrorMessage);
    }
}
