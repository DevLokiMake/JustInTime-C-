using System.Windows;
using JustInTime.App.ViewModels;

namespace JustInTime.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
