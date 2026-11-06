using Microsoft.Extensions.Configuration;

namespace JustInTime.Infrastructure.Database;

/// <summary>
/// Единое место сборки конфигурации: appsettings.json, user-secrets, переменные окружения.
/// Используется и приложением, и командой dotnet ef.
/// </summary>
public static class ConfigurationFactory
{
    public static IConfigurationRoot Create(bool includeCurrentDirectory = false)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        if (includeCurrentDirectory)
        {
            builder.AddJsonFile(
                Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
                optional: true,
                reloadOnChange: false);
        }

        return builder
            .AddUserSecrets(DatabaseOptions.UserSecretsId)
            .AddEnvironmentVariables(prefix: "JUSTINTIME_")
            .Build();
    }
}
