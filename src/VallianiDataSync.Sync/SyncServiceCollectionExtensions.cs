using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Sync.Engine;
using VallianiDataSync.Sync.Images;

namespace VallianiDataSync.Sync;

public static class SyncServiceCollectionExtensions
{
    public static IServiceCollection AddVallianiSync(this IServiceCollection services)
    {
        services.AddSingleton<IImageSyncService, ImageSyncService>();
        services.AddSingleton<ISyncEngine, SyncEngine>();
        services.AddSingleton<IHostedService, SyncSchedulerHostedService>();
        return services;
    }
}

public sealed class SyncSchedulerHostedService : IHostedService, IDisposable
{
    private readonly ISyncEngine _engine;
    private readonly ISettingsService _settings;
    private readonly IDestinationSyncService _destinations;
    private readonly ILogger<SyncSchedulerHostedService> _logger;
    private Timer? _timer;
    private DateTime? _nextRunLocal;

    public DateTime? NextScheduledSyncLocal => _nextRunLocal;

    public SyncSchedulerHostedService(
        ISyncEngine engine,
        ISettingsService settings,
        IDestinationSyncService destinations,
        ILogger<SyncSchedulerHostedService> logger)
    {
        _engine = engine;
        _settings = settings;
        _destinations = destinations;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(async _ => await TickAsync().ConfigureAwait(false), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        RecalculateNext();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task TickAsync()
    {
        try
        {
            var sync = _settings.Current.Sync;
            if (sync.AutomaticEnabled && !sync.ManualOnly
                && _nextRunLocal is not null && DateTime.Now >= _nextRunLocal
                && !_engine.IsRunning)
            {
                _logger.LogInformation("Starting scheduled sync for store {Store}", sync.SelectedStoreCode);
                await _engine.RunAsync(sync.DefaultMode, sync.Channels, sync.SelectedStoreCode, CancellationToken.None).ConfigureAwait(false);
                RecalculateNext();
            }
            else if (!sync.AutomaticEnabled)
            {
                RecalculateNext();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled sync failed");
            RecalculateNext();
        }

        try
        {
            await _destinations.RunDueProjectsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled destination sync failed");
        }
    }

    private void RecalculateNext()
    {
        var sync = _settings.Current.Sync;
        if (!sync.AutomaticEnabled)
        {
            _nextRunLocal = null;
            return;
        }

        if (sync.UseDailySchedule && sync.DailyAtLocalTime is not null)
        {
            var today = DateTime.Today.Add(sync.DailyAtLocalTime.Value);
            _nextRunLocal = today > DateTime.Now ? today : today.AddDays(1);
            return;
        }

        var minutes = Math.Max(1, sync.IntervalMinutes);
        _nextRunLocal = DateTime.Now.AddMinutes(minutes);
    }
}
