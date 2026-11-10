using System.Text.Json;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Sync.Engine;

public sealed class SyncEngine : ISyncEngine
{
    private readonly IProductRepository _products;
    private readonly IRemoteApiClient _api;
    private readonly ISyncStateStore _state;
    private readonly IImageSyncService _images;
    private readonly ISettingsService _settings;
    private readonly ILogger<SyncEngine> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private CancellationTokenSource? _cts;

    public SyncEngine(
        IProductRepository products,
        IRemoteApiClient api,
        ISyncStateStore state,
        IImageSyncService images,
        ISettingsService settings,
        ILogger<SyncEngine> logger)
    {
        _products = products;
        _api = api;
        _state = state;
        _images = images;
        _settings = settings;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }
    public SyncRunResult? LastResult { get; private set; }
    public event EventHandler<SyncRunResult>? ProgressChanged;

    public async Task StopAsync()
    {
        _cts?.Cancel();
        await Task.CompletedTask;
    }

    public async Task<SyncRunResult> RetryFailedAsync(string? storeCode, CancellationToken cancellationToken = default)
    {
        var pending = await _state.DequeuePendingAsync(500, cancellationToken).ConfigureAwait(false);
        var failed = pending.Where(j => j.Status == SyncJobStatus.Failed
                                        && (storeCode is null || j.StoreCode == storeCode)).ToList();
        foreach (var job in failed)
        {
            job.Status = SyncJobStatus.Pending;
            job.LastError = null;
            await _state.UpdateJobAsync(job, cancellationToken).ConfigureAwait(false);
        }

        return await RunAsync(SyncMode.Incremental, _settings.Current.Sync.Channels, storeCode, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SyncRunResult> RunAsync(SyncMode mode, SyncChannels channels, string? storeCode, CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A sync is already running.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        IsRunning = true;

        var result = new SyncRunResult
        {
            StoreCode = storeCode ?? _settings.Current.Sync.SelectedStoreCode ?? "*",
            Mode = mode,
            StartedAtUtc = DateTime.UtcNow
        };
        LastResult = result;
        Raise(result);

        try
        {
            await _state.AddLogAsync(new SyncLogEntry
            {
                Level = LogLevelKind.Information,
                StoreCode = result.StoreCode,
                Operation = "SyncStarted",
                Message = $"Sync started ({mode}, channels={channels})"
            }, token).ConfigureAwait(false);

            var batchSize = Math.Clamp(_settings.Current.Sync.BatchSize, 1, 2000);
            var skip = 0;
            DateTime? modifiedSince = null;
            if (mode == SyncMode.Incremental)
                modifiedSince = await _state.GetLastSuccessfulSyncAsync(storeCode, token).ConfigureAwait(false);

            while (!token.IsCancellationRequested)
            {
                IReadOnlyList<ProductRecord> batch;
                try
                {
                    batch = await _products.GetProductsAsync(storeCode, modifiedSince, batchSize, skip, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add(ex.Message);
                    await LogError(result.StoreCode, null, "ReadProducts", ex.Message, token).ConfigureAwait(false);
                    break;
                }

                if (batch.Count == 0)
                    break;

                result.TotalFound += batch.Count;
                skip += batch.Count;

                var toSend = new List<ProductRecord>();
                foreach (var product in batch)
                {
                    if (mode == SyncMode.Incremental)
                    {
                        var lastHash = await _state.GetLastHashAsync(product.StoreCode, product.Sku, token).ConfigureAwait(false);
                        if (string.Equals(lastHash, product.ContentHash, StringComparison.OrdinalIgnoreCase))
                        {
                            result.SkippedUnchanged++;
                            continue;
                        }
                    }

                    toSend.Add(product);
                    await _state.EnqueueAsync(new SyncJob
                    {
                        StoreCode = product.StoreCode,
                        Sku = product.Sku,
                        Channel = SyncChannel.Products,
                        Status = SyncJobStatus.Pending,
                        PayloadJson = JsonSerializer.Serialize(product),
                        ContentHash = product.ContentHash,
                        IdempotencyKey = $"{product.StoreCode}:{product.Sku}:{product.ContentHash}"
                    }, token).ConfigureAwait(false);
                }

                if (toSend.Count > 0 && channels.HasFlag(SyncChannels.Products))
                    await SendProductsAsync(toSend, result, token).ConfigureAwait(false);

                if (toSend.Count > 0 && channels.HasFlag(SyncChannels.Inventory))
                    await SendInventoryAsync(toSend, result, token).ConfigureAwait(false);

                if (toSend.Count > 0 && channels.HasFlag(SyncChannels.Prices))
                    await SendPricesAsync(toSend, result, token).ConfigureAwait(false);

                if (channels.HasFlag(SyncChannels.Images))
                {
                    var imageSummary = await _images.SyncImagesAsync(batch, token).ConfigureAwait(false);
                    result.ImagesUploaded += imageSummary.Uploaded;
                    result.ImagesFailed += imageSummary.Failed;
                    result.ImagesMissing += imageSummary.Missing;
                    result.ImagesPending += imageSummary.Pending;
                }

                Raise(result);

                if (batch.Count < batchSize)
                    break;
            }

            // Flush durable queue for offline recovery
            await FlushPendingQueueAsync(result, channels, token).ConfigureAwait(false);

            if (!token.IsCancellationRequested && result.Failed == 0)
                await _state.SetLastSuccessfulSyncAsync(result.StoreCode, DateTime.UtcNow, token).ConfigureAwait(false);

            try
            {
                await _api.CompleteSyncAsync(result.RunId, result, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "sync/complete failed (non-fatal)");
            }

            result.CompletedAtUtc = DateTime.UtcNow;
            result.WasCancelled = token.IsCancellationRequested;
            await _state.AddLogAsync(new SyncLogEntry
            {
                Level = LogLevelKind.Information,
                StoreCode = result.StoreCode,
                Operation = "SyncCompleted",
                Message = $"uploaded={result.Uploaded} updated={result.Updated} failed={result.Failed} skipped={result.SkippedUnchanged}"
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
            LastResult = result;
            Raise(result);
            _runLock.Release();
        }

        return result;
    }

    private async Task FlushPendingQueueAsync(SyncRunResult result, SyncChannels channels, CancellationToken token)
    {
        var pending = await _state.DequeuePendingAsync(_settings.Current.Sync.BatchSize, token).ConfigureAwait(false);
        if (pending.Count == 0 || !channels.HasFlag(SyncChannels.Products))
            return;

        var products = new List<ProductRecord>();
        foreach (var job in pending.Where(j => j.Channel == SyncChannel.Products && j.Status is SyncJobStatus.Pending or SyncJobStatus.Failed))
        {
            try
            {
                var product = JsonSerializer.Deserialize<ProductRecord>(job.PayloadJson);
                if (product is not null)
                    products.Add(product);
            }
            catch
            {
                // skip corrupt payload
            }
        }

        if (products.Count > 0)
            await SendProductsAsync(products, result, token).ConfigureAwait(false);
    }

    private async Task SendProductsAsync(List<ProductRecord> products, SyncRunResult result, CancellationToken token)
    {
        var key = $"products:{result.RunId}:{products.Count}:{products[0].ContentHash}";
        try
        {
            var response = await _api.SyncProductsAsync(products, key, token).ConfigureAwait(false);
            if (response.Success)
            {
                result.Uploaded += response.Uploaded;
                result.Updated += response.Updated;
                foreach (var p in products)
                    await _state.SetLastHashAsync(p.StoreCode, p.Sku, p.ContentHash, token).ConfigureAwait(false);
            }
            else
            {
                result.Failed += Math.Max(response.Failed, products.Count);
                result.Errors.AddRange(response.Errors);
                await LogError(result.StoreCode, null, "ProductsSync", response.Message ?? "failed", token, "/api/v1/products/sync", response.Message).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            result.Failed += products.Count;
            result.Errors.Add(ex.Message);
            _logger.LogError(ex, "Product sync batch failed; leaving jobs pending for retry");
            await LogError(result.StoreCode, null, "ProductsSync", ex.Message, token, "/api/v1/products/sync").ConfigureAwait(false);
        }
    }

    private async Task SendInventoryAsync(List<ProductRecord> products, SyncRunResult result, CancellationToken token)
    {
        try
        {
            var response = await _api.SyncInventoryAsync(products, $"inv:{result.RunId}:{products[0].ContentHash}", token).ConfigureAwait(false);
            if (!response.Success)
            {
                result.Failed += response.Failed;
                result.Errors.AddRange(response.Errors);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            await LogError(result.StoreCode, null, "InventorySync", ex.Message, token, "/api/v1/inventory/sync").ConfigureAwait(false);
        }
    }

    private async Task SendPricesAsync(List<ProductRecord> products, SyncRunResult result, CancellationToken token)
    {
        try
        {
            var response = await _api.SyncPricesAsync(products, $"price:{result.RunId}:{products[0].ContentHash}", token).ConfigureAwait(false);
            if (!response.Success)
            {
                result.Failed += response.Failed;
                result.Errors.AddRange(response.Errors);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex.Message);
            await LogError(result.StoreCode, null, "PriceSync", ex.Message, token, "/api/v1/prices/sync").ConfigureAwait(false);
        }
    }

    private Task LogError(string? store, string? sku, string op, string error, CancellationToken token, string? endpoint = null, string? response = null)
        => _state.AddLogAsync(new SyncLogEntry
        {
            Level = LogLevelKind.Error,
            StoreCode = store,
            Sku = sku,
            Operation = op,
            ApiEndpoint = endpoint,
            Response = response,
            Error = error,
            Message = error
        }, token);

    private void Raise(SyncRunResult result) => ProgressChanged?.Invoke(this, result);
}
