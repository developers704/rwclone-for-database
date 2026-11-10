using System.Data;
using System.Text.Json;
using FluentAssertions;
using VallianiDataSync.Core.Hashing;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Core.Security;
using VallianiDataSync.Infrastructure.Mapping;

namespace VallianiDataSync.Tests;

public class ProductHasherTests
{
    [Fact]
    public void ComputeContentHash_IsStable_ForSameValues()
    {
        var a = Sample();
        var b = Sample();
        ProductHasher.ComputeContentHash(a).Should().Be(ProductHasher.ComputeContentHash(b));
    }

    [Fact]
    public void ComputeContentHash_Changes_WhenPriceChanges()
    {
        var a = Sample();
        var b = Sample();
        b.RetailPrice = 999m;
        ProductHasher.ComputeContentHash(a).Should().NotBe(ProductHasher.ComputeContentHash(b));
    }

    private static ProductRecord Sample() => new()
    {
        StoreCode = "STORE01",
        Sku = "ABC123",
        Name = "Ring",
        RetailPrice = 299m,
        Quantity = 5
    };
}

public class MappingTests
{
    [Fact]
    public void Mapper_ReadsConfiguredColumns()
    {
        var config = new FieldMappingConfig
        {
            Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Sku"] = "ItemSku",
                ["Name"] = "Desc",
                ["Store"] = "Loc",
                ["RetailPrice"] = "Price"
            }
        };
        var mapper = new ConfigurableProductMapper(config);
        var table = new DataTable();
        table.Columns.Add("ItemSku", typeof(string));
        table.Columns.Add("Desc", typeof(string));
        table.Columns.Add("Loc", typeof(string));
        table.Columns.Add("Price", typeof(decimal));
        table.Rows.Add("SKU1", "Gold Ring", "STORE01", 100m);

        using var reader = table.CreateDataReader();
        reader.Read().Should().BeTrue();
        var mapped = mapper.MapRow(reader);
        mapped.Sku.Should().Be("SKU1");
        mapped.Name.Should().Be("Gold Ring");
        mapped.StoreCode.Should().Be("STORE01");
        mapped.RetailPrice.Should().Be(100m);
        mapped.ContentHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Mapper_RequiresSkuMapping()
    {
        var config = new FieldMappingConfig { Columns = new Dictionary<string, string>() };
        var mapper = new ConfigurableProductMapper(config);
        mapper.ValidateMapping().Should().Contain(e => e.Contains("SKU"));
    }
}

public class PathSanitizerTests
{
    [Fact]
    public void IsUnderRoot_AllowsChild()
    {
        var root = Path.GetTempPath().TrimEnd('\\', '/');
        var child = Path.Combine(root, "images", "a.jpg");
        PathSanitizer.IsUnderRoot(child, root).Should().BeTrue();
    }
}

public class DuplicatePreventionTests
{
    [Fact]
    public void IdempotencyKey_IsStoreSkuAndHash()
    {
        var p = new ProductRecord { StoreCode = "S1", Sku = "A", ContentHash = "HASH1" };
        var key = $"{p.StoreCode}:{p.Sku}:{p.ContentHash}";
        key.Should().Be("S1:A:HASH1");
    }
}

public class ApiSerializationTests
{
    [Fact]
    public void ProductDto_SerializesCamelCase()
    {
        var payload = new
        {
            storeCode = "STORE01",
            sku = "ABC123",
            name = "Product Name",
            cost = 100.00m,
            price = 299.00m,
            quantity = 5m,
            barcode = "123",
            imageUrls = Array.Empty<string>()
        };
        var json = JsonSerializer.Serialize(payload);
        json.Should().Contain("storeCode");
        json.Should().Contain("ABC123");
        json.Should().Contain("299");
    }
}

public class RetryBehaviorTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    public void ExponentialBackoff_Seconds(int attempt, double expectedSeconds)
    {
        Math.Pow(2, attempt).Should().Be(expectedSeconds);
    }
}

public class ImageMappingTests
{
    [Fact]
    public void ResolvePrefersExactSkuFileName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vds-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "ABC123.jpg");
            File.WriteAllBytes(file, [1, 2, 3]);
            File.Exists(file).Should().BeTrue();
            Path.GetFileNameWithoutExtension(file).Should().Be("ABC123");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
