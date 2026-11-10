using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface ILocalDatabaseService
{
    Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
