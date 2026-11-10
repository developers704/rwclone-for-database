namespace VallianiDataSync.Core.Options;

public sealed class SmtpMailboxOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
}

public sealed class SecurityOptions
{
    /// <summary>When true, app opens on login and requires OTPs from both emails.</summary>
    public bool OtpEnabled { get; set; }

    public string Email1 { get; set; } = string.Empty;
    public string Email2 { get; set; } = string.Empty;

    /// <summary>Empty = allow any IP. Otherwise machine must have at least one listed IPv4.</summary>
    public List<string> AllowedIps { get; set; } = new();

    /// <summary>SMTP mailbox used to send OTP to Email 1 (that mailbox's own password).</summary>
    public SmtpMailboxOptions Smtp1 { get; set; } = new();

    /// <summary>SMTP mailbox used to send OTP to Email 2 (that mailbox's own password).</summary>
    public SmtpMailboxOptions Smtp2 { get; set; } = new();

    // Legacy single-SMTP fields (migrated into Smtp1 on load if Smtp1.Host empty)
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpFrom { get; set; } = string.Empty;
    public bool SmtpUseSsl { get; set; } = true;

    public int OtpExpiryMinutes { get; set; } = 10;

    public void MigrateLegacySmtpIfNeeded()
    {
        if (!string.IsNullOrWhiteSpace(Smtp1.Host))
            return;
        if (string.IsNullOrWhiteSpace(SmtpHost))
            return;
        Smtp1.Host = SmtpHost;
        Smtp1.Port = SmtpPort <= 0 ? 587 : SmtpPort;
        Smtp1.Username = SmtpUsername;
        Smtp1.From = string.IsNullOrWhiteSpace(SmtpFrom) ? Email1 : SmtpFrom;
        Smtp1.UseSsl = SmtpUseSsl;
    }
}
