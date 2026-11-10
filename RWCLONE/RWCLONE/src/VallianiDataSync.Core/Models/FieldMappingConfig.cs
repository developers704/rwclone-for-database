namespace VallianiDataSync.Core.Models;

public sealed class FieldMappingConfig
{
    public string ProductsQuery { get; set; } =
        "SELECT TOP (@Take) * FROM [{Table}] WHERE (@StoreCode IS NULL OR [{StoreColumn}] = @StoreCode)";

    public string? ProductsTable { get; set; } = "ItemMaster";
    public string? ModifiedAtColumn { get; set; }

    /// <summary>When true, destination Sync sends every selected (or all user) table as JSON rows.</summary>
    public bool SyncAllTables { get; set; } = true;

    /// <summary>Tables included in multi-table sync. Empty + SyncAllTables = discover all user tables at sync time.</summary>
    public List<string> SelectedTables { get; set; } = new();

    /// <summary>All table names discovered from DB.</summary>
    public List<string> DiscoveredTables { get; set; } = new();

    /// <summary>All physical columns discovered from DB (A→Z).</summary>
    public List<string> DiscoveredColumns { get; set; } = new();

    public Dictionary<string, string> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sku"] = "SKU",
        ["ItemNumber"] = "ItemNumber",
        ["Name"] = "ProductName",
        ["Description"] = "Description",
        ["Category"] = "Category",
        ["Brand"] = "Brand",
        ["Vendor"] = "Vendor",
        ["Cost"] = "Cost",
        ["RetailPrice"] = "RetailPrice",
        ["SalePrice"] = "SalePrice",
        ["Quantity"] = "Quantity",
        ["Store"] = "StoreCode",
        ["Barcode"] = "Barcode",
        ["Metal"] = "Metal",
        ["Stone"] = "Stone",
        ["Shape"] = "Shape",
        ["Weight"] = "Weight",
        ["ImagePath"] = "ImagePath"
    };
}
