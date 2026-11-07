using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;
using JustInTime.Infrastructure.Repositories;

namespace JustInTime.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SettingsRepositoryTests(PostgresFixture db) : IAsyncLifetime
{
    private readonly SettingsRepository _settings = new(db.Factory);

    public Task InitializeAsync() => db.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Get_returns_null_for_missing_key()
    {
        Assert.Null(await _settings.GetAsync("нет-такого"));
    }

    [Fact]
    public async Task Set_then_Get_returns_value()
    {
        await _settings.SetAsync("Theme", "dark");

        Assert.Equal("dark", await _settings.GetAsync("Theme"));
    }

    [Fact]
    public async Task Set_overwrites_existing_value_without_creating_duplicate()
    {
        await _settings.SetAsync("Theme", "dark");
        await _settings.SetAsync("Theme", "light");

        Assert.Equal("light", await _settings.GetAsync("Theme"));
        Assert.Single(await _settings.GetAllAsync());
    }

    [Fact]
    public async Task Set_called_concurrently_for_same_key_does_not_fail()
    {
        var writes = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => _settings.SetAsync("Counter", i.ToString())));

        await Task.WhenAll(writes);

        Assert.Single(await _settings.GetAllAsync());
        Assert.NotNull(await _settings.GetAsync("Counter"));
    }

    [Fact]
    public async Task GetAll_returns_every_setting()
    {
        await _settings.SetAsync("A", "1");
        await _settings.SetAsync("B", "2");

        var all = await _settings.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("1", all["A"]);
        Assert.Equal("2", all["B"]);
    }

    [Fact]
    public async Task Set_rejects_blank_key()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _settings.SetAsync(" ", "x"));
    }

    [Fact]
    public async Task Bool_helpers_use_default_until_value_is_saved()
    {
        ISettingsStore store = _settings;

        Assert.True(await store.GetBoolAsync(SettingKeys.AutomationEnabled, defaultValue: true));

        await store.SetBoolAsync(SettingKeys.AutomationEnabled, false);

        Assert.False(await store.GetBoolAsync(SettingKeys.AutomationEnabled, defaultValue: true));
    }

    [Fact]
    public async Task Int_helpers_fall_back_to_default_for_garbage_value()
    {
        ISettingsStore store = _settings;

        await store.SetAsync("Retries", "не число");
        Assert.Equal(3, await store.GetIntAsync("Retries", defaultValue: 3));

        await store.SetIntAsync("Retries", 5);
        Assert.Equal(5, await store.GetIntAsync("Retries", defaultValue: 3));
    }
}
