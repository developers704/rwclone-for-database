using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface IStoreRepository
{
    Task<IReadOnlyList<StoreRecord>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task SaveStoresAsync(IEnumerable<StoreRecord> stores, CancellationToken cancellationToken = default);
}
