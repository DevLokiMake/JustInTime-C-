using System.Windows;
using JustInTime.App.ViewModels;
using JustInTime.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace JustInTime.App.Services;

public sealed class OcrWindowService(IServiceProvider services) : IOcrWindowService
{
    private ImageToTextWindow? _window;

    public void Show(Action<string>? insertText = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window is null)
            {
                var window = new ImageToTextWindow(services.GetRequiredService<ImageToTextViewModel>());
                window.Closed += (_, _) => _window = null;

                if (Application.Current.MainWindow is { IsVisible: true } owner)
                {
                    window.Owner = owner;
                }

                _window = window;
            }

            _window.ViewModel.InsertCallback = insertText;

            if (!_window.IsVisible)
            {
                _window.Show();
            }

            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
        });
    }
}
