using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.App.Services;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using Microsoft.Extensions.Logging;
using JustInTime.Core.Localization;

namespace JustInTime.App.ViewModels;

public sealed partial class DashboardViewModel(
    IDashboardService dashboard,
    INavigationService navigation,
    ILogger<DashboardViewModel> logger) : ObservableObject, IInitializableViewModel
{
    private static string NoNextTask => Loc.T("dash_no_next");

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
            ErrorMessage = Loc.T("dash_load_failed", ex.Message);
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
            null => Loc.T("res_running"),
            ExecutionResult.Success => Loc.T("res_success"),
            ExecutionResult.Failed => Loc.T("res_failed"),
            ExecutionResult.Unknown => Loc.T("res_unknown"),
            ExecutionResult.Skipped => Loc.T("res_skipped"),
            _ => entry.Result.ToString()
        };

        return new HistoryItemViewModel(
            entry.ScheduledTask?.Title ?? Loc.T("untitled"),
            $"{when} · {result}",
            entry.ErrorMessage);
    }
}
