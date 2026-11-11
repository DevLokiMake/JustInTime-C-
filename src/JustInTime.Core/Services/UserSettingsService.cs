using JustInTime.Core.Interfaces;
using JustInTime.Core.Models;

namespace JustInTime.Core.Services;

public sealed class UserSettingsService(ISettingsStore store) : IUserSettingsService
{
    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var grace = await store.GetIntAsync(
            SettingKeys.MissedGraceMinutes, SchedulerEngineOptions.DefaultMissedGraceMinutes, cancellationToken);

        return new UserSettings(
            await store.GetBoolAsync(SettingKeys.AutomationEnabled, true, cancellationToken),
            await store.GetBoolAsync(SettingKeys.NotificationsEnabled, true, cancellationToken),
            await store.GetBoolAsync(SettingKeys.NotifyOnSuccess, false, cancellationToken),
            // Так же, как в движке: испорченное значение в БД не должно показывать пользователю абсурд.
            Math.Clamp(grace, SchedulerEngineOptions.MinMissedGraceMinutes, SchedulerEngineOptions.MaxMissedGraceMinutes));
    }

    public async Task<ServiceResult> SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.MissedGraceMinutes is < SchedulerEngineOptions.MinMissedGraceMinutes
            or > SchedulerEngineOptions.MaxMissedGraceMinutes)
        {
            return ServiceResult.Fail(
                $"Допустимое опоздание должно быть от {SchedulerEngineOptions.MinMissedGraceMinutes} до " +
                $"{SchedulerEngineOptions.MaxMissedGraceMinutes} минут.");
        }

        await store.SetBoolAsync(SettingKeys.AutomationEnabled, settings.AutomationEnabled, cancellationToken);
        await store.SetBoolAsync(SettingKeys.NotificationsEnabled, settings.NotificationsEnabled, cancellationToken);
        await store.SetBoolAsync(SettingKeys.NotifyOnSuccess, settings.NotifyOnSuccess, cancellationToken);
        await store.SetIntAsync(SettingKeys.MissedGraceMinutes, settings.MissedGraceMinutes, cancellationToken);

        return ServiceResult.Ok();
    }
}
