using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using JustInTime.Core.Services;
using Microsoft.Win32;
using JustInTime.Core.Localization;

namespace JustInTime.App.Services;

public sealed class ImageInputService : IImageInputService
{
    public ImageInputResult PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("img_dialog_title"),
            Filter = Loc.T("img_dialog_filter") + "|" + string.Join(";", ImageFileRules.Extensions.Select(e => "*" + e)),
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? ReadFile(dialog.FileName) : ImageInputResult.Cancel();
    }

    public ImageInputResult ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                return ImageInputResult.Fail(Loc.T("img_err_file_missing"));
            }

            var error = ImageFileRules.Validate(path, info.Length);

            return error is null
                ? new ImageInputResult(File.ReadAllBytes(path))
                : ImageInputResult.Fail(error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ImageInputResult.Fail(Loc.T("img_err_read_file", ex.Message));
        }
    }

    public string? CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return null;
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            return Loc.T("img_err_clipboard_busy", ex.Message);
        }
    }

    public ImageInputResult FromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                return ImageInputResult.Fail(
                    Loc.T("img_err_clipboard_empty"));
            }

            var source = Clipboard.GetImage();

            if (source is null)
            {
                return ImageInputResult.Fail(Loc.T("img_err_clipboard_read"));
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return new ImageInputResult(stream.ToArray());
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // Буфер обмена мог быть занят другой программой.
            return ImageInputResult.Fail(Loc.T("img_err_clipboard_busy", ex.Message));
        }
    }
}
