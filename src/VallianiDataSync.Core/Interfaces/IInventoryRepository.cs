using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface IInventoryRepository
{
    Task<IReadOnlyList<ProductRecord>> GetInventoryAsync(
        string? storeCode,
        DateTime? modifiedSinceUtc,
        int take,
        int skip,
        CancellationToken cancellationToken = default);
}
