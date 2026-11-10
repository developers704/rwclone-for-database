using VallianiDataSync.Core.Interfaces;

namespace VallianiDataSync.Infrastructure.Security;

public sealed class AuthSession : IAuthSession
{
    public bool IsAuthenticated { get; private set; }

    public void MarkAuthenticated() => IsAuthenticated = true;

    public void Lock() => IsAuthenticated = false;
}
