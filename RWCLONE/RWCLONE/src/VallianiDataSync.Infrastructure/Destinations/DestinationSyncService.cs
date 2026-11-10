using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Infrastructure.Destinations;

namespace VallianiDataSync.Infrastructure.Destinations;

public sealed class DestinationSyncService : IDestinationSyncService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ISettingsService _settings;
    private readonly IProductRepository _products;
    private readonly ICredentialStore _credentials;
    private readonly ISyncStateStore _state;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DestinationSyncService> _logger;

    public DestinationSyncService(
        ISettingsService settings,
        IProductRepository products,
        ICredentialStore credentials,
        ISyncStateStore state,
        IHttpClientFactory httpClientFactory,
        ILogger<DestinationSyncService> logger)
    {
        _settings = settings;
        _products = products;
        _credentials = credentials;
        _state = state;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task RunDueProjectsAsync(CancellationToken cancellationToken = default)
    {
        var due = _settings.Current.Destinations
            .Where(d => d.Enabled && d.AutoSyncEnabled)
            .Where(d => d.NextSyncLocal is null || d.NextSyncLocal <= DateTime.Now)
            .ToList();

        foreach (var project in due)
            await SyncProjectAsync(project.Id, fullSync: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<(bool Ok, string Message, int Sent, int Skipped)> SyncProjectAsync(
        string projectId,
        bool fullSync,
        CancellationToken cancellationToken = default)
    {
        var project = _settings.Current.Destinations.FirstOrDefault(d => d.Id == projectId);
        if (project is null)
            return (false, "Project not found.", 0, 0);
        if (!project.Enabled)
            return (false, "Project is disabled. Enable it first.", 0, 0);
        if (string.IsNullOrWhiteSpace(project.ApiUrl))
            return (false, "API URL is required.", 0, 0);

        var mapping = _settings.Current.Mapping;
        var multiTable = mapping.SyncAllTables || mapping.SelectedTables.Count > 0;
        if (multiTable)
            return await SyncAllTablesAsync(project, cancellationToken).ConfigureAwait(false);

        if (project.FormatFields.Count == 0)
            return (false, "Add format fields, or enable Sync all tables on Data Mapping.", 0, 0);

        return await SyncProductsAsync(project, fullSync, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(bool Ok, string Message, int Sent, int Skipped)> SyncAllTablesAsync(
        DestinationProject project,
        CancellationToken cancellationToken)
    {
        var mapping = _settings.Current.Mapping;
        // SyncAllTables=true → always rediscover every user table (thousands OK). SelectedTables only when SyncAllTables off.
        List<string> tables;
        if (mapping.SyncAllTables)
            tables = (await _products.ListTablesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        else
            tables = mapping.SelectedTables
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (tables.Count == 0)
            return (false, "No tables found. Check Database connection, then Data Mapping → enable Sync all tables → Save.", 0, 0);

        var batchSize = Math.Clamp(_settings.Current.Sync.BatchSize, 50, 500);
        var totalSent = 0;
        var okTables = 0;
        var failTables = 0;
        var errors = new List<string>();

        _logger.LogInformation("Multi-table sync starting for {Project}: {Count} tables", project.Name, tables.Count);
        project.LastSyncMessage = $"Syncing 0/{tables.Count} tables…";
        await SaveSettingsAsync().ConfigureAwait(false);

        for (var i = 0; i < tables.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = tables[i];
            try
            {
                var skip = 0;
                var tableSent = 0;
                while (true)
                {
                    var rows = await _products.ReadTableRowsAsync(table, batchSize, skip, cancellationToken).ConfigureAwait(false);
                    if (rows.Count == 0)
                        break;

                    await PushTableAsync(project, table, rows.ToList(), cancellationToken).ConfigureAwait(false);
                    tableSent += rows.Count;
                    totalSent += rows.Count;
                    skip += rows.Count;
                    if (rows.Count < batchSize)
                        break;
                }

                okTables++;
            }
            catch (Exception ex)
            {
                failTables++;
                _logger.LogWarning(ex, "Table sync failed: {Table}", table);
                if (errors.Count < 20)
                    errors.Add($"{table}: {Truncate(ex.Message, 120)}");
            }

            // Progress every 25 tables so UI/logs stay usable with ~2600 tables
            if ((i + 1) % 25 == 0 || i + 1 == tables.Count)
            {
                project.LastSyncMessage = $"Syncing {i + 1}/{tables.Count} (ok={okTables}, fail={failTables}, rows={totalSent})…";
                await SaveSettingsAsync().ConfigureAwait(false);
                await _state.AddLogAsync(new SyncLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Level = LogLevelKind.Information,
                    Operation = "DestinationTableSync",
                    Message = $"{project.Name}: progress {i + 1}/{tables.Count} ok={okTables} fail={failTables} rows={totalSent}",
                    StoreCode = project.Id
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        var summary = $"DONE tables={tables.Count} ok={okTables} fail={failTables} rows={totalSent}";
        if (errors.Count > 0)
            summary += " | errors: " + string.Join("; ", errors);
        UpdateSchedule(project, summary);
        await _state.AddLogAsync(new SyncLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = failTables > 0 ? LogLevelKind.Warning : LogLevelKind.Information,
            Operation = "DestinationTableSync",
            Message = $"{project.Name}: {summary}",
            StoreCode = project.Id
        }, cancellationToken).ConfigureAwait(false);
        await SaveSettingsAsync().ConfigureAwait(false);

        var ok = okTables > 0;
        return (ok,
            $"Synced {okTables}/{tables.Count} table(s), {totalSent} row(s)" + (failTables > 0 ? $", {failTables} failed." : "."),
            totalSent,
            failTables);
    }

    private async Task<(bool Ok, string Message, int Sent, int Skipped)> SyncProductsAsync(
        DestinationProject project,
        bool fullSync,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_settings.Current.Sync.BatchSize, 50, 1000);
        var skip = 0;
        var sent = 0;
        var skipped = 0;
        var payloadRows = new List<Dictionary<string, object?>>();
        var hashKeys = new List<(string Store, string Sku, string Hash)>();

        try
        {
            while (true)
            {
                var batch = await _products.GetProductsAsync(
                    _settings.Current.Sync.SelectedStoreCode,
                    modifiedSinceUtc: null,
                    batchSize,
                    skip,
                    cancellationToken).ConfigureAwait(false);
                if (batch.Count == 0)
                    break;

                foreach (var product in batch)
                {
                    var destKey = DestStateKey(project.Id);
                    var last = await _state.GetLastHashAsync(destKey, product.Sku, cancellationToken).ConfigureAwait(false);
                    if (!fullSync && string.Equals(last, product.ContentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    payloadRows.Add(ProductFormatExporter.MapProduct(product, project.FormatFields));
                    hashKeys.Add((destKey, product.Sku, product.ContentHash));
                }

                skip += batch.Count;
                if (batch.Count < batchSize)
                    break;
            }

            if (payloadRows.Count == 0)
            {
                UpdateSchedule(project, "No changes.");
                await SaveSettingsAsync().ConfigureAwait(false);
                return (true, "No changes to sync.", 0, skipped);
            }

            const int chunk = 200;
            for (var i = 0; i < payloadRows.Count; i += chunk)
            {
                var slice = payloadRows.Skip(i).Take(chunk).ToList();
                var hashSlice = hashKeys.Skip(i).Take(chunk).ToList();
                await PushAsync(project, slice, cancellationToken).ConfigureAwait(false);
                foreach (var h in hashSlice)
                    await _state.SetLastHashAsync(h.Store, h.Sku, h.Hash, cancellationToken).ConfigureAwait(false);
                sent += slice.Count;
            }

            UpdateSchedule(project, $"OK sent={sent} skipped={skipped}");
            await _state.AddLogAsync(new SyncLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Level = LogLevelKind.Information,
                Operation = "DestinationSync",
                Message = $"{project.Name}: sent {sent}, skipped {skipped}",
                StoreCode = project.Id
            }, cancellationToken).ConfigureAwait(false);
            await SaveSettingsAsync().ConfigureAwait(false);
            return (true, $"Synced {sent} row(s), skipped {skipped}.", sent, skipped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Destination sync failed for {Project}", project.Name);
            project.LastSyncMessage = ex.Message;
            await SaveSettingsAsync().ConfigureAwait(false);
            await _state.AddLogAsync(new SyncLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Level = LogLevelKind.Error,
                Operation = "DestinationSync",
                Message = $"{project.Name}: {ex.Message}",
                Error = ex.Message,
                StoreCode = project.Id
            }, cancellationToken).ConfigureAwait(false);
            return (false, ex.Message, sent, skipped);
        }
    }

    private async Task PushTableAsync(
        DestinationProject project,
        string tableName,
        List<Dictionary<string, object?>> rows,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(DestinationSyncService));
        using var request = new HttpRequestMessage(new HttpMethod(project.HttpMethod), project.ApiUrl);
        ApplyAuth(project, request);

        var payload = new { table = tableName, rowCount = rows.Count, rows };
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Table {tableName}: HTTP {(int)response.StatusCode}: {Truncate(body, 300)}");
    }

    private async Task PushAsync(DestinationProject project, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(DestinationSyncService));
        using var request = new HttpRequestMessage(new HttpMethod(project.HttpMethod), project.ApiUrl);
        ApplyAuth(project, request);

        if (string.Equals(project.BodyFormat, "Csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = ProductFormatExporter.ToCsv(rows, project.FormatFields);
            request.Content = new StringContent(csv, Encoding.UTF8, "text/csv");
        }
        else
        {
            var json = ProductFormatExporter.ToJsonArray(rows);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Truncate(body, 300)}");
    }

    private void ApplyAuth(DestinationProject project, HttpRequestMessage request)
    {
        var apiKey = _credentials.GetSecret(CredentialKeys.DestinationApiKey(project.Id));
        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(project.AuthHeaderName))
        {
            var value = (project.AuthHeaderPrefix ?? string.Empty) + apiKey;
            request.Headers.TryAddWithoutValidation(project.AuthHeaderName, value);
        }
    }

    private void UpdateSchedule(DestinationProject project, string message)
    {
        project.LastSyncUtc = DateTime.UtcNow;
        project.LastSyncMessage = message;
        var minutes = Math.Max(15, project.IntervalMinutes);
        project.NextSyncLocal = DateTime.Now.AddMinutes(minutes);
    }

    private Task SaveSettingsAsync()
    {
        _settings.Update(_ => { });
        return _settings.SaveAsync();
    }

    private static string DestStateKey(string projectId) => $"dest:{projectId}";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
