using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.Core.Interfaces;
using JustInTime.App.Services;
using JustInTime.Core.Models;
using JustInTime.Core.Services;
using Microsoft.Extensions.Logging;
using JustInTime.Core.Localization;

namespace JustInTime.App.ViewModels;

public enum HistoryFilter
{
    All,
    Problems,
    Success
}

public sealed partial class HistoryViewModel : ObservableObject, IInitializableViewModel
{
    private const int PageSize = 200;

    private readonly IExecutionHistoryRepository _history;
    private readonly IMaintenanceService _maintenance;
    private readonly IDangerConfirmation _danger;
    private readonly ILogger<HistoryViewModel> _logger;

    // Защита от наложения запросов: показываем только результат самого свежего.
    private int _loadVersion;

    public HistoryViewModel(
        IExecutionHistoryRepository history,
        IMaintenanceService maintenance,
        IDangerConfirmation danger,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _maintenance = maintenance;
        _danger = danger;
        _logger = logger;
        _selectedFilter = Filters[0];
    }

    public IReadOnlyList<Option<HistoryFilter>> Filters { get; } =
    [
        new(HistoryFilter.All, Loc.T("filter_all")),
        new(HistoryFilter.Problems, Loc.T("filter_problems")),
        new(HistoryFilter.Success, Loc.T("filter_success"))
    ];

    public ObservableCollection<HistoryEntryViewModel> Items { get; } = [];

    [ObservableProperty]
    private Option<HistoryFilter>? _selectedFilter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _footer = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public Task InitializeAsync() => RefreshAsync();

    partial void OnSelectedFilterChanged(Option<HistoryFilter>? value) => _ = RefreshAsync();

    /// <summary>Очистка журнала необратима, поэтому всегда с подтверждением (кроме ключа --force / -y).</summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        try
        {
            var count = await _maintenance.CountFinishedHistoryAsync();

            if (count == 0)
            {
                ErrorMessage = Loc.T("hist_clear_none");
                return;
            }

            if (!_danger.Confirm(DangerAssessor.ForClearHistory(count)))
            {
                return;
            }

            await _maintenance.ClearHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось очистить журнал");
            ErrorMessage = Loc.T("hist_load_failed", ex.Message);
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;

        try
        {
            var filter = SelectedFilter?.Value ?? HistoryFilter.All;

            var entries = filter switch
            {
                HistoryFilter.Problems => await _history.GetRecentByResultAsync(ExecutionResults.Problems, PageSize),
                HistoryFilter.Success => await _history.GetRecentByResultAsync([ExecutionResult.Success], PageSize),
                _ => await _history.GetRecentAsync(PageSize)
            };

            if (version != _loadVersion)
            {
                return;
            }

            Items.Clear();
            foreach (var entry in entries)
            {
                Items.Add(HistoryEntryViewModel.From(entry));
            }

            IsEmpty = Items.Count == 0;
            Footer = entries.Count >= PageSize ? Loc.T("hist_footer", PageSize) : string.Empty;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                _logger.LogError(ex, "Не удалось загрузить журнал");
                ErrorMessage = Loc.T("hist_load_failed", ex.Message);
            }
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
