using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustInTime.App.Services;

namespace JustInTime.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(INavigationService navigation)
    {
        Navigation = navigation;

        NavItems =
        [
            new(NavSection.Dashboard, "Dashboard"),
            new(NavSection.MyTasks, "My Tasks"),
            new(NavSection.CreateTask, "Create Task"),
            new(NavSection.ScheduledMessages, "Scheduled Messages"),
            new(NavSection.Automation, "Automation"),
            new(NavSection.History, "History"),
            new(NavSection.Settings, "Settings")
        ];

        // Если раздел сменили из кода (например, кнопкой на Dashboard), выделение в меню следует за ним.
        navigation.PropertyChanged += OnNavigationPropertyChanged;

        SelectedItem = NavItems[0];
    }

    public INavigationService Navigation { get; }

    public IReadOnlyList<NavigationItem> NavItems { get; }

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is not null)
        {
            Navigation.Navigate(value.Section);
        }
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(INavigationService.CurrentSection))
        {
            return;
        }

        var item = NavItems.FirstOrDefault(i => i.Section == Navigation.CurrentSection);
        if (item is not null && item != SelectedItem)
        {
            SelectedItem = item;
        }
    }
}
