using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using HandyControl.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VallianiDataSync.App.ViewModels;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Infrastructure;
using VallianiDataSync.Sync;

namespace VallianiDataSync.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // HandyControl defaults to Chinese — force English app-wide
        var en = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = en;
        CultureInfo.DefaultThreadCurrentUICulture = en;
        Thread.CurrentThread.CurrentCulture = en;
        Thread.CurrentThread.CurrentUICulture = en;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(en.IetfLanguageTag)));
        try { ConfigHelper.Instance.SetLang("en"); }
        catch { /* older HC builds */ }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash("Dispatcher", args.Exception);
            MessageBox.Show(
                "Valliani Data Sync crashed:\n\n" + args.Exception.Message +
                "\n\nDetails saved to Desktop\\VallianiDataSync-crash.txt",
                "Valliani Data Sync",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrash("AppDomain", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash("Task", args.Exception);
            args.SetObserved();
        };

        try
        {
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
        catch (Exception ex)
        {
            WriteCrash("Startup", ex);
            MessageBox.Show(
                "App failed to start:\n\n" + ex.Message +
                "\n\nDetails: Desktop\\VallianiDataSync-crash.txt\n\n" +
                "Tip: run Install.bat from the extracted folder (not from inside the ZIP).",
                "Valliani Data Sync",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteCrash(string kind, Exception ex)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, "VallianiDataSync-crash.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:O}] {kind}");
            sb.AppendLine(ex.ToString());
            sb.AppendLine();
            File.AppendAllText(path, sb.ToString());
        }
        catch
        {
            // ignore
        }

        try { Log.Fatal(ex, "Unhandled {Kind}", kind); }
        catch { /* ignore */ }
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
