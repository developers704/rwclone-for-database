using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Destinations;

public static class ProductFormatExporter
{
    private static readonly HashSet<string> SkipProps = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(ProductRecord.AllFields),
        nameof(ProductRecord.ContentHash)
    };

    private static readonly Dictionary<string, PropertyInfo> Props = typeof(ProductRecord)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => !SkipProps.Contains(p.Name) && p.PropertyType != typeof(Dictionary<string, string?>))
        .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> BuiltInSourceFields { get; } =
        Props.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Built-in logical fields + discovered DB columns (A→Z).</summary>
    public static IReadOnlyList<string> GetAvailableSourceFields(IEnumerable<string>? discoveredColumns)
    {
        return BuiltInSourceFields
            .Concat(discoveredColumns ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // backward-compatible alias
    public static IReadOnlyList<string> AvailableSourceFields => BuiltInSourceFields;

    public static Dictionary<string, object?> MapProduct(ProductRecord product, IEnumerable<DestinationFormatField> fields)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.DestinationKey) || string.IsNullOrWhiteSpace(field.SourceField))
                continue;
            row[field.DestinationKey.Trim()] = GetValue(product, field.SourceField.Trim());
        }
        return row;
    }

    public static string ToJsonArray(IEnumerable<Dictionary<string, object?>> rows)
        => JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });

    public static string ToCsv(IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyList<DestinationFormatField> fields)
    {
        var keys = fields
            .Where(f => !string.IsNullOrWhiteSpace(f.DestinationKey))
            .Select(f => f.DestinationKey.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return BuildCsv(rows, keys);
    }

    /// <summary>CSV with all columns present in the row dictionaries (selected-table sync).</summary>
    public static string ToCsvAllColumns(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var k in row.Keys)
            {
                if (seen.Add(k))
                    keys.Add(k);
            }
        }
        // Keep first-row key order when it matches a fixed template; else A→Z
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        return BuildCsv(rows, keys);
    }

    /// <summary>CSV with exact header order (Sku, Barcode, Warehouse, …).</summary>
    public static string ToCsvOrdered(IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyList<string> headers)
        => BuildCsv(rows, headers);

    public static void WriteCsvHeader(TextWriter writer, IReadOnlyList<string> keys)
        => writer.WriteLine(string.Join(",", keys.Select(EscapeCsv)));

    public static void WriteCsvRows(TextWriter writer, IEnumerable<Dictionary<string, object?>> rows, IReadOnlyList<string> keys)
    {
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", keys.Select(k =>
            {
                row.TryGetValue(k, out var v);
                return EscapeCsv(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
            })));
        }
    }

    private static string BuildCsv(IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyList<string> keys)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", keys.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", keys.Select(k =>
            {
                row.TryGetValue(k, out var v);
                return EscapeCsv(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
            })));
        }
        return sb.ToString();
    }

    private static object? GetValue(ProductRecord product, string sourceField)
    {
        if (Props.TryGetValue(sourceField, out var prop))
            return prop.GetValue(product);

        if (product.AllFields.TryGetValue(sourceField, out var raw))
            return raw;

        return null;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
