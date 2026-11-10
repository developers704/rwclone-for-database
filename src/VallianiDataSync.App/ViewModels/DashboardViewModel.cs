using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Sync;

namespace VallianiDataSync.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ILocalDatabaseService _db;
    private readonly IRemoteApiClient _api;
    private readonly IProductRepository _products;
    private readonly ISyncEngine _engine;
    private readonly ISyncStateStore _state;
    private readonly ISettingsService _settings;
    private readonly IImageSyncService _images;
    private readonly IServiceProvider _services;

    [ObservableProperty] private string _databaseStatus = "Unknown";
    [ObservableProperty] private string _apiStatus = "Unknown";
    [ObservableProperty] private string _selectedStore = "-";
    [ObservableProperty] private string _lastSuccessfulSync = "-";
    [ObservableProperty] private string _nextScheduledSync = "-";
    [ObservableProperty] private int _totalProducts;
    [ObservableProperty] private int _recordsUploaded;
    [ObservableProperty] private int _recordsUpdated;
    [ObservableProperty] private int _failedRecords;
    [ObservableProperty] private int _pendingImages;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<string> RecentErrors { get; } = new();

    public DashboardViewModel(
        ILocalDatabaseService db,
        IRemoteApiClient api,
        IProductRepository products,
        ISyncEngine engine,
        ISyncStateStore state,
        ISettingsService settings,
        IImageSyncService images,
        IServiceProvider services)
    {
        _db = db;
        _api = api;
        _products = products;
        _engine = engine;
        _state = state;
        _settings = settings;
        _images = images;
        _services = services;
        _engine.ProgressChanged += (_, result) =>
        {
            App.Current.Dispatcher.Invoke(() => ApplyResult(result));
        };
    }

    public async Task RefreshAsync()
    {
        SelectedStore = _settings.Current.Sync.SelectedStoreCode ?? "(all)";
        var last = await _state.GetLastSuccessfulSyncAsync(_settings.Current.Sync.SelectedStoreCode);
        LastSuccessfulSync = last?.ToLocalTime().ToString("g") ?? "Never";

        var scheduler = _services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<SyncSchedulerHostedService>()
            .FirstOrDefault();
        NextScheduledSync = scheduler?.NextScheduledSyncLocal?.ToString("g") ?? "Not scheduled";

        try
        {
            DatabaseStatus = await _db.IsAvailableAsync() ? "Connected" : "Offline";
        }
        catch
        {
            DatabaseStatus = "Offline";
        }

        try
        {
            var (ok, msg) = await _api.ValidateAsync();
            ApiStatus = ok ? "Connected" : $"Error: {msg}";
        }
        catch (Exception ex)
        {
            ApiStatus = $"Error: {ex.Message}";
        }

        try
        {
            TotalProducts = await _products.CountProductsAsync(_settings.Current.Sync.SelectedStoreCode);
        }
        catch
        {
            TotalProducts = 0;
        }

        var images = await _images.GetSummaryAsync(_settings.Current.Sync.SelectedStoreCode);
        PendingImages = images.Pending + images.Missing;

        var logs = await _state.QueryLogsAsync(null, null, LogLevelKind.Error, 8);
        RecentErrors.Clear();
        foreach (var log in logs)
            RecentErrors.Add($"{log.TimestampUtc.ToLocalTime():g} — {log.Message}");

        if (_engine.LastResult is not null)
            ApplyResult(_engine.LastResult);
    }

    private void ApplyResult(SyncRunResult result)
    {
        RecordsUploaded = result.Uploaded;
        RecordsUpdated = result.Updated;
        FailedRecords = result.Failed;
        PendingImages = result.ImagesPending + result.ImagesMissing;
        StatusMessage = result.CompletedAtUtc is null ? "Sync in progress…" : "Sync finished";
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await RunSyncAsync(SyncMode.Incremental);
    }

    [RelayCommand]
    private async Task FullSyncAsync()
    {
        await RunSyncAsync(SyncMode.Full);
    }

    [RelayCommand]
    private async Task StopSyncAsync()
    {
        await _engine.StopAsync();
        StatusMessage = "Stop requested";
    }

    [RelayCommand]
    private void ViewLogs()
    {
        // Navigation handled via MainViewModel typically; expose status only.
        StatusMessage = "Open Logs from the left navigation.";
    }

    private async Task RunSyncAsync(SyncMode mode)
    {
        if (IsBusy || _engine.IsRunning)
        {
            StatusMessage = "Sync already running";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = mode == SyncMode.Full ? "Full sync started…" : "Incremental sync started…";
            var result = await _engine.RunAsync(mode, _settings.Current.Sync.Channels, _settings.Current.Sync.SelectedStoreCode);
            ApplyResult(result);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            RecentErrors.Insert(0, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
