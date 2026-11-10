using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface IDestinationSyncService
{
    Task<(bool Ok, string Message, int Sent, int Skipped)> SyncProjectAsync(
        string projectId,
        bool fullSync,
        CancellationToken cancellationToken = default);

    Task RunDueProjectsAsync(CancellationToken cancellationToken = default);
}
