using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Core.Security;

namespace VallianiDataSync.Sync.Images;

public sealed class ImageSyncService : IImageSyncService
{
    private readonly ISettingsService _settings;
    private readonly IRemoteApiClient _api;
    private readonly ISyncStateStore _state;
    private readonly ILogger<ImageSyncService> _logger;

    public ImageSyncService(
        ISettingsService settings,
        IRemoteApiClient api,
        ISyncStateStore state,
        ILogger<ImageSyncService> logger)
    {
        _settings = settings;
        _api = api;
        _state = state;
        _logger = logger;
    }

    public Task<ImageSyncSummary> GetSummaryAsync(string? storeCode, CancellationToken cancellationToken = default)
        => _state.GetImageSummaryAsync(storeCode, cancellationToken);

    public async Task<ImageSyncSummary> SyncImagesAsync(IEnumerable<ProductRecord> products, CancellationToken cancellationToken = default)
    {
        var options = _settings.Current.Images;
        var roots = options.RootFolders
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(PathSanitizer.SanitizePath)
            .ToList();

        var summary = new ImageSyncSummary();

        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            summary.Total++;

            var resolved = ResolveLocalPath(product, roots, options.AllowedExtensions);
            if (resolved is null)
            {
                summary.Missing++;
                var missing = new ImageSyncItem
                {
                    StoreCode = product.StoreCode,
                    Sku = product.Sku,
                    LocalPath = product.ImagePath ?? string.Empty,
                    Status = ImageSyncItemStatus.Missing,
                    Error = "Local image not found"
                };
                await _state.SaveImageStateAsync(missing, cancellationToken).ConfigureAwait(false);
                await _state.AddLogAsync(new SyncLogEntry
                {
                    Level = LogLevelKind.Warning,
                    StoreCode = product.StoreCode,
                    Sku = product.Sku,
                    Operation = "ImageSync",
                    Message = "Missing local image",
                    Error = product.ImagePath
                }, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var fileInfo = new FileInfo(resolved);
            string hash;
            await using (var stream = fileInfo.OpenRead())
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }

            var previous = await _state.GetImageStateAsync(product.StoreCode, product.Sku, cancellationToken).ConfigureAwait(false);
            if (previous is not null
                && previous.Status == ImageSyncItemStatus.Uploaded
                && ((options.UseChecksum && string.Equals(previous.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                    || (options.UseModifiedTimestamp && previous.LocalModifiedUtc == fileInfo.LastWriteTimeUtc)))
            {
                summary.Uploaded++;
                continue;
            }

            try
            {
                var response = await _api.UploadImageAsync(product.StoreCode, product.Sku, resolved, hash, cancellationToken).ConfigureAwait(false);
                var item = new ImageSyncItem
                {
                    StoreCode = product.StoreCode,
                    Sku = product.Sku,
                    LocalPath = resolved,
                    ContentHash = hash,
                    LocalModifiedUtc = fileInfo.LastWriteTimeUtc,
                    Status = response.Success ? ImageSyncItemStatus.Uploaded : ImageSyncItemStatus.Failed,
                    RemoteUrl = response.Url,
                    Error = response.Success ? null : response.Message
                };
                await _state.SaveImageStateAsync(item, cancellationToken).ConfigureAwait(false);

                if (response.Success)
                {
                    summary.Uploaded++;
                }
                else
                {
                    summary.Failed++;
                    _logger.LogWarning("Image upload failed for {Sku}: {Message}", product.Sku, response.Message);
                }
            }
            catch (Exception ex)
            {
                summary.Failed++;
                _logger.LogError(ex, "Image upload exception for {Sku}", product.Sku);
                await _state.SaveImageStateAsync(new ImageSyncItem
                {
                    StoreCode = product.StoreCode,
                    Sku = product.Sku,
                    LocalPath = resolved,
                    ContentHash = hash,
                    LocalModifiedUtc = fileInfo.LastWriteTimeUtc,
                    Status = ImageSyncItemStatus.Failed,
                    Error = ex.Message
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        summary.Pending = Math.Max(0, summary.Total - summary.Uploaded - summary.Failed - summary.Missing);
        return summary;
    }

    private static string? ResolveLocalPath(ProductRecord product, List<string> roots, string[] extensions)
    {
        if (!string.IsNullOrWhiteSpace(product.ImagePath))
        {
            var candidate = product.ImagePath.Trim().Trim('"');
            if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                return PathSanitizer.SanitizePath(candidate);

            foreach (var root in roots)
            {
                var combined = Path.Combine(root, candidate);
                if (File.Exists(combined) && PathSanitizer.IsUnderRoot(combined, root))
                    return combined;

                foreach (var ext in extensions)
                {
                    var withExt = Path.HasExtension(combined) ? combined : combined + ext;
                    if (File.Exists(withExt) && PathSanitizer.IsUnderRoot(withExt, root))
                        return withExt;
                }
            }
        }

        // Fallback: look for SKU.* under roots
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var ext in extensions)
            {
                var path = Path.Combine(root, product.Sku + ext);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }
}
