namespace VallianiDataSync.Api.Models;

public sealed class ProductItemDto
{
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
    public string[]? ImageUrls { get; set; }
    public string? ContentHash { get; set; }
}

public sealed class SyncBatchRequest
{
    public List<ProductItemDto> Items { get; set; } = new();
    public bool Replace { get; set; }
}

public sealed class SyncBatchResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public int Uploaded { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
}

public sealed class AuthValidateResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class StoreDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class ImageUploadResponse
{
    public string? Url { get; set; }
    public string? Message { get; set; }
}

public sealed class SyncCompleteRequest
{
    public Guid RunId { get; set; }
    public string? StoreCode { get; set; }
    public int Uploaded { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class SyncStatusDto
{
    public Guid RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastUpdatedUtc { get; set; }
}

public sealed class ActivityRequest
{
    public string Module { get; set; } = string.Empty;
    public string Activity { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? StoreCode { get; set; }
}
