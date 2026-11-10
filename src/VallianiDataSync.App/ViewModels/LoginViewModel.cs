using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly IOtpAuthService _otp;
    private readonly IAuthSession _session;
    private readonly ISettingsService _settings;
    private readonly Action _onUnlocked;

    [ObservableProperty] private string _otpEmail1 = string.Empty;
    [ObservableProperty] private string _otpEmail2 = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _emailHint = string.Empty;
    [ObservableProperty] private string _otpLabel1 = "OTP from email 1";
    [ObservableProperty] private string _otpLabel2 = "OTP from email 2";
    [ObservableProperty] private bool _otpRequired;
    [ObservableProperty] private bool _showRecovery;

    public LoginViewModel(IOtpAuthService otp, IAuthSession session, ISettingsService settings, Action onUnlocked)
    {
        _otp = otp;
        _session = session;
        _settings = settings;
        _onUnlocked = onUnlocked;
        RefreshHint();
    }

    public void RefreshHint()
    {
        var s = _settings.Current.Security;
        OtpRequired = s.OtpEnabled
            && !string.IsNullOrWhiteSpace(s.Email1)
            && !string.IsNullOrWhiteSpace(s.Email2);

        OtpLabel1 = string.IsNullOrWhiteSpace(s.Email1)
            ? "OTP from email 1"
            : $"OTP from {s.Email1}";
        OtpLabel2 = string.IsNullOrWhiteSpace(s.Email2)
            ? "OTP from email 2"
            : $"OTP from {s.Email2}";

        if (!OtpRequired)
        {
            EmailHint = "Click Unlock to open the workspace. OTP is not required on this PC yet.";
            ShowRecovery = false;
        }
        else
        {
            EmailHint = $"1 = {s.Email1}   |   2 = {s.Email2}";
            ShowRecovery = true;
        }
    }

    [RelayCommand]
    private async Task SendOtpAsync()
    {
        if (!OtpRequired)
        {
            StatusMessage = "OTP is not enabled. Click Unlock.";
            return;
        }
        var (ok, message) = await _otp.SendLoginOtpsAsync();
        StatusMessage = message;
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        var sec = _settings.Current.Security;

        // Fresh PC / OTP off / incomplete OTP config → open without codes
        if (!OtpRequired)
        {
            EnterWorkspace();
            return;
        }

        var (ok, message) = await _otp.VerifyLoginOtpsAsync(OtpEmail1, OtpEmail2);
        StatusMessage = message;
        if (!ok)
            return;
        EnterWorkspace();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RecoverUnlockAsync()
    {
        var answer = MessageBox.Show(
            "This clears OTP lock and IP allow-list on THIS PC so you can open the app and re-configure Security.\n\nContinue?",
            "Recover access",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _settings.Update(s =>
        {
            s.Security.OtpEnabled = false;
            s.Security.AllowedIps.Clear();
        });
        await _settings.SaveAsync();
        OtpRequired = false;
        ShowRecovery = false;
        EmailHint = "OTP lock cleared on this PC. Click Unlock.";
        StatusMessage = "Recovery done. Click Unlock.";
        RefreshHint();
    }

    private void EnterWorkspace()
    {
        _session.MarkAuthenticated();
        _onUnlocked();
    }
}
