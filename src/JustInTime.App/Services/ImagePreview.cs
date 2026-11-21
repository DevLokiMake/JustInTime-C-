using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JustInTime.App.Services;

public static class ImagePreview
{
    /// <summary>Картинка для показа в окне. null, если данные не удалось прочитать как изображение.</summary>
    public static ImageSource? Create(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // читаем сразу, чтобы поток можно было закрыть
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
