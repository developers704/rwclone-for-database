using System.Security.Cryptography;
using System.Text;
using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.Infrastructure.Security;

/// <summary>
/// Stores secrets using Windows DPAPI (CurrentUser scope).
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string _directory;

    public DpapiCredentialStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VallianiDataSync",
            "secrets");
        Directory.CreateDirectory(_directory);
    }

    public void SaveSecret(string key, string secret)
    {
        var path = GetPath(key);
        var bytes = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    public string? GetSecret(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
            return null;
        var protectedBytes = File.ReadAllBytes(path);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public void DeleteSecret(string key)
    {
        var path = GetPath(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    public bool HasSecret(string key) => File.Exists(GetPath(key));

    private string GetPath(string key)
    {
        var safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_directory, safe + ".dpapi");
    }
}
