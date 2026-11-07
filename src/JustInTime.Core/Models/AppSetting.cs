namespace JustInTime.Core.Models;

/// <summary>Пользовательская настройка в виде пары «ключ и значение». В БД: таблица "Settings".</summary>
public class AppSetting
{
    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
