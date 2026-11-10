using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VallianiDataSync.Api.Data;
using VallianiDataSync.Api.Models;

namespace VallianiDataSync.Api.Services;

public interface IImageStorageService
{
    Task<ImageUploadResponse> SaveAsync(string storeCode, string sku, string contentHash, IFormFile file, CancellationToken ct);
}

public sealed class ImageStorageService : IImageStorageService
{
    private readonly SyncDbContext _db;
    private readonly ApiOptions _options;
    private readonly IWebHostEnvironment _env;

    public ImageStorageService(SyncDbContext db, IOptions<ApiOptions> options, IWebHostEnvironment env)
    {
        _db = db;
        _options = options.Value;
        _env = env;
    }

    public async Task<ImageUploadResponse> SaveAsync(string storeCode, string sku, string contentHash, IFormFile file, CancellationToken ct)
    {
        storeCode = storeCode.Trim();
        sku = sku.Trim();
        contentHash = contentHash.Trim();

        var existing = await _db.ProductImages
            .FirstOrDefaultAsync(i => i.StoreCode == storeCode && i.Sku == sku && i.ContentHash == contentHash, ct)
            .ConfigureAwait(false);
        if (existing is not null)
            return new ImageUploadResponse { Url = existing.Url, Message = "unchanged" };

        var root = Path.IsPathRooted(_options.ImageStoragePath)
            ? _options.ImageStoragePath
            : Path.Combine(_env.ContentRootPath, _options.ImageStoragePath);
        var folder = Path.Combine(root, Sanitize(storeCode));
        Directory.CreateDirectory(folder);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
        var fileName = $"{Sanitize(sku)}_{contentHash[..Math.Min(12, contentHash.Length)]}{ext.ToLowerInvariant()}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct).ConfigureAwait(false);

        // Verify hash if client sent one; recompute for safety when empty
        if (string.IsNullOrWhiteSpace(contentHash) || contentHash.Length < 8)
        {
            await using var read = File.OpenRead(fullPath);
            contentHash = Convert.ToHexString(await SHA256.HashDataAsync(read, ct).ConfigureAwait(false));
            fileName = $"{Sanitize(sku)}_{contentHash[..12]}{ext.ToLowerInvariant()}";
            var renamed = Path.Combine(folder, fileName);
            if (!string.Equals(fullPath, renamed, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(fullPath, renamed, overwrite: true);
                fullPath = renamed;
            }
        }

        var relative = Path.Combine(Sanitize(storeCode), fileName).Replace('\\', '/');
        var url = $"{_options.PublicBaseUrl.TrimEnd('/')}/files/{relative}";

        var entity = new ProductImage
        {
            StoreCode = storeCode,
            Sku = sku,
            ContentHash = contentHash,
            FileName = fileName,
            RelativePath = relative,
            Url = url,
            SizeBytes = file.Length,
            UploadedAtUtc = DateTime.UtcNow
        };
        _db.ProductImages.Add(entity);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.StoreCode == storeCode && p.Sku == sku, ct).ConfigureAwait(false);
        if (product is not null)
        {
            product.ImageUrlsJson = JsonSerialize(url);
            product.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ImageUploadResponse { Url = url, Message = "uploaded" };
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
                sb.Append(ch);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }

    private static string JsonSerialize(string url) => System.Text.Json.JsonSerializer.Serialize(new[] { url });
}
