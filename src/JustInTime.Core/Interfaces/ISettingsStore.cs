using System.Globalization;

namespace JustInTime.Core.Interfaces;

public interface ISettingsStore
{
    /// <summary>Значение настройки или null, если её ещё нет.</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Создаёт или перезаписывает настройку. Безопасно при одновременных вызовах.</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
}

public static class SettingsStoreExtensions
{
    public static async Task<bool> GetBoolAsync(
        this ISettingsStore store, string key, bool defaultValue, CancellationToken cancellationToken = default)
    {
        var raw = await store.GetAsync(key, cancellationToken);
        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    public static Task SetBoolAsync(
        this ISettingsStore store, string key, bool value, CancellationToken cancellationToken = default) =>
        store.SetAsync(key, value ? "true" : "false", cancellationToken);

    public static async Task<int> GetIntAsync(
        this ISettingsStore store, string key, int defaultValue, CancellationToken cancellationToken = default)
    {
        var raw = await store.GetAsync(key, cancellationToken);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    public static Task SetIntAsync(
        this ISettingsStore store, string key, int value, CancellationToken cancellationToken = default) =>
        store.SetAsync(key, value.ToString(CultureInfo.InvariantCulture), cancellationToken);
}
