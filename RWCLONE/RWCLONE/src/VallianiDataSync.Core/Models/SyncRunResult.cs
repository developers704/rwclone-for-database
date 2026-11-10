namespace VallianiDataSync.Core.Models;

public sealed class SyncRunResult
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public string StoreCode { get; set; } = string.Empty;
    public SyncMode Mode { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool WasCancelled { get; set; }
    public int TotalFound { get; set; }
    public int Uploaded { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public int SkippedUnchanged { get; set; }
    public int ImagesPending { get; set; }
    public int ImagesUploaded { get; set; }
    public int ImagesFailed { get; set; }
    public int ImagesMissing { get; set; }
    public List<string> Errors { get; set; } = new();
}
