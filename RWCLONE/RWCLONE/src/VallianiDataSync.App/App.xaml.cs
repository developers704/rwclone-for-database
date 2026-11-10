using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VallianiDataSync.App.ViewModels;
using VallianiDataSync.App.Views;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Infrastructure;
using VallianiDataSync.Sync;

namespace VallianiDataSync.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddVallianiInfrastructure(AppContext.BaseDirectory);
                services.AddVallianiSync();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<DatabaseSettingsViewModel>();
                services.AddSingleton<ApiSettingsViewModel>();
                services.AddSingleton<StoreManagementViewModel>();
                services.AddSingleton<DataMappingViewModel>();
                services.AddSingleton<SynchronizationSettingsViewModel>();
                services.AddSingleton<ImageSyncViewModel>();
                services.AddSingleton<LogsViewModel>();
                services.AddSingleton<DestinationsViewModel>();
                services.AddSingleton<SecuritySettingsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        var settings = _host.Services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();
        await _host.Services.GetRequiredService<ISyncStateStore>().InitializeAsync();
        await _host.StartAsync();

        Log.Information("Valliani Data Sync starting");

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
