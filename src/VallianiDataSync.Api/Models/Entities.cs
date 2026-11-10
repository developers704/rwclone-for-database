namespace VallianiDataSync.Api.Models;

public sealed class Store
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StoreCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }
    public string? Vendor { get; set; }
    public decimal? Cost { get; set; }
    public decimal? Price { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal? Quantity { get; set; }
    public string? Barcode { get; set; }
    public string? Metal { get; set; }
    public string? Stone { get; set; }
    public string? Shape { get; set; }
    public decimal? Weight { get; set; }
    public string? ImageUrlsJson { get; set; }
    public string? ContentHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProductImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StoreCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SyncRun
{
    public Guid Id { get; set; }
    public string StoreCode { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public int Uploaded { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ActivityLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Module { get; set; } = string.Empty;
    public string Activity { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? StoreCode { get; set; }
    public string? ClientId { get; set; }
}
