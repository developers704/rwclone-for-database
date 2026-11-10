namespace VallianiDataSync.Core.Models;

public sealed class FieldMappingConfig
{
    public string ProductsQuery { get; set; } =
        "SELECT TOP (@Take) * FROM [{Table}] WHERE (@StoreCode IS NULL OR [{StoreColumn}] = @StoreCode)";

    public string? ProductsTable { get; set; } = "ItemMaster";
    public string? ModifiedAtColumn { get; set; }

    /// <summary>When true, Sync Now rediscovers every DB table. When false, only SelectedTables sync.</summary>
    public bool SyncAllTables { get; set; }

    /// <summary>Tables included in multi-table sync when SyncAllTables is false.</summary>
    public List<string> SelectedTables { get; set; } = new();

    /// <summary>All table names discovered from DB.</summary>
    public List<string> DiscoveredTables { get; set; } = new();

    /// <summary>Merged columns from selected tables (A→Z).</summary>
    public List<string> DiscoveredColumns { get; set; } = new();

    /// <summary>Per-table columns discovered after Load columns on selected tables.</summary>
    public Dictionary<string, List<string>> TableColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional rename: source table → destination table name sent to API.
    /// Empty / missing = send the same source table name.
    /// </summary>
    public Dictionary<string, string> TableAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Custom SQL per logical sync source name (e.g. inventory / sales / sales_persons).
    /// When set, ReadTableRows uses this query instead of SELECT * FROM [name].
    /// </summary>
    public Dictionary<string, string> TableQueries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

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
