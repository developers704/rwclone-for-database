namespace VallianiDataSync.Infrastructure.Destinations;

/// <summary>
/// Marketplace CSV templates: inventory (8 cols) and products (35 cols).
/// </summary>
public static class MarketplaceCatalogMapper
{
    public static bool IsVendorCatalogUrl(string? apiUrl) =>
        !string.IsNullOrWhiteSpace(apiUrl) &&
        (apiUrl.Contains("vendor-catalog", StringComparison.OrdinalIgnoreCase)
         || apiUrl.Contains("sku-inventory", StringComparison.OrdinalIgnoreCase)
         || apiUrl.Contains("/bulk/", StringComparison.OrdinalIgnoreCase));

    /// <summary>One HTTP file for whole inventory/products (no chunking).</summary>
    public static bool WantsSingleFilePush(string? apiUrl) =>
        !string.IsNullOrWhiteSpace(apiUrl) &&
        (apiUrl.Contains("sku-inventory", StringComparison.OrdinalIgnoreCase)
         || apiUrl.Contains("mode=merge", StringComparison.OrdinalIgnoreCase)
         || IsVendorCatalogUrl(apiUrl));

    public const string CatalogInventorySql = BuiltInSyncQueries.InventorySql;

    /// <summary>
    /// Inventory rows: Sku, Warehouse (code), Quantity, … — drop GUID warehouses; one row per Sku+Warehouse.
    /// </summary>
    public static List<Dictionary<string, object?>> ToCatalogRows(List<Dictionary<string, object?>> rows)
    {
        var mapped = new List<Dictionary<string, object?>>(rows.Count);
        foreach (var r in rows)
        {
            var sku = Pick(r, "Sku", "SKU", "sku", "Item #");
            if (string.IsNullOrWhiteSpace(sku))
                continue;

            var warehouse = Pick(r, "Warehouse");
            if (string.IsNullOrWhiteSpace(warehouse) || LooksLikeGuid(warehouse))
                continue;

            var qty = PickObj(r, "Quantity", "quantity", "Qty");
            var o = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sku"] = sku.Trim(),
                ["Barcode"] = PickObj(r, "Barcode", "barcode"),
                ["Warehouse"] = warehouse.Trim(),
                ["Quantity"] = qty,
                ["lastRestocked"] = PickObj(r, "lastRestocked"),
                ["P-Cost"] = PickObj(r, "P-Cost", "cost", "Cost"),
                ["Tag Price"] = PickObj(r, "Tag Price", "price", "SellingPrice"),
                ["99 Price"] = PickObj(r, "99 Price", "VMLevelCat2")
            };
            mapped.Add(o);
        }

        if (mapped.Count <= 1)
            return mapped;

        return mapped
            .GroupBy(r => $"{r["Sku"]}|{r["Warehouse"]}", StringComparer.OrdinalIgnoreCase)
            .Select(g => new Dictionary<string, object?>(g.First(), StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Products: pin exact 35 headers; skip blank Sku.</summary>
    public static List<Dictionary<string, object?>> ToProductRows(List<Dictionary<string, object?>> rows)
    {
        var mapped = new List<Dictionary<string, object?>>(rows.Count);
        foreach (var r in rows)
        {
            var sku = Pick(r, "Sku", "SKU", "sku");
            if (string.IsNullOrWhiteSpace(sku))
                continue;
            var o = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in BuiltInSyncQueries.ProductHeaders)
            {
                if (h.Equals("Sku", StringComparison.OrdinalIgnoreCase))
                    o[h] = sku.Trim();
                else
                {
                    r.TryGetValue(h, out var v);
                    o[h] = v;
                }
            }
            mapped.Add(o);
        }
        return mapped;
    }

    public static List<Dictionary<string, object?>> OrderTemplateColumns(
        List<Dictionary<string, object?>> rows,
        IReadOnlyList<string>? headers = null)
    {
        var cols = headers ?? BuiltInSyncQueries.InventoryHeaders;
        return rows.Select(r =>
        {
            var o = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in cols)
            {
                r.TryGetValue(h, out var v);
                o[h] = v;
            }
            return o;
        }).ToList();
    }

    private static string? Pick(Dictionary<string, object?> r, params string[] keys)
    {
        var v = PickObj(r, keys);
        return Convert.ToString(v);
    }

    private static object? PickObj(Dictionary<string, object?> r, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!r.TryGetValue(k, out var v) || v is null)
                continue;
            if (string.IsNullOrWhiteSpace(Convert.ToString(v)))
                continue;
            return v;
        }
        return null;
    }

    private static bool LooksLikeGuid(string value)
    {
        var s = value.Trim();
        return s.Length >= 32 && Guid.TryParse(s, out _);
    }
}
