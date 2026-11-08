using System.ComponentModel;

namespace JustInTime.App.Services;

public interface INavigationService : INotifyPropertyChanged
{
    /// <summary>ViewModel текущей страницы. Окно подбирает к ней View через DataTemplate.</summary>
    object? CurrentViewModel { get; }

    NavSection? CurrentSection { get; }

    void Navigate(NavSection section);

    /// <summary>Открывает форму задачи в режиме редактирования.</summary>
    void NavigateToEditor(Guid taskId);
}
