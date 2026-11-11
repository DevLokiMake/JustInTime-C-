using JustInTime.Core.Interfaces;

namespace JustInTime.Tests;

/// <summary>Настройки в памяти: тесты Core-логики не нуждаются в PostgreSQL.</summary>
internal sealed class FakeSettingsStore : ISettingsStore
{
    public Dictionary<string, string> Values { get; } = new();

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(Values);
}
