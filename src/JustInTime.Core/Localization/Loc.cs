using System.Globalization;

namespace JustInTime.Core.Localization;

/// <summary>
/// Тексты интерфейса на выбранном языке. Один общий язык на приложение: его переключает раздел Settings.
/// Если ключа нет в английской таблице, берётся русский текст; если нет и там, показывается сам ключ (тест этого не допускает).
/// </summary>
public static class Loc
{
    private static readonly CultureInfo RuCulture = new("ru-RU");
    private static readonly CultureInfo EnCulture = new("en-US");

    private static volatile AppLanguage _language = AppLanguage.Ru;

    public static AppLanguage Language => _language;

    public static CultureInfo Culture => CultureOf(_language);

    /// <summary>Вызывается после смены языка. Подписчики обновляют показанные тексты.</summary>
    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        LanguageChanged?.Invoke();
    }

    public static string T(string key) => Translate(_language, key);

    public static string T(string key, params object?[] args) => Translate(_language, key, args);

    public static CultureInfo CultureOf(AppLanguage language) => language == AppLanguage.En ? EnCulture : RuCulture;

    /// <summary>Перевод на заданный язык, не трогая общий язык приложения (для тестов и фоновой работы).</summary>
    public static string Translate(AppLanguage language, string key, params object?[] args)
    {
        var text = Lookup(language, key);
        return args.Length == 0 ? text : string.Format(CultureOf(language), text, args);
    }

    public static IReadOnlyDictionary<string, string> Table(AppLanguage language) =>
        language == AppLanguage.En ? Strings.En : Strings.Ru;

    private static string Lookup(AppLanguage language, string key)
    {
        if (Table(language).TryGetValue(key, out var text))
        {
            return text;
        }

        return Strings.Ru.TryGetValue(key, out var fallback) ? fallback : key;
    }
}
