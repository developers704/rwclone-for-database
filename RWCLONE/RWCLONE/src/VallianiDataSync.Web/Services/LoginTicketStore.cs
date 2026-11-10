using Microsoft.Extensions.Caching.Memory;

namespace VallianiDataSync.Web.Services;

public sealed class LoginTicketStore
{
    private readonly IMemoryCache _cache;

    public LoginTicketStore(IMemoryCache cache) => _cache = cache;

    public string Issue(string displayName)
    {
        var ticket = Guid.NewGuid().ToString("N");
        _cache.Set("login:" + ticket, displayName, TimeSpan.FromMinutes(2));
        return ticket;
    }

    public bool TryConsume(string ticket, out string displayName)
    {
        displayName = string.Empty;
        if (string.IsNullOrWhiteSpace(ticket))
            return false;
        var key = "login:" + ticket.Trim();
        if (!_cache.TryGetValue(key, out string? name) || string.IsNullOrWhiteSpace(name))
            return false;
        _cache.Remove(key);
        displayName = name;
        return true;
    }
}
