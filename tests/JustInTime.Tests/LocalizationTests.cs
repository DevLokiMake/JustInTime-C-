using System.Text.RegularExpressions;
using JustInTime.Core.Localization;
using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

/// <summary>
/// Ни один из тестов не меняет общий язык приложения (Loc.SetLanguage): тесты идут параллельно, и остальные
/// проверяют русские тексты. Перевод проверяется функцией Translate с явным языком.
/// </summary>
public sealed partial class LocalizationTests
{
    [Fact]
    public void Russian_and_english_tables_have_exactly_the_same_keys()
    {
        var ru = Loc.Table(AppLanguage.Ru).Keys.ToHashSet();
        var en = Loc.Table(AppLanguage.En).Keys.ToHashSet();

        Assert.Empty(ru.Except(en));
        Assert.Empty(en.Except(ru));
    }

    [Fact]
    public void No_translation_is_empty()
    {
        foreach (var language in new[] { AppLanguage.Ru, AppLanguage.En })
        {
            var empty = Loc.Table(language).Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ToList();
            Assert.True(empty.Count == 0, $"{language}: пустые тексты: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Both_languages_use_the_same_format_placeholders()
    {
        var bad = new List<string>();

        foreach (var (key, ru) in Loc.Table(AppLanguage.Ru))
        {
            var en = Loc.Table(AppLanguage.En)[key];

            if (!Placeholders(ru).SetEquals(Placeholders(en)))
            {
                bad.Add(key);
            }
        }

        Assert.True(bad.Count == 0, "Разные параметры {0}, {1} в языках: " + string.Join(", ", bad));
    }

    [Fact]
    public void English_texts_do_not_contain_cyrillic_and_russian_ones_are_not_english_copies_of_nothing()
    {
        var cyrillic = new Regex("[А-Яа-яЁё]");
        var bad = Loc.Table(AppLanguage.En).Where(p => cyrillic.IsMatch(p.Value) && p.Key != "lang_ru").Select(p => p.Key).ToList();

        Assert.True(bad.Count == 0, "Русские буквы в английской таблице: " + string.Join(", ", bad));
    }

    [Fact]
    public void Translate_uses_the_requested_language_and_formats_arguments()
    {
        Assert.Equal("Время: 10:00", Loc.Translate(AppLanguage.Ru, "label_when", "10:00"));
        Assert.Equal("Time: 10:00", Loc.Translate(AppLanguage.En, "label_when", "10:00"));
    }

    [Fact]
    public void Unknown_key_falls_back_to_the_key_itself()
    {
        Assert.Equal("no_such_key", Loc.Translate(AppLanguage.En, "no_such_key"));
    }

    [Theory]
    [InlineData("ru", AppLanguage.Ru)]
    [InlineData("en", AppLanguage.En)]
    [InlineData("EN", AppLanguage.En)]
    [InlineData(" en ", AppLanguage.En)]
    [InlineData("de", AppLanguage.Ru)]
    [InlineData("", AppLanguage.Ru)]
    [InlineData(null, AppLanguage.Ru)]
    public void Language_code_is_read_safely(string? code, AppLanguage expected)
    {
        Assert.Equal(expected, AppLanguageExtensions.FromCode(code));
    }

    [Fact]
    public void Every_status_and_action_has_a_name_in_both_languages()
    {
        // Названия статусов и действий берутся из таблиц: проверяем, что для каждого значения ключ существует.
        var keys = Loc.Table(AppLanguage.En).Keys;

        foreach (var key in new[]
                 {
                     "status_pending", "status_scheduled", "status_running", "status_waiting_unlock",
                     "status_waiting_network", "status_needs_action", "status_completed", "status_failed",
                     "status_cancelled", "action_send_message", "action_system_setting", "action_launch_program",
                     "action_open_url"
                 })
        {
            Assert.Contains(key, keys);
        }

        foreach (var status in Enum.GetValues<ScheduledTaskStatus>())
        {
            Assert.NotEqual(status.ToString(), status.ToDisplayName());
        }
    }

    [Fact]
    public void Every_key_used_in_xaml_and_code_exists_in_the_tables()
    {
        var src = FindSourceRoot();
        var known = Loc.Table(AppLanguage.Ru).Keys.ToHashSet();
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
            {
                if (!known.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in CodeKey().Matches(File.ReadAllText(file)))
            {
                if (!known.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Ключи без текста: " + string.Join("; ", missing));
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");

            if (Directory.Exists(Path.Combine(candidate, "JustInTime.App")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Не найдена папка src репозитория: запускайте тесты из папки проекта.");
    }

    private static HashSet<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{loc:T ([A-Za-z0-9_]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex(@"Loc\.(?:T|Translate)\((?:AppLanguage\.\w+, )?""([a-z0-9_]+)""")]
    private static partial Regex CodeKey();
}
