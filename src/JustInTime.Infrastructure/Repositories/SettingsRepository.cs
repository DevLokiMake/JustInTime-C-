using JustInTime.Core.Interfaces;
using JustInTime.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace JustInTime.Infrastructure.Repositories;

public sealed class SettingsRepository(IDbContextFactory<JustInTimeDbContext> contextFactory) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Settings
            .AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Один атомарный запрос: две одновременные записи одного ключа не дают ошибку дубликата.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO "Settings" ("Key", "Value")
             VALUES ({key}, {value})
             ON CONFLICT ("Key") DO UPDATE SET "Value" = EXCLUDED."Value"
             """,
            cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Settings
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
    }
}
