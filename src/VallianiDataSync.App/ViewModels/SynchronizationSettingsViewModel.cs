using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.App.ViewModels;

public partial class SynchronizationSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;

    [ObservableProperty] private bool _manualOnly = true;
    [ObservableProperty] private bool _automaticEnabled;
    [ObservableProperty] private int _intervalMinutes = 15;
    [ObservableProperty] private bool _useDailySchedule;
    [ObservableProperty] private string _dailyAt = "02:00";
    [ObservableProperty] private bool _fullByDefault;
    [ObservableProperty] private bool _syncProducts = true;
    [ObservableProperty] private bool _syncInventory = true;
    [ObservableProperty] private bool _syncPrices = true;
    [ObservableProperty] private bool _syncImages = true;
    [ObservableProperty] private int _batchSize = 200;
    [ObservableProperty] private string _selectedStoreCode = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SynchronizationSettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        Load();
    }

    public void Load()
    {
        var sync = _settings.Current.Sync;
        ManualOnly = sync.ManualOnly;
        AutomaticEnabled = sync.AutomaticEnabled;
        IntervalMinutes = sync.IntervalMinutes;
        UseDailySchedule = sync.UseDailySchedule;
        DailyAt = sync.DailyAtLocalTime?.ToString(@"hh\:mm") ?? "02:00";
        FullByDefault = sync.DefaultMode == SyncMode.Full;
        SyncProducts = sync.Channels.HasFlag(SyncChannels.Products);
        SyncInventory = sync.Channels.HasFlag(SyncChannels.Inventory);
        SyncPrices = sync.Channels.HasFlag(SyncChannels.Prices);
        SyncImages = sync.Channels.HasFlag(SyncChannels.Images);
        BatchSize = sync.BatchSize;
        SelectedStoreCode = sync.SelectedStoreCode ?? string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        TimeSpan? daily = null;
        if (TimeSpan.TryParse(DailyAt, out var parsed))
            daily = parsed;

        var channels = SyncChannels.None;
        if (SyncProducts) channels |= SyncChannels.Products;
        if (SyncInventory) channels |= SyncChannels.Inventory;
        if (SyncPrices) channels |= SyncChannels.Prices;
        if (SyncImages) channels |= SyncChannels.Images;

        _settings.Update(s =>
        {
            s.Sync.ManualOnly = ManualOnly;
            s.Sync.AutomaticEnabled = AutomaticEnabled;
            s.Sync.IntervalMinutes = Math.Max(1, IntervalMinutes);
            s.Sync.UseDailySchedule = UseDailySchedule;
            s.Sync.DailyAtLocalTime = daily;
            s.Sync.DefaultMode = FullByDefault ? SyncMode.Full : SyncMode.Incremental;
            s.Sync.Channels = channels;
            s.Sync.BatchSize = Math.Clamp(BatchSize, 1, 2000);
            s.Sync.SelectedStoreCode = string.IsNullOrWhiteSpace(SelectedStoreCode) ? null : SelectedStoreCode.Trim();
        });
        await _settings.SaveAsync();
        StatusMessage = "Synchronization settings saved.";
    }
}
