namespace VallianiDataSync.Core.Interfaces;

public interface ICredentialStore
{
    void SaveSecret(string key, string secret);
    string? GetSecret(string key);
    void DeleteSecret(string key);
    bool HasSecret(string key);
}

public static class CredentialKeys
{
    public const string SqlPassword = "valliani.datasync.sql.password";
    public const string ApiKey = "valliani.datasync.api.key";
    public const string SmtpPassword = "valliani.datasync.smtp.password"; // legacy → mailbox 1
    public const string SmtpPassword1 = "valliani.datasync.smtp1.password";
    public const string SmtpPassword2 = "valliani.datasync.smtp2.password";

    public static string DestinationApiKey(string projectId) =>
        $"valliani.datasync.dest.{projectId}.apikey";
}
