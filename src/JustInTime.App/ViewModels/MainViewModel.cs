using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JustInTime.App.Services;
using JustInTime.Core.Localization;

namespace JustInTime.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel(INavigationService navigation)
    {
        Navigation = navigation;

        _navItems = BuildNavItems();
        Loc.LanguageChanged += OnLanguageChanged;

        // Если раздел сменили из кода (например, кнопкой на Dashboard), выделение в меню следует за ним.
        navigation.PropertyChanged += OnNavigationPropertyChanged;

        SelectedItem = NavItems[0];
    }

    public INavigationService Navigation { get; }

    [ObservableProperty]
    private IReadOnlyList<NavigationItem> _navItems;

    private static IReadOnlyList<NavigationItem> BuildNavItems() =>
    [
        new(NavSection.Dashboard, Loc.T("nav_dashboard")),
        new(NavSection.MyTasks, Loc.T("nav_mytasks")),
        new(NavSection.CreateTask, Loc.T("nav_create")),
        new(NavSection.ScheduledMessages, Loc.T("nav_messages")),
        new(NavSection.Automation, Loc.T("nav_automation")),
        new(NavSection.History, Loc.T("nav_history")),
        new(NavSection.Settings, Loc.T("nav_settings"))
    ];

    private void OnLanguageChanged()
    {
        var section = SelectedItem?.Section ?? Navigation.CurrentSection ?? NavSection.Dashboard;
        NavItems = BuildNavItems();
        SelectedItem = NavItems.First(i => i.Section == section);
    }

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
