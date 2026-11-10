using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DashboardViewModel _dashboard;
    private readonly DatabaseSettingsViewModel _database;
    private readonly ApiSettingsViewModel _api;
    private readonly StoreManagementViewModel _stores;
    private readonly DataMappingViewModel _mapping;
    private readonly SynchronizationSettingsViewModel _sync;
    private readonly ImageSyncViewModel _images;
    private readonly LogsViewModel _logs;
    private readonly DestinationsViewModel _destinations;
    private readonly SecuritySettingsViewModel _security;
    private readonly IAuthSession _session;
    private readonly ISettingsService _settings;
    private readonly IOtpAuthService _otp;

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isUnlocked;
    [ObservableProperty] private LoginViewModel? _login;

    public MainViewModel(
        DashboardViewModel dashboard,
        DatabaseSettingsViewModel database,
        ApiSettingsViewModel api,
        StoreManagementViewModel stores,
        DataMappingViewModel mapping,
        SynchronizationSettingsViewModel sync,
        ImageSyncViewModel images,
        LogsViewModel logs,
        DestinationsViewModel destinations,
        SecuritySettingsViewModel security,
        IAuthSession session,
        ISettingsService settings,
        IOtpAuthService otp)
    {
        _dashboard = dashboard;
        _database = database;
        _api = api;
        _stores = stores;
        _mapping = mapping;
        _sync = sync;
        _images = images;
        _logs = logs;
        _destinations = destinations;
        _security = security;
        _session = session;
        _settings = settings;
        _otp = otp;

        if (!_settings.Current.Security.OtpEnabled
            || string.IsNullOrWhiteSpace(_settings.Current.Security.Email1)
            || string.IsNullOrWhiteSpace(_settings.Current.Security.Email2))
        {
            _session.MarkAuthenticated();
            IsUnlocked = true;
            CurrentPage = _dashboard;
            _ = _dashboard.RefreshAsync();
            return;
        }

        IsUnlocked = false;
        Login = new LoginViewModel(_otp, _session, _settings, OnUnlocked);
        CurrentPage = Login;
    }

    private void OnUnlocked()
    {
        IsUnlocked = true;
        CurrentPage = _dashboard;
        _ = _dashboard.RefreshAsync();
    }

    [RelayCommand]
    private void LockApp()
    {
        _session.Lock();
        IsUnlocked = false;
        Login = new LoginViewModel(_otp, _session, _settings, OnUnlocked);
        CurrentPage = Login;
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        if (!IsUnlocked)
            return;

        CurrentPage = page switch
        {
            "Dashboard" => _dashboard,
            "Database" => _database,
            "Api" => _api,
            "Stores" => _stores,
            "Mapping" => _mapping,
            "Sync" => _sync,
            "Images" => _images,
            "Logs" => _logs,
            "Destinations" => _destinations,
            "Security" => _security,
            _ => _dashboard
        };

        if (CurrentPage is DashboardViewModel d)
            _ = d.RefreshAsync();
        if (CurrentPage is LogsViewModel l)
            _ = l.LoadAsync();
        if (CurrentPage is ImageSyncViewModel i)
            _ = i.RefreshAsync();
        if (CurrentPage is StoreManagementViewModel s)
            s.Load();
        if (CurrentPage is DataMappingViewModel m)
            m.Load();
        if (CurrentPage is SynchronizationSettingsViewModel sy)
            sy.Load();
        if (CurrentPage is DatabaseSettingsViewModel db)
            db.Load();
        if (CurrentPage is ApiSettingsViewModel a)
            a.Load();
        if (CurrentPage is DestinationsViewModel dest)
            dest.Load();
        if (CurrentPage is SecuritySettingsViewModel sec)
            sec.Load();
    }
}
