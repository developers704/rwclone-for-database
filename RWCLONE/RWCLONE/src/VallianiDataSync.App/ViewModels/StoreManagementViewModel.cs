using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.App.ViewModels;

public partial class StoreManagementViewModel : ObservableObject
{
    private readonly IStoreRepository _stores;
    private readonly ISettingsService _settings;

    public ObservableCollection<StoreRecord> Stores { get; } = new();

    [ObservableProperty] private StoreRecord? _selectedStore;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public StoreManagementViewModel(IStoreRepository stores, ISettingsService settings)
    {
        _stores = stores;
        _settings = settings;
        Load();
    }

    public void Load()
    {
        Stores.Clear();
        foreach (var store in _settings.Current.Stores)
            Stores.Add(store);
    }

    [RelayCommand]
    private void AddStore()
    {
        var store = new StoreRecord { StoreCode = "STORE01", StoreName = "New Store", LocalStoreCode = "STORE01" };
        Stores.Add(store);
        SelectedStore = store;
    }

    [RelayCommand]
    private void RemoveStore()
    {
        if (SelectedStore is null) return;
        Stores.Remove(SelectedStore);
        SelectedStore = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _stores.SaveStoresAsync(Stores.ToList());
        StatusMessage = "Stores saved.";
    }
}
