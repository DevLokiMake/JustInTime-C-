using JustInTime.Core.Models;
using JustInTime.Core.Services;

namespace JustInTime.Core.Interfaces;

public interface IUserSettingsService
{
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Проверяет и сохраняет настройки. При ошибке проверки ничего не сохраняется.</summary>
    Task<ServiceResult> SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}
