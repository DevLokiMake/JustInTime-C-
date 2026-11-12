using System.IO;

namespace JustInTime.App.Services;

/// <summary>Куда сохраняются снимки окон. Только локальная папка пользователя.</summary>
public static class UiDumpPaths
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JustInTime",
        "ui-dumps");
}
