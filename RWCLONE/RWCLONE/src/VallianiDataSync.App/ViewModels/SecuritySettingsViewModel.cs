using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.App.ViewModels;

public partial class SecuritySettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IOtpAuthService _otp;

    [ObservableProperty] private bool _otpEnabled;
    [ObservableProperty] private string _email1 = string.Empty;
    [ObservableProperty] private string _email2 = string.Empty;
    [ObservableProperty] private string _allowedIpsText = string.Empty;

    [ObservableProperty] private string _smtp1Host = string.Empty;
    [ObservableProperty] private int _smtp1Port = 587;
    [ObservableProperty] private string _smtp1Username = string.Empty;
    [ObservableProperty] private string _smtp1Password = string.Empty;
    [ObservableProperty] private string _smtp1From = string.Empty;
    [ObservableProperty] private bool _smtp1UseSsl = true;

    [ObservableProperty] private string _smtp2Host = string.Empty;
    [ObservableProperty] private int _smtp2Port = 587;
    [ObservableProperty] private string _smtp2Username = string.Empty;
    [ObservableProperty] private string _smtp2Password = string.Empty;
    [ObservableProperty] private string _smtp2From = string.Empty;
    [ObservableProperty] private bool _smtp2UseSsl = true;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _sensitiveOtp1 = string.Empty;
    [ObservableProperty] private string _sensitiveOtp2 = string.Empty;

    public SecuritySettingsViewModel(ISettingsService settings, ICredentialStore credentials, IOtpAuthService otp)
    {
        _settings = settings;
        _credentials = credentials;
        _otp = otp;
        Load();
    }

    public void Load()
    {
        var s = _settings.Current.Security;
        s.MigrateLegacySmtpIfNeeded();

        OtpEnabled = s.OtpEnabled;
        Email1 = s.Email1;
        Email2 = s.Email2;
        AllowedIpsText = string.Join(Environment.NewLine, s.AllowedIps);

        Smtp1Host = s.Smtp1.Host;
        Smtp1Port = s.Smtp1.Port <= 0 ? 587 : s.Smtp1.Port;
        Smtp1Username = string.IsNullOrWhiteSpace(s.Smtp1.Username) ? s.Email1 : s.Smtp1.Username;
        Smtp1From = string.IsNullOrWhiteSpace(s.Smtp1.From) ? s.Email1 : s.Smtp1.From;
        Smtp1UseSsl = s.Smtp1.UseSsl;
        Smtp1Password = string.Empty;

        Smtp2Host = s.Smtp2.Host;
        Smtp2Port = s.Smtp2.Port <= 0 ? 587 : s.Smtp2.Port;
        Smtp2Username = string.IsNullOrWhiteSpace(s.Smtp2.Username) ? s.Email2 : s.Smtp2.Username;
        Smtp2From = string.IsNullOrWhiteSpace(s.Smtp2.From) ? s.Email2 : s.Smtp2.From;
        Smtp2UseSsl = s.Smtp2.UseSsl;
        Smtp2Password = string.Empty;

        var p1 = _credentials.HasSecret(CredentialKeys.SmtpPassword1) || _credentials.HasSecret(CredentialKeys.SmtpPassword);
        var p2 = _credentials.HasSecret(CredentialKeys.SmtpPassword2);
        StatusMessage = (p1 || p2)
            ? $"Email passwords on file — Email1: {(p1 ? "yes" : "no")}, Email2: {(p2 ? "yes" : "no")}."
            : "No SMTP passwords saved yet — enter each email account password and Save.";
    }

    [RelayCommand]
    private async Task SendSensitiveOtpAsync()
    {
        var (ok, message) = await _otp.SendSensitiveOtpsAsync("Admin security change");
        StatusMessage = ok ? message : $"Failed: {message}";
        if (!ok)
            MessageBox.Show(message, "OTP send failed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (OtpEnabled && (string.IsNullOrWhiteSpace(Email1) || string.IsNullOrWhiteSpace(Email2)))
        {
            StatusMessage = "Enter both OTP emails before enabling OTP.";
            return;
        }

        var existing = _settings.Current.Security;
        var emailsOrIpChanging =
            !string.Equals(existing.Email1?.Trim(), Email1.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Email2?.Trim(), Email2.Trim(), StringComparison.OrdinalIgnoreCase)
            || existing.OtpEnabled != OtpEnabled
            || !IpsEqual(existing.AllowedIps, AllowedIpsText);

        var alreadyLockedIn = existing.OtpEnabled
            && !string.IsNullOrWhiteSpace(existing.Email1)
            && !string.IsNullOrWhiteSpace(existing.Email2);

        // SMTP / password-only updates must not require OTP (otherwise chicken-egg when mail is broken).
        if (alreadyLockedIn && emailsOrIpChanging)
        {
            if (string.IsNullOrWhiteSpace(SensitiveOtp1) || string.IsNullOrWhiteSpace(SensitiveOtp2))
            {
                StatusMessage = "Changing emails / IP / OTP switch needs admin OTP. SMTP password-only Save is allowed without OTP.";
                MessageBox.Show(StatusMessage, "Admin OTP required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var (ok, message) = await _otp.VerifySensitiveOtpsAsync(SensitiveOtp1, SensitiveOtp2);
            SensitiveOtp1 = string.Empty;
            SensitiveOtp2 = string.Empty;
            if (!ok)
            {
                StatusMessage = message;
                MessageBox.Show(message, "Admin OTP failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var user1 = string.IsNullOrWhiteSpace(Smtp1Username) ? Email1.Trim() : Smtp1Username.Trim();
        var user2 = string.IsNullOrWhiteSpace(Smtp2Username) ? Email2.Trim() : Smtp2Username.Trim();

        _settings.Update(s =>
        {
            s.Security.OtpEnabled = OtpEnabled;
            s.Security.Email1 = Email1.Trim();
            s.Security.Email2 = Email2.Trim();
            s.Security.AllowedIps = AllowedIpsText
                .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            s.Security.Smtp1.Host = Smtp1Host.Trim();
            s.Security.Smtp1.Port = Smtp1Port <= 0 ? 587 : Smtp1Port;
            s.Security.Smtp1.Username = user1;
            s.Security.Smtp1.From = Email1.Trim();
            s.Security.Smtp1.UseSsl = Smtp1UseSsl;

            s.Security.Smtp2.Host = Smtp2Host.Trim();
            s.Security.Smtp2.Port = Smtp2Port <= 0 ? 587 : Smtp2Port;
            s.Security.Smtp2.Username = user2;
            s.Security.Smtp2.From = Email2.Trim();
            s.Security.Smtp2.UseSsl = Smtp2UseSsl;

            s.Security.SmtpHost = s.Security.Smtp1.Host;
            s.Security.SmtpPort = s.Security.Smtp1.Port;
            s.Security.SmtpUsername = s.Security.Smtp1.Username;
            s.Security.SmtpFrom = s.Security.Smtp1.From;
            s.Security.SmtpUseSsl = s.Security.Smtp1.UseSsl;
        });

        Smtp1Username = user1;
        Smtp1From = Email1.Trim();
        Smtp2Username = user2;
        Smtp2From = Email2.Trim();

        if (!string.IsNullOrWhiteSpace(Smtp1Password))
        {
            _credentials.SaveSecret(CredentialKeys.SmtpPassword1, Smtp1Password);
            _credentials.SaveSecret(CredentialKeys.SmtpPassword, Smtp1Password);
        }
        if (!string.IsNullOrWhiteSpace(Smtp2Password))
            _credentials.SaveSecret(CredentialKeys.SmtpPassword2, Smtp2Password);

        await _settings.SaveAsync();
        StatusMessage = "Saved. Email1 password → Shafquat SMTP. Email2 password → Umair SMTP. Click Send test OTP.";
        Smtp1Password = string.Empty;
        Smtp2Password = string.Empty;
    }

    [RelayCommand]
    private async Task TestOtpAsync()
    {
        StatusMessage = "Sending OTP…";
        var (ok, message) = await _otp.SendLoginOtpsAsync();
        StatusMessage = ok ? message : $"Failed: {message}";
        MessageBox.Show(
            message,
            ok ? "OTP sent" : "OTP send failed",
            MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private static bool IpsEqual(IReadOnlyList<string> existing, string text)
    {
        var next = text
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var prev = existing
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return prev.SequenceEqual(next, StringComparer.OrdinalIgnoreCase);
    }
}
