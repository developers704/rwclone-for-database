using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface ISyncEngine
{
    bool IsRunning { get; }
    SyncRunResult? LastResult { get; }
    event EventHandler<SyncRunResult>? ProgressChanged;

    Task<SyncRunResult> RunAsync(SyncMode mode, SyncChannels channels, string? storeCode, CancellationToken cancellationToken = default);
    Task StopAsync();
    Task<SyncRunResult> RetryFailedAsync(string? storeCode, CancellationToken cancellationToken = default);
}
