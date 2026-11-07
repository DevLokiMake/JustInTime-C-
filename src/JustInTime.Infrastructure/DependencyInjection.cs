using JustInTime.Core.Interfaces;
using JustInTime.Infrastructure.Database;
using JustInTime.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JustInTime.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        var connectionString = options.BuildConnectionString();

        services.AddSingleton(options);

        // Фабрика контекстов, а не один общий DbContext: в WPF нет «запроса»,
        // поэтому каждая операция создаёт и закрывает свой контекст.
        services.AddDbContextFactory<JustInTimeDbContext>(o => o.UseNpgsql(connectionString));

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<IExecutionHistoryRepository, ExecutionHistoryRepository>();
        services.AddSingleton<ISettingsStore, SettingsRepository>();

        return services;
    }
}
