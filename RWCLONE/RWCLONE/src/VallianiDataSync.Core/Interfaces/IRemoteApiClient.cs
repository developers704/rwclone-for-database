using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface IRemoteApiClient
{
    Task<(bool Ok, string Message)> ValidateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoreRecord>> GetStoresAsync(CancellationToken cancellationToken = default);
    Task<ApiSyncResponse> SyncProductsAsync(IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<ApiSyncResponse> SyncInventoryAsync(IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<ApiSyncResponse> SyncPricesAsync(IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<ApiImageUploadResponse> UploadImageAsync(string storeCode, string sku, string filePath, string contentHash, CancellationToken cancellationToken = default);
    Task CompleteSyncAsync(Guid runId, SyncRunResult result, CancellationToken cancellationToken = default);
    Task<ApiSyncStatus?> GetSyncStatusAsync(Guid runId, CancellationToken cancellationToken = default);
}

public sealed class ApiSyncResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int Uploaded { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public sealed class ApiImageUploadResponse
{
    public bool Success { get; set; }
    public string? Url { get; set; }
    public string? Message { get; set; }
}

public sealed class ApiSyncStatus
{
    public Guid RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastUpdatedUtc { get; set; }
}
