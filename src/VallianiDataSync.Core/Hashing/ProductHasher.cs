using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Hashing;

public static class ProductHasher
{
    public static string ComputeContentHash(ProductRecord product)
    {
        var sb = new StringBuilder(1024);
        Append(sb, product.StoreCode);
        Append(sb, product.Sku);
        Append(sb, product.ItemNumber);
        Append(sb, product.Name);
        Append(sb, product.Description);
        Append(sb, product.Category);
        Append(sb, product.Brand);
        Append(sb, product.Vendor);
        Append(sb, FormatDecimal(product.Cost));
        Append(sb, FormatDecimal(product.RetailPrice));
        Append(sb, FormatDecimal(product.SalePrice));
        Append(sb, FormatDecimal(product.Quantity));
        Append(sb, product.Barcode);
        Append(sb, product.Metal);
        Append(sb, product.Stone);
        Append(sb, product.Shape);
        Append(sb, FormatDecimal(product.Weight));
        Append(sb, product.ImagePath);

        foreach (var kv in product.AllFields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(sb, kv.Key);
            Append(sb, kv.Value);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void Append(StringBuilder sb, string? value)
    {
        sb.Append(value?.Trim() ?? string.Empty);
        sb.Append('|');
    }

    private static string FormatDecimal(decimal? value)
        => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
}
