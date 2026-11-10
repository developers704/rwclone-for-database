using System.Text.Json;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Destinations;

/// <summary>
/// Destination format is uploaded by the user — we do not invent fields.
/// CSV header / JSON sample object / format definition JSON.
/// </summary>
public static class FormatTemplateImporter
{
    public static (bool Ok, string Message, List<DestinationFormatField> Fields, string BodyFormatHint) Import(string filePath)
    {
        if (!File.Exists(filePath))
            return (false, "File not found.", new(), "JsonArray");

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var text = File.ReadAllText(filePath);

        try
        {
            if (ext is ".csv" or ".tsv")
            {
                var sep = ext == ".tsv" ? '\t' : ',';
                var headerLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                    ?? string.Empty;
                var keys = SplitCsvLine(headerLine, sep)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => k.Trim().Trim('"'))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (keys.Count == 0)
                    return (false, "CSV header empty.", new(), "Csv");
                return (true, $"Imported {keys.Count} CSV columns.", keys.Select(ToField).ToList(), "Csv");
            }

            if (ext is ".json" or ".txt")
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                // Explicit format: { "fields": [ { "destinationKey": "sku", "sourceField": "Sku" }, ... ] }
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("fields", out var fieldsEl)
                    && fieldsEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<DestinationFormatField>();
                    foreach (var item in fieldsEl.EnumerateArray())
                    {
                        var dest = item.TryGetProperty("destinationKey", out var dk) ? dk.GetString()
                            : item.TryGetProperty("key", out var k) ? k.GetString() : null;
                        var src = item.TryGetProperty("sourceField", out var sf) ? sf.GetString()
                            : item.TryGetProperty("source", out var s) ? s.GetString() : null;
                        if (string.IsNullOrWhiteSpace(dest))
                            continue;
                        list.Add(new DestinationFormatField
                        {
                            DestinationKey = dest.Trim(),
                            SourceField = GuessSource(src ?? dest)
                        });
                    }
                    if (list.Count == 0)
                        return (false, "fields[] empty in format JSON.", new(), "JsonArray");
                    return (true, $"Imported {list.Count} fields from format definition.", list, "JsonArray");
                }

                // Sample product object → keys only
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var keys = root.EnumerateObject().Select(p => p.Name).ToList();
                    return (true, $"Imported {keys.Count} keys from JSON sample.", keys.Select(ToField).ToList(), "JsonArray");
                }

                // Array of samples → first object keys
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                    && root[0].ValueKind == JsonValueKind.Object)
                {
                    var keys = root[0].EnumerateObject().Select(p => p.Name).ToList();
                    return (true, $"Imported {keys.Count} keys from JSON array sample.", keys.Select(ToField).ToList(), "JsonArray");
                }

                // Plain text: one key per line
                if (ext == ".txt")
                {
                    var keys = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(l => !l.StartsWith('#') && !l.StartsWith('{'))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (keys.Count > 0)
                        return (true, $"Imported {keys.Count} keys from text.", keys.Select(ToField).ToList(), "JsonArray");
                }
            }

            return (false, "Unsupported format. Use .csv header, JSON sample object, or {\"fields\":[...]} definition.", new(), "JsonArray");
        }
        catch (Exception ex)
        {
            return (false, $"Import failed: {ex.Message}", new(), "JsonArray");
        }
    }

    public static DestinationFormatField FromKey(string destinationKey) => ToField(destinationKey);

    private static DestinationFormatField ToField(string destinationKey) => new()
    {
        DestinationKey = destinationKey,
        SourceField = GuessSource(destinationKey),
        Enabled = true
    };

    private static string GuessSource(string name)
    {
        var n = name.Trim();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sku"] = "Sku",
            ["SKU"] = "Sku",
            ["item_number"] = "ItemNumber",
            ["itemNumber"] = "ItemNumber",
            ["title"] = "Name",
            ["name"] = "Name",
            ["product_name"] = "Name",
            ["description"] = "Description",
            ["price"] = "RetailPrice",
            ["retail_price"] = "RetailPrice",
            ["retailPrice"] = "RetailPrice",
            ["sale_price"] = "SalePrice",
            ["salePrice"] = "SalePrice",
            ["cost"] = "Cost",
            ["qty"] = "Quantity",
            ["quantity"] = "Quantity",
            ["barcode"] = "Barcode",
            ["upc"] = "Barcode",
            ["metal"] = "Metal",
            ["stone"] = "Stone",
            ["shape"] = "Shape",
            ["weight"] = "Weight",
            ["image"] = "ImagePath",
            ["image_url"] = "ImagePath",
            ["imageUrl"] = "ImagePath",
            ["brand"] = "Brand",
            ["category"] = "Category",
            ["vendor"] = "Vendor",
            ["store"] = "StoreCode",
            ["storeCode"] = "StoreCode"
        };
        if (map.TryGetValue(n, out var hit))
            return hit;
        // exact ProductRecord property match
        if (ProductFormatExporter.AvailableSourceFields.Any(s => s.Equals(n, StringComparison.OrdinalIgnoreCase)))
            return ProductFormatExporter.AvailableSourceFields.First(s => s.Equals(n, StringComparison.OrdinalIgnoreCase));
        return "Sku";
    }

    private static IEnumerable<string> SplitCsvLine(string line, char sep)
    {
        var cur = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == sep && !inQuotes)
            {
                yield return cur.ToString();
                cur.Clear();
                continue;
            }
            cur.Append(ch);
        }
        yield return cur.ToString();
    }
}
