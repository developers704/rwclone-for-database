using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Options;

namespace VallianiDataSync.App.ViewModels;

public partial class DatabaseSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly ILocalDatabaseService _db;

    public string[] ProviderChoices { get; } = ["MySQL", "SQL Server"];

    [ObservableProperty] private string _selectedProvider = "MySQL";
    [ObservableProperty] private string _host = "localhost";
    [ObservableProperty] private int _port = 3306;
    [ObservableProperty] private string _database = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _useWindowsAuthentication;
    [ObservableProperty] private int _connectionTimeout = 30;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isSqlServer;

    public DatabaseSettingsViewModel(ISettingsService settings, ICredentialStore credentials, ILocalDatabaseService db)
    {
        _settings = settings;
        _credentials = credentials;
        _db = db;
        Load();
    }

    partial void OnSelectedProviderChanged(string value)
    {
        IsSqlServer = string.Equals(value, "SQL Server", StringComparison.OrdinalIgnoreCase);
        if (!IsSqlServer && Port == 1433)
            Port = 3306;
        if (IsSqlServer && Port == 3306)
            Port = 1433;
        if (!IsSqlServer)
            UseWindowsAuthentication = false;
    }

    public void Load()
    {
        var db = _settings.Current.Database;
        SelectedProvider = db.Provider == DatabaseProviderKind.MySql ? "MySQL" : "SQL Server";
        IsSqlServer = db.Provider == DatabaseProviderKind.SqlServer;
        Host = db.Host;
        Port = db.Port > 0 ? db.Port : (IsSqlServer ? 1433 : 3306);
        Database = db.Database;
        Username = db.Username;
        UseWindowsAuthentication = db.UseWindowsAuthentication;
        ConnectionTimeout = db.ConnectionTimeoutSeconds;
        Password = string.Empty;
        StatusMessage = _credentials.HasSecret(CredentialKeys.SqlPassword) ? "Password on file (DPAPI)." : "No password stored.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        Persist();
        StatusMessage = "Testing SQL (tries localhost, .\\SQLEXPRESS, LocalDB, port 0/1433)…";
        var (ok, message) = await _db.TestConnectionAsync();
        if (ok)
        {
            // Probe may have updated Host/Port — refresh UI fields
            var db = _settings.Current.Database;
            Host = db.Host;
            Port = db.Port;
            StatusMessage = message;
            return;
        }
        StatusMessage = "Failed:\n" + message;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Persist();
        await _settings.SaveAsync();
        StatusMessage = UseWindowsAuthentication && IsSqlServer
            ? "Saved — Windows Auth ON (SQL username/password ignored)."
            : "Database settings saved. Click Test Connection.";
    }

    private void Persist()
    {
        if (Database.Equals("iTransectDB", StringComparison.OrdinalIgnoreCase))
            Database = "iTransactDB";

        _settings.Update(s =>
        {
            s.Database.Provider = IsSqlServer ? DatabaseProviderKind.SqlServer : DatabaseProviderKind.MySql;
            s.Database.Host = Host.Trim();
            s.Database.Port = Port;
            s.Database.Database = Database.Trim();
            s.Database.Username = Username.Trim();
            s.Database.UseWindowsAuthentication = IsSqlServer && UseWindowsAuthentication;
            s.Database.ConnectionTimeoutSeconds = ConnectionTimeout;
        });

        if (!(IsSqlServer && UseWindowsAuthentication) && !string.IsNullOrEmpty(Password))
            _credentials.SaveSecret(CredentialKeys.SqlPassword, Password);
    }
}
