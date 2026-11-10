namespace VallianiDataSync.Core.Options;

public enum DatabaseProviderKind
{
    SqlServer = 0,
    MySql = 1
}

public sealed class DatabaseOptions
{
    public DatabaseProviderKind Provider { get; set; } = DatabaseProviderKind.SqlServer;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool UseWindowsAuthentication { get; set; }
    public int ConnectionTimeoutSeconds { get; set; } = 30;
}
