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
            _ => new SqlConnection(BuildSqlServerConnectionString(db.Host, db.Port, db))
        };

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Tries several common local DataSource forms. On success updates Host/Port in settings.
    /// </summary>
    public async Task<(bool Ok, string Message)> ProbeSqlServerAsync(CancellationToken cancellationToken = default)
    {
        var db = _settings.Current.Database;
        if (string.IsNullOrWhiteSpace(db.Database))
            return (false, "Database name is empty. Type the exact name from SSMS (e.g. iTransactDB).");

        var host = (db.Host ?? "localhost").Trim();
        var candidates = new List<(string Host, int Port)>();

        void Add(string h, int p)
        {
            if (candidates.Any(c => c.Host.Equals(h, StringComparison.OrdinalIgnoreCase) && c.Port == p))
                return;
            candidates.Add((h, p));
        }

        Add(host, db.Port);
        Add(host, 0);
        if (!host.Contains('\\', StringComparison.Ordinal))
        {
            Add(host, 1433);
            Add($@".\SQLEXPRESS", 0);
            Add(@"localhost\SQLEXPRESS", 0);
            Add(@"(local)", 0);
            Add(@"(localdb)\MSSQLLocalDB", 0);
            Add(".", 0);
            Add("localhost", 0);
        }

        var errors = new List<string>();
        foreach (var (h, p) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cs = BuildSqlServerConnectionString(h, p, db);
            try
            {
                await using var conn = new SqlConnection(cs);
                await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DB_NAME(), SYSTEM_USER, @@SERVERNAME";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var dbName = "";
                var user = "";
                var server = "";
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    dbName = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    user = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    server = reader.IsDBNull(2) ? "" : reader.GetString(2);
                }

                _settings.Update(s =>
                {
                    s.Database.Host = h;
                    s.Database.Port = p;
                });

                return (true,
                    $"Connected OK. DataSource={Describe(h, p)} Server={server} Database={dbName} User={user}. Settings updated — click Save.");
            }
            catch (Exception ex)
            {
                errors.Add($"{Describe(h, p)} → {Truncate(ex.Message, 100)}");
            }
        }

        return (false,
            "All connection attempts failed. In SSMS copy the exact Server name and paste as Host. Tried:\n" +
            string.Join("\n", errors.Take(8)));
    }

    private static string Describe(string host, int port)
        => port > 0 && !host.Contains('\\', StringComparison.Ordinal) ? $"{host},{port}" : host;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";

    private string BuildSqlServerConnectionString(string host, int port, DatabaseOptions db)
    {
        host = (host ?? "localhost").Trim();
        var usePort = port > 0 && !host.Contains('\\', StringComparison.Ordinal);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = usePort ? $"{host},{port}" : host,
            InitialCatalog = db.Database,
            ConnectTimeout = Math.Clamp(db.ConnectionTimeoutSeconds, 5, 60),
            TrustServerCertificate = true,
            Encrypt = false
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

    private string BuildSqlServerConnectionString(DatabaseOptions db)
        => BuildSqlServerConnectionString(db.Host, db.Port, db);

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
