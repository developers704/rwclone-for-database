using System.Net;
using System.Net.Mail;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Options;

namespace VallianiDataSync.Infrastructure.Security;

public sealed class OtpAuthService : IOtpAuthService
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly ILogger<OtpAuthService> _logger;
    private PendingOtp? _login;
    private PendingOtp? _sensitive;

    public OtpAuthService(ISettingsService settings, ICredentialStore credentials, ILogger<OtpAuthService> logger)
    {
        _settings = settings;
        _credentials = credentials;
        _logger = logger;
    }

    public (bool Ok, string Message) CheckIpAllowed()
    {
        var allowed = _settings.Current.Security.AllowedIps
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();
        if (allowed.Count == 0)
            return (true, "IP allow-list empty (all IPs allowed).");

        var local = GetLocalIpv4().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowed.Any(a => local.Contains(a)))
            return (true, "IP allowed.");

        return (false, $"This PC IP is not allowed. Local: {string.Join(", ", local)}. Allowed: {string.Join(", ", allowed)}");
    }

    public Task<(bool Ok, string Message)> SendLoginOtpsAsync(CancellationToken cancellationToken = default)
        => SendPairAsync(isSensitive: false, "Login", cancellationToken);

    public Task<(bool Ok, string Message)> VerifyLoginOtpsAsync(string otpEmail1, string otpEmail2, CancellationToken cancellationToken = default)
        => Task.FromResult(VerifyPair(_login, otpEmail1, otpEmail2, clear: true));

    public Task<(bool Ok, string Message)> SendSensitiveOtpsAsync(string reason, CancellationToken cancellationToken = default)
        => SendPairAsync(isSensitive: true, reason, cancellationToken);

    public Task<(bool Ok, string Message)> VerifySensitiveOtpsAsync(string otpEmail1, string otpEmail2, CancellationToken cancellationToken = default)
        => Task.FromResult(VerifyPair(_sensitive, otpEmail1, otpEmail2, clear: true));

    private async Task<(bool Ok, string Message)> SendPairAsync(bool isSensitive, string reason, CancellationToken cancellationToken)
    {
        var ip = CheckIpAllowed();
        if (!ip.Ok)
            return ip;

        var sec = _settings.Current.Security;
        sec.MigrateLegacySmtpIfNeeded();

        if (string.IsNullOrWhiteSpace(sec.Email1) || string.IsNullOrWhiteSpace(sec.Email2))
            return (false, "Set both OTP emails on Security page and enable OTP.");

        var code1 = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        var code2 = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        while (code2 == code1)
            code2 = RandomNumberGenerator.GetInt32(100000, 999999).ToString();

        var pending = new PendingOtp(
            Hash(code1),
            Hash(code2),
            DateTime.UtcNow.AddMinutes(Math.Clamp(sec.OtpExpiryMinutes, 3, 30)));

        if (isSensitive)
            _sensitive = pending;
        else
            _login = pending;

        var subject = $"Valliani Data Sync OTP ({reason})";
        var mins = Math.Clamp(sec.OtpExpiryMinutes, 3, 30);
        var (ok1, msg1) = await DeliverViaMailboxAsync(
            mailbox: 1,
            smtp: sec.Smtp1,
            to: sec.Email1.Trim(),
            subject,
            reason,
            code1,
            mins).ConfigureAwait(false);
        if (!ok1)
            return (false, $"Email 1 ({sec.Email1}): {msg1}");

        var (ok2, msg2) = await DeliverViaMailboxAsync(
            mailbox: 2,
            smtp: sec.Smtp2,
            to: sec.Email2.Trim(),
            subject,
            reason,
            code2,
            mins).ConfigureAwait(false);
        if (!ok2)
            return (false, $"Email 1 OK. Email 2 ({sec.Email2}): {msg2}");

        return (true, $"OTPs sent to {sec.Email1} and {sec.Email2}. Valid {mins} minutes.");
    }

    private (bool Ok, string Message) VerifyPair(PendingOtp? pending, string otp1, string otp2, bool clear)
    {
        if (pending is null)
            return (false, "No OTP pending. Click Send OTP first.");
        if (DateTime.UtcNow > pending.ExpiresUtc)
            return (false, "OTP expired. Send again.");
        if (!SlowEquals(pending.Hash1, Hash(otp1?.Trim() ?? "")) || !SlowEquals(pending.Hash2, Hash(otp2?.Trim() ?? "")))
            return (false, "Invalid OTP. Both codes must match the emails.");

        if (clear)
        {
            if (ReferenceEquals(pending, _login))
                _login = null;
            if (ReferenceEquals(pending, _sensitive))
                _sensitive = null;
        }
        return (true, "OTP verified.");
    }

    private async Task<(bool Ok, string Message)> DeliverViaMailboxAsync(
        int mailbox,
        SmtpMailboxOptions smtp,
        string to,
        string subject,
        string reason,
        string code,
        int expiryMinutes)
    {
        if (string.IsNullOrWhiteSpace(smtp.Host))
            return (false, $"SMTP host missing. Open Security → Email {mailbox} SMTP → enter Host (e.g. mail.valliani.app), username, and that email's password, then Save.");

        var pass = ResolvePassword(mailbox);
        if (string.IsNullOrEmpty(pass))
            return (false, $"Password missing for Email {mailbox}. Enter that email account password on Security page and Save.");

        var user = !string.IsNullOrWhiteSpace(smtp.Username)
            ? smtp.Username.Trim()
            : to;
        var from = !string.IsNullOrWhiteSpace(smtp.From) ? smtp.From.Trim() : to;
        var plain = $"Valliani Data Sync\n\nYour one-time password for {reason}:\n{code}\n\nValid for {expiryMinutes} minutes.\nSent to: {to}\n\nIf you did not request this, ignore this email.";
        var html = BuildOtpHtml(to, reason, code, expiryMinutes, mailbox);

        try
        {
            using var client = new SmtpClient(smtp.Host.Trim(), smtp.Port <= 0 ? 587 : smtp.Port)
            {
                EnableSsl = smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 45000,
                UseDefaultCredentials = false
            };
            client.Credentials = new NetworkCredential(user, pass);

            using var message = new MailMessage
            {
                From = new MailAddress(from, "Valliani Data Sync"),
                Subject = subject
            };
            message.To.Add(to);
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(plain, Encoding.UTF8, "text/plain"));
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(html, Encoding.UTF8, "text/html"));
            await client.SendMailAsync(message).ConfigureAwait(false);
            return (true, "Sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP {Mailbox} send failed to {To} via {Host}:{Port} user={User}", mailbox, to, smtp.Host, smtp.Port, user);
            return (false, FormatSmtpError(ex, user, smtp.Host, smtp.Port <= 0 ? 587 : smtp.Port));
        }
    }

    private static string BuildOtpHtml(string to, string reason, string code, int expiryMinutes, int mailbox)
    {
        // Inline CSS — email clients strip most stylesheets.
        return $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width""/></head>
<body style=""margin:0;padding:0;background:#E7EEF2;font-family:Segoe UI,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#E7EEF2;padding:28px 12px;"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""520"" cellpadding=""0"" cellspacing=""0"" style=""max-width:520px;width:100%;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #C8D4DE;"">
        <tr>
          <td style=""background:linear-gradient(135deg,#0C5A4C,#1A4D45);padding:28px 32px;"">
            <div style=""font-size:11px;letter-spacing:2px;color:#A8D8CF;text-transform:uppercase;"">Valliani</div>
            <div style=""font-size:22px;font-weight:700;color:#ffffff;margin-top:4px;"">Data Sync</div>
            <div style=""font-size:13px;color:#D8EEE7;margin-top:8px;"">Security verification</div>
          </td>
        </tr>
        <tr>
          <td style=""padding:28px 32px 8px;color:#14242F;"">
            <div style=""font-size:16px;font-weight:600;margin-bottom:8px;"">Your one-time password</div>
            <div style=""font-size:14px;color:#5B6C79;line-height:1.5;"">
              Use this code for <strong style=""color:#0C5A4C;"">{WebUtility.HtmlEncode(reason)}</strong>.
              Enter it in the field for <strong>{WebUtility.HtmlEncode(to)}</strong> (OTP {mailbox}).
            </div>
          </td>
        </tr>
        <tr>
          <td align=""center"" style=""padding:20px 32px;"">
            <div style=""display:inline-block;background:#D8EEE7;border:1px solid #1F9A80;border-radius:12px;padding:18px 28px;"">
              <div style=""font-size:11px;letter-spacing:1.5px;color:#0C5A4C;text-transform:uppercase;margin-bottom:6px;"">OTP code</div>
              <div style=""font-size:36px;font-weight:700;letter-spacing:10px;color:#0C5A4C;font-family:Consolas,monospace;"">{WebUtility.HtmlEncode(code)}</div>
            </div>
          </td>
        </tr>
        <tr>
          <td style=""padding:0 32px 28px;"">
            <div style=""font-size:13px;color:#5B6C79;line-height:1.55;"">
              Valid for <strong style=""color:#14242F;"">{expiryMinutes} minutes</strong>. Do not share this code.
              If you did not request it, you can ignore this email.
            </div>
          </td>
        </tr>
        <tr>
          <td style=""background:#F5F9FA;border-top:1px solid #C8D4DE;padding:14px 32px;font-size:11px;color:#5B6C79;"">
            Valliani Data Sync · secure workspace access
          </td>
        </tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }

    private static string FormatSmtpError(Exception ex, string user, string host, int port)
    {
        var parts = new List<string>();
        for (var e = ex; e != null; e = e.InnerException!)
            parts.Add(e.Message);

        var detail = string.Join(" → ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        var hint = detail.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                   || detail.Contains("535", StringComparison.OrdinalIgnoreCase)
                   || detail.Contains("5.7.8", StringComparison.OrdinalIgnoreCase)
            ? " Wrong username/password for this email (use full email + that mailbox password)."
            : detail.Contains("secure", StringComparison.OrdinalIgnoreCase) || detail.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                ? " Try port 587 + SSL on, or ask host for SMTP settings."
                : string.Empty;

        return $"SMTP {host}:{port} user={user} failed. {detail}.{hint}";
    }

    private string? ResolvePassword(int mailbox)
    {
        if (mailbox == 1)
        {
            return _credentials.GetSecret(CredentialKeys.SmtpPassword1)
                   ?? _credentials.GetSecret(CredentialKeys.SmtpPassword);
        }
        return _credentials.GetSecret(CredentialKeys.SmtpPassword2);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static bool SlowEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static IEnumerable<string> GetLocalIpv4()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    yield return addr.Address.ToString();
            }
        }
    }

    private sealed record PendingOtp(string Hash1, string Hash2, DateTime ExpiresUtc);
}
