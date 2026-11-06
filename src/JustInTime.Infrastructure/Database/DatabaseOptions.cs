using Npgsql;

namespace JustInTime.Infrastructure.Database;

/// <summary>
/// Настройки подключения к PostgreSQL. Всё, кроме пароля, лежит в appsettings.json.
/// Пароль берётся из user-secrets или переменной окружения JUSTINTIME_Database__Password
/// и никогда не попадает в Git.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Идентификатор хранилища user-secrets. Совпадает с UserSecretsId в JustInTime.App.csproj.</summary>
    public const string UserSecretsId = "justintime-8f3a2c1e-4b7d-4e6a-9c55-2a1d0f7b9e31";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "justintime_dev";

    public string Username { get; set; } = "postgres";

    public string? Password { get; set; }

    public string BuildConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Timeout = 5,
            CommandTimeout = 30
        };

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }

    /// <summary>Описание подключения без пароля, безопасно для логов.</summary>
    public string Describe() => $"{Username}@{Host}:{Port}/{Database}";
}
