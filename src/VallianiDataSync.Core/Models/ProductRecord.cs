namespace VallianiDataSync.Core.Models;

public sealed class ProductRecord
{
    public string StoreCode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Vendor { get; set; }
    public decimal? Cost { get; set; }
    public decimal? RetailPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal? Quantity { get; set; }
    public string? Barcode { get; set; }
    public string? Metal { get; set; }
    public string? Stone { get; set; }
    public string? Shape { get; set; }
    public decimal? Weight { get; set; }
    public string? ImagePath { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Every DB column A→Z from the products row (raw values).</summary>
    public Dictionary<string, string?> AllFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
