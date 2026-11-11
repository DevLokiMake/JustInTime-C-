using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Tests;

public sealed class UserSettingsServiceTests
{
    private readonly FakeSettingsStore _store = new();

    private UserSettingsService CreateService() => new(_store);

    [Fact]
    public async Task Defaults_are_safe_when_nothing_is_stored()
    {
        var settings = await CreateService().LoadAsync();

        Assert.True(settings.AutomationEnabled);
        Assert.True(settings.NotificationsEnabled);
        Assert.False(settings.NotifyOnSuccess);
        Assert.Equal(SchedulerEngineOptions.DefaultMissedGraceMinutes, settings.MissedGraceMinutes);
    }

    [Fact]
    public async Task Saved_settings_are_loaded_back()
    {
        var service = CreateService();
        var saved = new UserSettings(false, false, true, 90);

        var result = await service.SaveAsync(saved);

        Assert.True(result.Succeeded);
        Assert.Equal(saved, await service.LoadAsync());
    }

    [Fact]
    public async Task Settings_use_the_same_keys_as_the_scheduler_engine()
    {
        await CreateService().SaveAsync(new UserSettings(false, true, false, 45));

        Assert.Equal("false", _store.Values[SettingKeys.AutomationEnabled]);
        Assert.Equal("45", _store.Values[SettingKeys.MissedGraceMinutes]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(SchedulerEngineOptions.MaxMissedGraceMinutes + 1)]
    public async Task Out_of_range_grace_is_rejected_and_nothing_is_saved(int minutes)
    {
        var result = await CreateService().SaveAsync(new UserSettings(false, false, true, minutes));

        Assert.False(result.Succeeded);
        Assert.Empty(_store.Values);
    }

    [Fact]
    public async Task Corrupt_stored_grace_is_clamped_on_load()
    {
        _store.Values[SettingKeys.MissedGraceMinutes] = "999999";

        Assert.Equal(SchedulerEngineOptions.MaxMissedGraceMinutes, (await CreateService().LoadAsync()).MissedGraceMinutes);
    }
}
