using System.Windows;
using System.Windows.Controls;
using JustInTime.App.ViewModels;

namespace JustInTime.App.Views;

public partial class TaskEditorView : UserControl
{
    public TaskEditorView()
    {
        InitializeComponent();
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
            ? files[0]
            : null;

    private void OnMessageDragOver(object sender, DragEventArgs e)
    {
        // Файлы обрабатываем сами; обычный текст пусть вставляет само поле.
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnMessageDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;

        if (DroppedFile(e) is not { } path)
        {
            if (DataContext is TaskEditorViewModel vm)
            {
                vm.RecognitionNote = "Перетащите один файл изображения.";
                vm.RecognitionIsError = true;
            }

            return;
        }

        if (DataContext is TaskEditorViewModel editor)
        {
            await editor.RecognizeFileAsync(path);
        }
    }
}
