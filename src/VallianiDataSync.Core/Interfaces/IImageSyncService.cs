using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface IImageSyncService
{
    Task<ImageSyncSummary> SyncImagesAsync(IEnumerable<ProductRecord> products, CancellationToken cancellationToken = default);
    Task<ImageSyncSummary> GetSummaryAsync(string? storeCode, CancellationToken cancellationToken = default);
}
