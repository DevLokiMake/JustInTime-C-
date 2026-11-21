namespace JustInTime.Core.Localization;

public enum AppLanguage
{
    Ru,
    En
}

public static class AppLanguageExtensions
{
    /// <summary>Код для хранения в настройках.</summary>
    public static string ToCode(this AppLanguage language) => language == AppLanguage.En ? "en" : "ru";

    /// <summary>Читает код из настроек. Незнакомое или пустое значение даёт русский: так приложение вело себя всегда.</summary>
    public static AppLanguage FromCode(string? code) =>
        string.Equals(code?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? AppLanguage.En : AppLanguage.Ru;
}
