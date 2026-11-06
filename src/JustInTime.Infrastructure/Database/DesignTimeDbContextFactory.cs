using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace JustInTime.Infrastructure.Database;

/// <summary>
/// Нужна только для команд dotnet ef (migrations add, database update).
/// Читает те же настройки, что и приложение, поэтому окно WPF для этого запускать не нужно.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JustInTimeDbContext>
{
    public JustInTimeDbContext CreateDbContext(string[] args)
    {
        var configuration = ConfigurationFactory.Create(includeCurrentDirectory: true);

        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        var optionsBuilder = new DbContextOptionsBuilder<JustInTimeDbContext>();
        optionsBuilder.UseNpgsql(options.BuildConnectionString());

        return new JustInTimeDbContext(optionsBuilder.Options);
    }
}
