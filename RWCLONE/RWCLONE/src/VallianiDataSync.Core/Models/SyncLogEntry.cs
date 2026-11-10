namespace VallianiDataSync.Core.Models;

public sealed class SyncLogEntry
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public LogLevelKind Level { get; set; } = LogLevelKind.Information;
    public string? StoreCode { get; set; }
    public string? Sku { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string? ApiEndpoint { get; set; }
    public string? Response { get; set; }
    public string? Error { get; set; }
    public string Message { get; set; } = string.Empty;
}
