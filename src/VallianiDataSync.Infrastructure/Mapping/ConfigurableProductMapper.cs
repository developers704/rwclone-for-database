using System.Data;
using System.Globalization;
using VallianiDataSync.Core.Hashing;
using VallianiDataSync.Core.Mapping;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Mapping;

public sealed class ConfigurableProductMapper : IProductMapper
{
    private readonly FieldMappingConfig _mapping;

    public ConfigurableProductMapper(FieldMappingConfig mapping)
    {
        _mapping = mapping;
    }

    public IReadOnlyList<string> ValidateMapping()
    {
        var errors = new List<string>();
        if (!_mapping.Columns.ContainsKey("Sku") || string.IsNullOrWhiteSpace(_mapping.Columns["Sku"]))
            errors.Add("SKU column mapping is required.");
        return errors;
    }

    public ProductRecord MapRow(IDataRecord record)
    {
        // A→Z: every column from the DB row
        var all = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < record.FieldCount; i++)
        {
            var name = record.GetName(i);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            all[name] = record.IsDBNull(i)
                ? null
                : Convert.ToString(record.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
        }

        var product = new ProductRecord
        {
            AllFields = all,
            Sku = GetString(record, "Sku") ?? GetRaw(all, "sku") ?? string.Empty,
            ItemNumber = GetString(record, "ItemNumber") ?? GetRaw(all, "vendor_model") ?? GetRaw(all, "item_number"),
            Name = GetString(record, "Name") ?? GetRaw(all, "description") ?? GetRaw(all, "name"),
            Description = GetString(record, "Description") ?? GetRaw(all, "description"),
            Category = GetString(record, "Category") ?? GetRaw(all, "category_id"),
            Brand = GetString(record, "Brand") ?? GetRaw(all, "brand_id"),
            Vendor = GetString(record, "Vendor"),
            Cost = GetDecimal(record, "Cost"),
            RetailPrice = GetDecimal(record, "RetailPrice") ?? ParseDec(GetRaw(all, "price")),
            SalePrice = GetDecimal(record, "SalePrice"),
            Quantity = GetDecimal(record, "Quantity"),
            StoreCode = GetString(record, "Store") ?? string.Empty,
            Barcode = GetString(record, "Barcode") ?? GetRaw(all, "sku"),
            Metal = GetString(record, "Metal") ?? GetRaw(all, "metal_type"),
            Stone = GetString(record, "Stone"),
            Shape = GetString(record, "Shape"),
            Weight = GetDecimal(record, "Weight") ?? ParseDec(GetRaw(all, "avg_weight")),
            ImagePath = GetString(record, "ImagePath") ?? GetRaw(all, "image_url"),
            ModifiedAt = GetDateTime(record) ?? ParseDate(GetRaw(all, "updated_at"))
        };

        product.ContentHash = ProductHasher.ComputeContentHash(product);
        return product;
    }

    private string? GetString(IDataRecord record, string logical)
    {
        if (!_mapping.Columns.TryGetValue(logical, out var column) || string.IsNullOrWhiteSpace(column))
            return null;
        var ordinal = TryGetOrdinal(record, column);
        if (ordinal < 0 || record.IsDBNull(ordinal))
            return null;
        return Convert.ToString(record.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim();
    }

    private decimal? GetDecimal(IDataRecord record, string logical)
    {
        var raw = GetString(record, logical);
        return ParseDec(raw);
    }

    private DateTime? GetDateTime(IDataRecord record)
    {
        if (string.IsNullOrWhiteSpace(_mapping.ModifiedAtColumn))
            return null;
        var ordinal = TryGetOrdinal(record, _mapping.ModifiedAtColumn);
        if (ordinal < 0 || record.IsDBNull(ordinal))
            return null;
        return Convert.ToDateTime(record.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static string? GetRaw(Dictionary<string, string?> all, string key)
        => all.TryGetValue(key, out var v) ? v : null;

    private static decimal? ParseDec(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }

    private static int TryGetOrdinal(IDataRecord record, string column)
    {
        for (var i = 0; i < record.FieldCount; i++)
        {
            if (string.Equals(record.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }
}
