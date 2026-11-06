using JustInTime.Core.Models;

namespace JustInTime.Core.Interfaces;

public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
