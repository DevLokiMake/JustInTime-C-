using JustInTime.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JustInTime.Infrastructure.Database;

public sealed class DatabaseInitializer(
    IDbContextFactory<JustInTimeDbContext> contextFactory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        logger.LogInformation("Ожидающих миграций: {Count}", pending.Count);

        await db.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("База данных готова");
    }
}
