using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Options;

namespace VallianiDataSync.Infrastructure.Data;

public sealed class DatabaseConnectionFactory
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly ILogger<DatabaseConnectionFactory> _logger;

    public DatabaseConnectionFactory(
        ISettingsService settings,
        ICredentialStore credentials,
        ILogger<DatabaseConnectionFactory> logger)
    {
        _settings = settings;
        _credentials = credentials;
        _logger = logger;
    }

    public DatabaseProviderKind Provider => _settings.Current.Database.Provider;

    public async Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var db = _settings.Current.Database;
        _logger.LogInformation("Opening {Provider} connection to {Host}/{Database}",
            db.Provider, db.Host, db.Database);

        DbConnection connection = db.Provider switch
        {
            DatabaseProviderKind.MySql => new MySqlConnection(BuildMySqlConnectionString(db)),
            _ => new SqlConnection(BuildSqlServerConnectionString(db))
        };

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private string BuildSqlServerConnectionString(DatabaseOptions db)
    {
        var builder = new SqlConnectionStringBuilder
        {
            // Local Windows-auth SQL can use shared memory even when TCP 1433 is disabled.
            // Remote hosts still use the configured TCP port.
            DataSource = db.UseWindowsAuthentication && IsLocalHost(db.Host)
                ? db.Host
                : db.Port > 0 ? $"{db.Host},{db.Port}" : db.Host,
            InitialCatalog = db.Database,
            ConnectTimeout = db.ConnectionTimeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = true
        };

        if (db.UseWindowsAuthentication)
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = db.Username;
            builder.Password = _credentials.GetSecret(CredentialKeys.SqlPassword) ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    private static bool IsLocalHost(string host)
        => string.Equals(host?.Trim(), "localhost", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host?.Trim(), ".", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host?.Trim(), "127.0.0.1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(host?.Trim(), "::1", StringComparison.OrdinalIgnoreCase);

    private string BuildMySqlConnectionString(DatabaseOptions db)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = db.Host,
            Port = (uint)(db.Port > 0 ? db.Port : 3306),
            Database = db.Database,
            UserID = db.Username,
            Password = _credentials.GetSecret(CredentialKeys.SqlPassword) ?? string.Empty,
            ConnectionTimeout = (uint)Math.Max(1, db.ConnectionTimeoutSeconds),
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true
        };
        return builder.ConnectionString;
    }
}
