using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using JustInTime.Core.Localization;

namespace JustInTime.App.Localization;

/// <summary>Источник строк для привязок в XAML: при смене языка все привязки обновляются сами.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    private LocSource()
    {
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public static LocSource Instance { get; } = new();

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Raise();
        }
        else
        {
            dispatcher.BeginInvoke(Raise);
        }
    }
}
