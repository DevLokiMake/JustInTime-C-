using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<HistoryViewModel> _logger;

    // Защита от наложения запросов: показываем только результат самого свежего.
    private int _loadVersion;

    public HistoryViewModel(IExecutionHistoryRepository history, ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _logger = logger;
        _selectedFilter = Filters[0];
    }

    public IReadOnlyList<Option<HistoryFilter>> Filters { get; } =
    [
        new(HistoryFilter.All, "Все"),
        new(HistoryFilter.Problems, "Проблемы"),
        new(HistoryFilter.Success, "Успешные")
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
            Footer = entries.Count >= PageSize ? $"Показаны последние {PageSize} записей." : string.Empty;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                _logger.LogError(ex, "Не удалось загрузить журнал");
                ErrorMessage = $"Не удалось загрузить журнал из PostgreSQL: {ex.Message}";
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
