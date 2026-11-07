using JustInTime.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace JustInTime.Tests;

/// <summary>
/// Общая тестовая база. Подключение берётся из тех же настроек и user-secrets, что и у приложения,
/// но имя базы подменяется на justintime_test. Рабочая база justintime_dev не затрагивается.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string TestDatabaseName = "justintime_test";

    public IDbContextFactory<JustInTimeDbContext> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configuration = ConfigurationFactory.Create();
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        options.Database = TestDatabaseName;

        var dbOptions = new DbContextOptionsBuilder<JustInTimeDbContext>()
            .UseNpgsql(options.BuildConnectionString())
            .Options;

        Factory = new TestDbContextFactory(dbOptions);

        await using var db = await Factory.CreateDbContextAsync();

        // Защита: удалять разрешено только базу с именем тестовой.
        var name = db.Database.GetDbConnection().Database;
        if (!string.Equals(name, TestDatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Тесты отказались работать с базой '{name}'.");
        }

        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Очищает все таблицы перед каждым тестом.</summary>
    public async Task ResetAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"ExecutionHistory\", \"Tasks\", \"Settings\" RESTART IDENTITY");
    }

    private sealed class TestDbContextFactory(DbContextOptions<JustInTimeDbContext> options)
        : IDbContextFactory<JustInTimeDbContext>
    {
        public JustInTimeDbContext CreateDbContext() => new(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
