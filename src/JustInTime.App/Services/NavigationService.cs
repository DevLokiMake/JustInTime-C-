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
        if (CurrentSection == section && CurrentViewModel is not null)
        {
            return;
        }

        object viewModel = section switch
        {
            NavSection.Dashboard => services.GetRequiredService<DashboardViewModel>(),
            NavSection.MyTasks => Placeholder("My Tasks", "Список задач с редактированием и отменой."),
            NavSection.CreateTask => Placeholder("Create Task", "Форма создания задачи с полями под выбранный тип действия."),
            NavSection.ScheduledMessages => Placeholder("Scheduled Messages", "Запланированные сообщения в WhatsApp и Telegram."),
            NavSection.Automation => Placeholder("Automation", "Настройки и состояние автоматизации."),
            NavSection.History => Placeholder("History", "Журнал выполненных действий."),
            NavSection.Settings => Placeholder("Settings", "Настройки приложения."),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
        };

        logger.LogInformation("Переход в раздел {Section}", section);

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
