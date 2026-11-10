using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VallianiDataSync.Api.Data;
using VallianiDataSync.Api.Models;

namespace VallianiDataSync.Api.Services;

public sealed class ApiOptions
{
    public string ApiKey { get; set; } = "valliani-dev-api-key-change-me";
    public string PublicBaseUrl { get; set; } = "https://localhost:7188";
    public string ImageStoragePath { get; set; } = "App_Data/images";
    /// <summary>sqlite | sqlserver</summary>
    public string DatabaseProvider { get; set; } = "sqlite";
}

public interface IProductSyncService
{
    Task<SyncBatchResponse> UpsertProductsAsync(IReadOnlyList<ProductItemDto> items, bool replace, CancellationToken ct);
    Task<SyncBatchResponse> UpsertInventoryAsync(IReadOnlyList<ProductItemDto> items, CancellationToken ct);
    Task<SyncBatchResponse> UpsertPricesAsync(IReadOnlyList<ProductItemDto> items, CancellationToken ct);
}

public sealed class ProductSyncService : IProductSyncService
{
    private readonly SyncDbContext _db;
    private readonly ILogger<ProductSyncService> _logger;

    public ProductSyncService(SyncDbContext db, ILogger<ProductSyncService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SyncBatchResponse> UpsertProductsAsync(IReadOnlyList<ProductItemDto> items, bool replace, CancellationToken ct)
    {
        var response = new SyncBatchResponse();
        if (replace && items.Count > 0)
        {
            var stores = items.Select(i => i.StoreCode).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var store in stores)
            {
                var existing = await _db.Products.Where(p => p.StoreCode == store).ToListAsync(ct).ConfigureAwait(false);
                _db.Products.RemoveRange(existing);
            }
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        foreach (var item in items)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.Sku) || string.IsNullOrWhiteSpace(item.StoreCode))
                {
                    response.Failed++;
                    response.Errors.Add("Missing storeCode/sku");
                    continue;
                }

                var entity = await _db.Products
                    .FirstOrDefaultAsync(p => p.StoreCode == item.StoreCode && p.Sku == item.Sku, ct)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    entity = new Product { StoreCode = item.StoreCode.Trim(), Sku = item.Sku.Trim() };
                    ApplyFull(entity, item);
                    _db.Products.Add(entity);
                    response.Uploaded++;
                }
                else if (!string.IsNullOrWhiteSpace(item.ContentHash)
                         && string.Equals(entity.ContentHash, item.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    // unchanged
                }
                else
                {
                    ApplyFull(entity, item);
                    entity.UpdatedAtUtc = DateTime.UtcNow;
                    response.Updated++;
                }
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.Errors.Add($"{item.Sku}: {ex.Message}");
                _logger.LogWarning(ex, "Failed product upsert {Sku}", item.Sku);
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        response.Success = response.Failed == 0;
        response.Message = response.Success ? "ok" : "completed with errors";
        return response;
    }

    public async Task<SyncBatchResponse> UpsertInventoryAsync(IReadOnlyList<ProductItemDto> items, CancellationToken ct)
    {
        var response = new SyncBatchResponse();
        foreach (var item in items)
        {
            try
            {
                var entity = await EnsureProductAsync(item, ct).ConfigureAwait(false);
                if (entity is null)
                {
                    response.Failed++;
                    response.Errors.Add($"{item.Sku}: missing identity");
                    continue;
                }

                var changed = entity.Quantity != item.Quantity;
                entity.Quantity = item.Quantity;
                if (!string.IsNullOrWhiteSpace(item.ContentHash))
                    entity.ContentHash = item.ContentHash;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                if (changed) response.Updated++; else response.Uploaded++;
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.Errors.Add($"{item.Sku}: {ex.Message}");
            }
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        response.Success = response.Failed == 0;
        response.Message = "ok";
        return response;
    }

    public async Task<SyncBatchResponse> UpsertPricesAsync(IReadOnlyList<ProductItemDto> items, CancellationToken ct)
    {
        var response = new SyncBatchResponse();
        foreach (var item in items)
        {
            try
            {
                var entity = await EnsureProductAsync(item, ct).ConfigureAwait(false);
                if (entity is null)
                {
                    response.Failed++;
                    response.Errors.Add($"{item.Sku}: missing identity");
                    continue;
                }

                entity.Cost = item.Cost;
                entity.Price = item.Price;
                entity.SalePrice = item.SalePrice;
                if (!string.IsNullOrWhiteSpace(item.ContentHash))
                    entity.ContentHash = item.ContentHash;
                entity.UpdatedAtUtc = DateTime.UtcNow;
                response.Updated++;
            }
            catch (Exception ex)
            {
                response.Failed++;
                response.Errors.Add($"{item.Sku}: {ex.Message}");
            }
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        response.Success = response.Failed == 0;
        response.Message = "ok";
        return response;
    }

    private async Task<Product?> EnsureProductAsync(ProductItemDto item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Sku) || string.IsNullOrWhiteSpace(item.StoreCode))
            return null;

        var entity = await _db.Products
            .FirstOrDefaultAsync(p => p.StoreCode == item.StoreCode && p.Sku == item.Sku, ct)
            .ConfigureAwait(false);
        if (entity is not null)
            return entity;

        entity = new Product { StoreCode = item.StoreCode.Trim(), Sku = item.Sku.Trim() };
        ApplyFull(entity, item);
        _db.Products.Add(entity);
        return entity;
    }

    private static void ApplyFull(Product entity, ProductItemDto item)
    {
        entity.ItemNumber = item.ItemNumber;
        entity.Name = item.Name;
        entity.Description = item.Description;
        entity.Brand = item.Brand;
        entity.Category = item.Category;
        entity.Vendor = item.Vendor;
        entity.Cost = item.Cost;
        entity.Price = item.Price;
        entity.SalePrice = item.SalePrice;
        entity.Quantity = item.Quantity;
        entity.Barcode = item.Barcode;
        entity.Metal = item.Metal;
        entity.Stone = item.Stone;
        entity.Shape = item.Shape;
        entity.Weight = item.Weight;
        entity.ContentHash = item.ContentHash;
        entity.ImageUrlsJson = item.ImageUrls is { Length: > 0 }
            ? JsonSerializer.Serialize(item.ImageUrls)
            : entity.ImageUrlsJson;
    }
}
