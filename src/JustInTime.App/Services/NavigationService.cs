using CommunityToolkit.Mvvm.ComponentModel;
using JustInTime.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JustInTime.App.Services;

public sealed partial class NavigationService(
    IServiceProvider services,
    ILogger<NavigationService> logger) : ObservableObject, INavigationService
{
    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private NavSection? _currentSection;

    public void Navigate(NavSection section)
    {
        // Сначала ViewModel, потом раздел: MainViewModel реагирует на смену раздела
        // вызовом Navigate, и эта проверка не даёт ему подменить только что открытую страницу.
        if (CurrentSection == section && CurrentViewModel is not null)
        {
            return;
        }

        object viewModel = section switch
        {
            NavSection.Dashboard => services.GetRequiredService<DashboardViewModel>(),
            NavSection.MyTasks => services.GetRequiredService<MyTasksViewModel>(),
            NavSection.CreateTask => services.GetRequiredService<TaskEditorViewModel>(),
            NavSection.ScheduledMessages => Placeholder("Scheduled Messages", "Запланированные сообщения в WhatsApp и Telegram."),
            NavSection.Automation => Placeholder("Automation", "Настройки и состояние автоматизации."),
            NavSection.History => Placeholder("History", "Журнал выполненных действий."),
            NavSection.Settings => Placeholder("Settings", "Настройки приложения."),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };

        logger.LogInformation("Переход в раздел {Section}", section);

        Show(viewModel, section);
    }

    public void NavigateToEditor(Guid taskId)
    {
        var editor = services.GetRequiredService<TaskEditorViewModel>();
        editor.BeginEdit(taskId);

        logger.LogInformation("Редактирование задачи {TaskId}", taskId);

        Show(editor, NavSection.CreateTask);
    }

    private void Show(object viewModel, NavSection section)
    {
        CurrentViewModel = viewModel;
        CurrentSection = section;

        if (viewModel is IInitializableViewModel initializable)
        {
            _ = initializable.InitializeAsync();
        }
    }

    private static PlaceholderViewModel Placeholder(string title, string description) =>
        new(title, description);
}
