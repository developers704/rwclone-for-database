using System.Text.Json;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Data;

public sealed class JsonStoreRepository : IStoreRepository
{
    private readonly ISettingsService _settings;

    public JsonStoreRepository(ISettingsService settings) => _settings = settings;

    public Task<IReadOnlyList<StoreRecord>> GetStoresAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<StoreRecord>>(_settings.Current.Stores.ToList());

    public async Task SaveStoresAsync(IEnumerable<StoreRecord> stores, CancellationToken cancellationToken = default)
    {
        _settings.Update(s =>
        {
            s.Stores = stores.Select(Clone).ToList();
        });
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private static StoreRecord Clone(StoreRecord s) => new()
    {
        Id = s.Id == Guid.Empty ? Guid.NewGuid() : s.Id,
        StoreCode = s.StoreCode,
        StoreName = s.StoreName,
        IsActive = s.IsActive,
        LocalStoreCode = s.LocalStoreCode,
        RemoteStoreId = s.RemoteStoreId
    };
}
