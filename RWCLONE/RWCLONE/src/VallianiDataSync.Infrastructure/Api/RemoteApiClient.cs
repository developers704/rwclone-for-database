using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Core.Options;

namespace VallianiDataSync.Infrastructure.Api;

public sealed class RemoteApiClient : IRemoteApiClient
{
    private readonly HttpClient _http;
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly ILogger<RemoteApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RemoteApiClient(
        HttpClient http,
        ISettingsService settings,
        ICredentialStore credentials,
        ILogger<RemoteApiClient> logger)
    {
        _http = http;
        _settings = settings;
        _credentials = credentials;
        _logger = logger;
    }

    public async Task<(bool Ok, string Message)> ValidateAsync(CancellationToken cancellationToken = default)
    {
        EnsureHttps();
        try
        {
            using var request = CreateRequest(HttpMethod.Post, "/api/v1/auth/validate");
            using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}: {Truncate(body)}");
            return (true, "API validated successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "API validation failed");
            return (false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<StoreRecord>> GetStoresAsync(CancellationToken cancellationToken = default)
    {
        EnsureHttps();
        using var request = CreateRequest(HttpMethod.Get, "/api/v1/stores");
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<StoreDto>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return (payload ?? new List<StoreDto>()).Select(s => new StoreRecord
        {
            StoreCode = s.Code ?? string.Empty,
            StoreName = s.Name ?? s.Code ?? string.Empty,
            RemoteStoreId = s.Id,
            IsActive = s.IsActive ?? true
        }).ToList();
    }

    public Task<ApiSyncResponse> SyncProductsAsync(IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken = default)
        => SyncBatchAsync("/api/v1/products/sync", products, idempotencyKey, cancellationToken);

    public Task<ApiSyncResponse> SyncInventoryAsync(IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken = default)
        => SyncBatchAsync("/api/v1/inventory/sync", products, idempotencyKey, cancellationToken);

    public Task<ApiSyncResponse> SyncPricesAsync(IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken = default)
        => SyncBatchAsync("/api/v1/prices/sync", products, idempotencyKey, cancellationToken);

    public async Task<ApiImageUploadResponse> UploadImageAsync(string storeCode, string sku, string filePath, string contentHash, CancellationToken cancellationToken = default)
    {
        EnsureHttps();
        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(storeCode), "storeCode");
        content.Add(new StringContent(sku), "sku");
        content.Add(new StringContent(contentHash), "contentHash");
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessMime(filePath));
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        using var request = CreateRequest(HttpMethod.Post, "/api/v1/images/upload");
        request.Content = content;
        request.Headers.TryAddWithoutValidation("Idempotency-Key", $"{storeCode}:{sku}:{contentHash}");

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new ApiImageUploadResponse { Success = false, Message = Truncate(body) };

        var dto = JsonSerializer.Deserialize<ImageUploadDto>(body, JsonOptions);
        return new ApiImageUploadResponse { Success = true, Url = dto?.Url, Message = dto?.Message };
    }

    public async Task CompleteSyncAsync(Guid runId, SyncRunResult result, CancellationToken cancellationToken = default)
    {
        EnsureHttps();
        using var request = CreateRequest(HttpMethod.Post, "/api/v1/sync/complete");
        request.Content = JsonContent.Create(new
        {
            runId,
            result.StoreCode,
            result.Uploaded,
            result.Updated,
            result.Failed,
            completedAtUtc = result.CompletedAtUtc
        }, options: JsonOptions);
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ApiSyncStatus?> GetSyncStatusAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        EnsureHttps();
        using var request = CreateRequest(HttpMethod.Get, $"/api/v1/sync/status?runId={runId}");
        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadFromJsonAsync<ApiSyncStatus>(JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApiSyncResponse> SyncBatchAsync(string path, IReadOnlyList<ProductRecord> products, string idempotencyKey, CancellationToken cancellationToken)
    {
        EnsureHttps();
        var dto = products.Select(ToDto).ToList();
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new { items = dto }, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ApiSyncResponse
            {
                Success = false,
                Message = Truncate(body),
                Failed = products.Count,
                Errors = { Truncate(body) }
            };
        }

        var parsed = JsonSerializer.Deserialize<SyncResponseDto>(body, JsonOptions) ?? new SyncResponseDto();
        return new ApiSyncResponse
        {
            Success = parsed.Success ?? true,
            Message = parsed.Message,
            Uploaded = parsed.Uploaded ?? products.Count,
            Updated = parsed.Updated ?? 0,
            Failed = parsed.Failed ?? 0,
            Errors = parsed.Errors ?? new List<string>()
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var api = _settings.Current.Api;
        var baseUrl = api.BaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{relativePath}");
        var apiKey = _credentials.GetSecret(CredentialKeys.ApiKey);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        if (!string.IsNullOrWhiteSpace(api.ClientId))
            request.Headers.TryAddWithoutValidation("X-Client-Id", api.ClientId);
        if (!string.IsNullOrWhiteSpace(api.StoreId))
            request.Headers.TryAddWithoutValidation("X-Store-Id", api.StoreId);
        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage prototype, CancellationToken cancellationToken)
    {
        var retries = Math.Max(0, _settings.Current.Api.RetryCount);
        Exception? last = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            using var request = await CloneAsync(prototype).ConfigureAwait(false);
            try
            {
                var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500 && response.StatusCode != HttpStatusCode.RequestTimeout && response.StatusCode != (HttpStatusCode)429)
                    return response;

                _logger.LogWarning("API {Method} {Url} returned {Status}; attempt {Attempt}/{Max}",
                    request.Method, request.RequestUri, (int)response.StatusCode, attempt + 1, retries + 1);
                response.Dispose();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                _logger.LogWarning(ex, "API call failed; attempt {Attempt}/{Max}", attempt + 1, retries + 1);
            }

            if (attempt < retries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw last ?? new HttpRequestException("API request failed after retries.");
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private void EnsureHttps()
    {
        var api = _settings.Current.Api;
        if (!api.RequireHttps)
            return;
        if (!Uri.TryCreate(api.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("API Base URL must use HTTPS.");
    }

    private static object ToDto(ProductRecord p) => new
    {
        storeCode = p.StoreCode,
        sku = p.Sku,
        itemNumber = p.ItemNumber,
        name = p.Name,
        description = p.Description,
        brand = p.Brand,
        category = p.Category,
        vendor = p.Vendor,
        cost = p.Cost,
        price = p.RetailPrice,
        salePrice = p.SalePrice,
        quantity = p.Quantity,
        barcode = p.Barcode,
        metal = p.Metal,
        stone = p.Stone,
        shape = p.Shape,
        weight = p.Weight,
        imageUrls = string.IsNullOrWhiteSpace(p.ImagePath) ? Array.Empty<string>() : new[] { p.ImagePath },
        contentHash = p.ContentHash
    };

    private static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };

    private static string Truncate(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 500 ? value : value[..500];

    private sealed class StoreDto
    {
        public string? Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public bool? IsActive { get; set; }
    }

    private sealed class SyncResponseDto
    {
        public bool? Success { get; set; }
        public string? Message { get; set; }
        public int? Uploaded { get; set; }
        public int? Updated { get; set; }
        public int? Failed { get; set; }
        public List<string>? Errors { get; set; }
    }

    private sealed class ImageUploadDto
    {
        public string? Url { get; set; }
        public string? Message { get; set; }
    }
}
