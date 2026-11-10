using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface ISyncStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<string?> GetLastHashAsync(string storeCode, string sku, CancellationToken cancellationToken = default);
    Task SetLastHashAsync(string storeCode, string sku, string hash, CancellationToken cancellationToken = default);
    Task EnqueueAsync(SyncJob job, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncJob>> DequeuePendingAsync(int take, CancellationToken cancellationToken = default);
    Task UpdateJobAsync(SyncJob job, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLastSuccessfulSyncAsync(string? storeCode, CancellationToken cancellationToken = default);
    Task SetLastSuccessfulSyncAsync(string storeCode, DateTime utc, CancellationToken cancellationToken = default);
    Task AddLogAsync(SyncLogEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncLogEntry>> QueryLogsAsync(string? search, string? storeCode, LogLevelKind? level, int take, CancellationToken cancellationToken = default);
    Task SaveImageStateAsync(ImageSyncItem item, CancellationToken cancellationToken = default);
    Task<ImageSyncItem?> GetImageStateAsync(string storeCode, string sku, CancellationToken cancellationToken = default);
    Task<ImageSyncSummary> GetImageSummaryAsync(string? storeCode, CancellationToken cancellationToken = default);
}
