using System.Windows;
using System.Windows.Input;
using JustInTime.App.ViewModels;

namespace JustInTime.App.Views;

public partial class ImageToTextWindow : Window
{
    public ImageToTextWindow(ImageToTextViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += Close;
    }

    public ImageToTextViewModel ViewModel { get; }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.CloseRequested -= Close;
        base.OnClosed(e);
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        // Файлы обрабатываем сами; обычный текст пусть вставляет поле результата.
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files)
        {
            await ViewModel.RecognizeFileAsync(files[0]);
        }
        else
        {
            ViewModel.Status = "Перетащите один файл изображения.";
            ViewModel.StatusIsError = true;
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V с картинкой в буфере распознаёт её. С обычным текстом в буфере вставка идёт как всегда.
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        bool hasImage;

        try
        {
            hasImage = Clipboard.ContainsImage();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return;
        }

        if (hasImage)
        {
            e.Handled = true;
            await ViewModel.PasteImageCommand.ExecuteAsync(null);
        }
    }
}
