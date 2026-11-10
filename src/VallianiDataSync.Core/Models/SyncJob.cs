namespace VallianiDataSync.Core.Models;

public sealed class SyncJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StoreCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public SyncChannel Channel { get; set; }
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Pending;
    public string PayloadJson { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
