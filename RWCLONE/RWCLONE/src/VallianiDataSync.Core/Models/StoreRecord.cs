namespace VallianiDataSync.Core.Models;

public sealed class StoreRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? LocalStoreCode { get; set; }
    public string? RemoteStoreId { get; set; }
}
