using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.App.ViewModels;

public partial class ApiSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IRemoteApiClient _api;

    [ObservableProperty] private string _baseUrl = "https://localhost:7188";
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _storeId = string.Empty;
    [ObservableProperty] private string _clientId = string.Empty;
    [ObservableProperty] private int _timeoutSeconds = 60;
    [ObservableProperty] private int _retryCount = 3;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ApiSettingsViewModel(ISettingsService settings, ICredentialStore credentials, IRemoteApiClient api)
    {
        _settings = settings;
        _credentials = credentials;
        _api = api;
        Load();
    }

    public void Load()
    {
        var api = _settings.Current.Api;
        BaseUrl = api.BaseUrl;
        StoreId = api.StoreId ?? string.Empty;
        ClientId = api.ClientId ?? string.Empty;
        TimeoutSeconds = api.TimeoutSeconds;
        RetryCount = api.RetryCount;
        ApiKey = string.Empty;
        StatusMessage = _credentials.HasSecret(CredentialKeys.ApiKey) ? "API key on file (DPAPI)." : "No API key stored.";
    }

    [RelayCommand]
    private async Task TestApiAsync()
    {
        Persist();
        var (ok, message) = await _api.ValidateAsync();
        StatusMessage = ok ? message : "Failed: " + message;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Persist();
        await _settings.SaveAsync();
        StatusMessage = "API settings saved.";
    }

    private void Persist()
    {
        _settings.Update(s =>
        {
            s.Api.BaseUrl = BaseUrl.Trim();
            s.Api.StoreId = string.IsNullOrWhiteSpace(StoreId) ? null : StoreId.Trim();
            s.Api.ClientId = string.IsNullOrWhiteSpace(ClientId) ? null : ClientId.Trim();
            s.Api.TimeoutSeconds = TimeoutSeconds;
            s.Api.RetryCount = RetryCount;
            s.Api.RequireHttps = true;
        });
        if (!string.IsNullOrWhiteSpace(ApiKey))
            _credentials.SaveSecret(CredentialKeys.ApiKey, ApiKey.Trim());
    }
}
