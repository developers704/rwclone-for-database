namespace VallianiDataSync.Core.Models;

public sealed class ImageSyncItem
{
    public string StoreCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public DateTime? LocalModifiedUtc { get; set; }
    public ImageSyncItemStatus Status { get; set; } = ImageSyncItemStatus.Pending;
    public string? RemoteUrl { get; set; }
    public string? Error { get; set; }
}

public enum ImageSyncItemStatus
{
    Pending,
    Uploaded,
    Failed,
    Missing,
    SkippedUnchanged
}

public sealed class ImageSyncSummary
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Uploaded { get; set; }
    public int Failed { get; set; }
    public int Missing { get; set; }
}
