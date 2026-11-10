using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.App.ViewModels;

public partial class ImageSyncViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IImageSyncService _images;
    private readonly IProductRepository _products;

    [ObservableProperty] private string _rootFoldersText = string.Empty;
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _pending;
    [ObservableProperty] private int _uploaded;
    [ObservableProperty] private int _failed;
    [ObservableProperty] private int _missing;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ImageSyncViewModel(ISettingsService settings, IImageSyncService images, IProductRepository products)
    {
        _settings = settings;
        _images = images;
        _products = products;
        RootFoldersText = string.Join(Environment.NewLine, _settings.Current.Images.RootFolders);
    }

    public async Task RefreshAsync()
    {
        var summary = await _images.GetSummaryAsync(_settings.Current.Sync.SelectedStoreCode);
        Total = summary.Total;
        Pending = summary.Pending;
        Uploaded = summary.Uploaded;
        Failed = summary.Failed;
        Missing = summary.Missing;
    }

    [RelayCommand]
    private async Task SaveRootsAsync()
    {
        var roots = RootFoldersText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _settings.Update(s => s.Images.RootFolders = roots);
        await _settings.SaveAsync();
        StatusMessage = "Image root folders saved.";
    }

    [RelayCommand]
    private async Task SyncImagesNowAsync()
    {
        try
        {
            StatusMessage = "Image sync started…";
            var products = await _products.GetProductsAsync(_settings.Current.Sync.SelectedStoreCode, null, 5000, 0);
            var summary = await _images.SyncImagesAsync(products);
            Total = summary.Total;
            Pending = summary.Pending;
            Uploaded = summary.Uploaded;
            Failed = summary.Failed;
            Missing = summary.Missing;
            StatusMessage = "Image sync finished.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
