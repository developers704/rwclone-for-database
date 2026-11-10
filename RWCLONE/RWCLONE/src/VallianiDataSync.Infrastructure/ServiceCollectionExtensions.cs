using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Infrastructure.Api;
using VallianiDataSync.Infrastructure.Configuration;
using VallianiDataSync.Infrastructure.Data;
using VallianiDataSync.Infrastructure.Security;
using VallianiDataSync.Infrastructure.State;

namespace VallianiDataSync.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <param name="contentRoot">App base (logs fallback).</param>
    /// <param name="dataRoot">Settings/secrets/state root. Null = LocalAppData desktop path.</param>
    /// <param name="scopedAuthSession">True for Blazor (per-circuit); false for WPF singleton.</param>
    public static IServiceCollection AddVallianiInfrastructure(
        this IServiceCollection services,
        string? contentRoot = null,
        string? dataRoot = null,
        bool scopedAuthSession = false)
    {
        var root = VallianiDataPaths.EnsureRoot(dataRoot);
        var logsDir = Path.Combine(root, "logs");
        Directory.CreateDirectory(logsDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(logsDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger, dispose: true);
        });

        services.AddSingleton(_ => root);
        services.AddSingleton<ICredentialStore>(_ => new DpapiCredentialStore(Path.Combine(root, "secrets")));
        services.AddSingleton<ISettingsService>(sp =>
            new JsonSettingsService(
                sp.GetRequiredService<ILogger<JsonSettingsService>>(),
                Path.Combine(root, "settings.json")));

        if (scopedAuthSession)
            services.AddScoped<IAuthSession, AuthSession>();
        else
            services.AddSingleton<IAuthSession, AuthSession>();

        services.AddSingleton<IOtpAuthService, OtpAuthService>();
        services.AddHttpClient(nameof(Destinations.DestinationSyncService), client =>
        {
            // Per batch push; full 2600-table sync can run many hours
            client.Timeout = TimeSpan.FromMinutes(15);
        });
        services.AddSingleton<IDestinationSyncService, Destinations.DestinationSyncService>();
        services.AddSingleton<ISyncStateStore>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var dir = Path.Combine(root, settings.Current.LocalStateDirectory);
            Directory.CreateDirectory(dir);
            return new SqliteSyncStateStore(dir);
        });

        services.AddSingleton<DatabaseConnectionFactory>();
        services.AddSingleton<ISourceCatalogService, SourceCatalogService>();
        services.AddSingleton<ILocalDatabaseService, LocalDatabaseService>();
        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<IInventoryRepository, InventoryRepository>();
        services.AddSingleton<IStoreRepository, JsonStoreRepository>();

        services.AddHttpClient<IRemoteApiClient, RemoteApiClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            client.Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.Current.Api.TimeoutSeconds));
        });

        return services;
    }
}
