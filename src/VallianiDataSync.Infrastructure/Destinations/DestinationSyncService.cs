using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Infrastructure.Destinations;

namespace VallianiDataSync.Infrastructure.Destinations;

public sealed class DestinationSyncService : IDestinationSyncService
{
    // Keep dictionary KEYS as-is (Item #, Available Qty) — never camelCase them.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    private static readonly SemaphoreSlim SyncGate = new(1, 1);

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
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var project = _settings.Current.Destinations.FirstOrDefault(d => d.Id == projectId);
        if (project is null)
            return (false, "Project not found.", 0, 0);
        if (!project.Enabled)
            return (false, "Project is disabled. Enable it first.", 0, 0);
        if (string.IsNullOrWhiteSpace(project.ApiUrl))
            return (false, "API URL is required.", 0, 0);

        BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);

        if (!await SyncGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return (false, "Another destination sync is already running. Wait for it to finish.", 0, 0);
        try
        {
            return await SyncFixedSourcesAsync(project, fullSync, cancellationToken, progress).ConfigureAwait(false);
        }
        finally
        {
            SyncGate.Release();
        }
    }

    /// <summary>
    /// Sync only the fixed sources (sales / inventory / payments) selected on this Destination.
    /// Interval / Sync Now skips a source when its data hash is unchanged (Full Sync always pushes).
    /// </summary>
    private async Task<(bool Ok, string Message, int Sent, int Skipped)> SyncFixedSourcesAsync(
        DestinationProject project,
        bool fullSync,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        void Report(string msg)
        {
            project.LastSyncMessage = msg;
            progress?.Report(msg);
        }

        var mapping = _settings.Current.Mapping;
        BuiltInSyncQueries.EnsureDefaults(mapping);

        var tables = (project.SyncSources is { Count: > 0 }
                ? project.SyncSources
                : BuiltInSyncQueries.AllKeys.ToList())
            .Where(BuiltInSyncQueries.IsFixedSource)
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tables.Count == 0)
            return (false, "No sources selected. Destinations → tick sales / inventory / marketplace-products / payments → Save.", 0, 0);

        var anySingleFile = MarketplaceCatalogMapper.WantsSingleFilePush(project.ApiUrl)
            || tables.Any(BuiltInSyncQueries.WantsSingleFileSource);
        var batchSize = anySingleFile
            ? 50000
            : Math.Clamp(Math.Max(_settings.Current.Sync.BatchSize, 10000), 10000, 20000);
        var pushWorkers = anySingleFile ? 1 : 6;
        var totalSent = 0;
        var skippedTables = 0;
        var okTables = 0;
        var failTables = 0;
        var lines = new List<string>();
        var pushFormatLock = new FormatLock();

        _logger.LogInformation("Fixed-source sync for {Project}: {Count} source(s), fullSync={Full}, singleFile={One}, batch={Batch}",
            project.Name, tables.Count, fullSync, anySingleFile, batchSize);
        Report(anySingleFile
            ? $"Starting sync — one CSV file per marketplace source (no chunks)…"
            : $"Starting turbo sync — batch {batchSize:N0}, {pushWorkers} parallel pushes…");
        await SaveSettingsAsync().ConfigureAwait(false);

        for (var i = 0; i < tables.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var table = tables[i];
            var destName = ResolveDestinationTableName(mapping, table);
            var stateSku = $"src:{table}";
            var destKey = DestStateKey(project.Id);
            var singleFile = MarketplaceCatalogMapper.WantsSingleFilePush(project.ApiUrl)
                || BuiltInSyncQueries.WantsSingleFileSource(table);
            try
            {
                // Cheap check (seconds): server COUNT only — Sync Now / Full Sync still push always
                string? newHash = null;
                var isAutoOnly = !fullSync && progress is null;
                if (isAutoOnly)
                {
                    Report($"[{i + 1}/{tables.Count}] Checking {table} (fast count)…");
                    if (BuiltInSyncQueries.IsCaratoneDated(table)
                        && project.TryGetSaleReportDates(out var fpFrom, out var fpTo))
                    {
                        newHash = $"dates:{fpFrom:yyyy-MM-dd}:{fpTo:yyyy-MM-dd}";
                    }
                    else
                    {
                        newHash = await _products.GetTableFingerprintAsync(table, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    var last = await _state.GetLastHashAsync(destKey, stateSku, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(last)
                        && string.Equals(last, newHash, StringComparison.OrdinalIgnoreCase))
                    {
                        skippedTables++;
                        lines.Add($"• {table}: SKIPPED — row count unchanged ({newHash}). Full Sync to force.");
                        Report($"[{i + 1}/{tables.Count}] {table} unchanged — skipped ({newHash})");
                        continue;
                    }
                    Report($"[{i + 1}/{tables.Count}] {table} changed ({newHash}) — pushing…");
                }

                Report(singleFile
                    ? $"[{i + 1}/{tables.Count}] Loading {table} then one API upload…"
                    : $"[{i + 1}/{tables.Count}] Pushing {table} (parallel)…");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                pushFormatLock.ApiUpdated = 0;
                pushFormatLock.ApiCreated = 0;
                pushFormatLock.ApiFailed = 0;
                pushFormatLock.BatchesOk = 0;
                pushFormatLock.SawZeroUpdate = false;
                pushFormatLock.LastBody = null;
                pushFormatLock.Label = null;

                // Pin template SQL for marketplace inventory / products
                string? restoreSql = null;
                if (BuiltInSyncQueries.IsInventoryFamily(table))
                {
                    restoreSql = mapping.TableQueries.TryGetValue(table, out var prev) ? prev : null;
                    mapping.TableQueries[table] = table.Equals(BuiltInSyncQueries.InventoryTest, StringComparison.OrdinalIgnoreCase)
                        ? BuiltInSyncQueries.InventoryTestSql
                        : MarketplaceCatalogMapper.CatalogInventorySql;
                    Report(table.Equals(BuiltInSyncQueries.InventoryTest, StringComparison.OrdinalIgnoreCase)
                        ? $"[{i + 1}/{tables.Count}] marketplace-inv-test → TOP 10,000 template…"
                        : $"[{i + 1}/{tables.Count}] inventory → Sku/Barcode/Warehouse/Quantity template…");
                }
                else if (BuiltInSyncQueries.IsProductsFamily(table))
                {
                    restoreSql = mapping.TableQueries.TryGetValue(table, out var prev) ? prev : null;
                    mapping.TableQueries[table] = table.Equals(BuiltInSyncQueries.ProductsTest, StringComparison.OrdinalIgnoreCase)
                        ? BuiltInSyncQueries.ProductsTestSql
                        : BuiltInSyncQueries.ProductsSql;
                    Report(table.Equals(BuiltInSyncQueries.ProductsTest, StringComparison.OrdinalIgnoreCase)
                        ? $"[{i + 1}/{tables.Count}] marketplace-products-test → TOP 3,000 catalog…"
                        : $"[{i + 1}/{tables.Count}] marketplace-products → 35-col catalog template…");
                }
                else if (BuiltInSyncQueries.IsCaratoneDated(table))
                {
                    if (!project.TryGetSaleReportDates(out var saleFrom, out var saleTo))
                    {
                        failTables++;
                        lines.Add($"• {table}: FAILED — set date (single or range) then Save.");
                        Report($"[{i + 1}/{tables.Count}] {table} — missing dates");
                        continue;
                    }
                    restoreSql = mapping.TableQueries.TryGetValue(table, out var prev) ? prev : null;
                    mapping.TableQueries[table] = BuiltInSyncQueries.IsCaratonePayment(table)
                        ? CaratonePaymentSql.ApplyDates(CaratonePaymentSql.Template, saleFrom, saleTo)
                        : CaratoneSaleSql.ApplyDates(CaratoneSaleSql.Template, saleFrom, saleTo);
                    Report($"[{i + 1}/{tables.Count}] {table} → {saleFrom:yyyy-MM-dd}" +
                           (saleFrom == saleTo ? " (single day)" : $" … {saleTo:yyyy-MM-dd}"));
                }

                int tableSent;
                try
                {
                    if (singleFile)
                    {
                        tableSent = await PushTableSingleFileAsync(
                                project, table, destName, batchSize, pushFormatLock, cancellationToken,
                                msg => Report($"[{i + 1}/{tables.Count}] {msg}"))
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        tableSent = await PushTableParallelAsync(
                                project, table, destName, batchSize, pushWorkers, pushFormatLock,
                                cancellationToken,
                                sent =>
                                {
                                    var rate = sw.Elapsed.TotalSeconds > 0.4 ? sent / sw.Elapsed.TotalSeconds : 0;
                                    Report($"[{i + 1}/{tables.Count}] {table}: pushed {sent:N0}…" +
                                           (rate > 0 ? $" (~{rate:N0}/sec)" : ""));
                                })
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (restoreSql is not null)
                        mapping.TableQueries[table] = restoreSql;
                }

                totalSent += tableSent;

                // API responded OK but said 0 updated → destination last_update won't change
                if (MarketplaceCatalogMapper.IsVendorCatalogUrl(project.ApiUrl)
                    && tableSent > 0
                    && pushFormatLock.SawZeroUpdate
                    && pushFormatLock.ApiUpdated == 0
                    && pushFormatLock.ApiCreated == 0
                    && pushFormatLock.LastBody is not null
                    && !pushFormatLock.LastBody.StartsWith("QUEUED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Pushed {tableSent:N0} rows but API updated=0 created=0 — destination DB not changed. " +
                        $"Response: {pushFormatLock.LastBody}. Check SKU match (Item # = marketplace sku).");
                }

                newHash ??= await _products.GetTableFingerprintAsync(table, cancellationToken)
                    .ConfigureAwait(false);
                await _state.SetLastHashAsync(destKey, stateSku, newHash, cancellationToken).ConfigureAwait(false);
                okTables++;
                var apiBit = pushFormatLock.BatchesOk > 0
                    ? $" | API updated={pushFormatLock.ApiUpdated} created={pushFormatLock.ApiCreated} failed={pushFormatLock.ApiFailed}"
                    : "";
                if (!string.IsNullOrWhiteSpace(pushFormatLock.LastBody))
                    apiBit += $" | resp: {Truncate(pushFormatLock.LastBody, 160)}";
                if (tableSent == 0)
                    lines.Add($"• {table}: OK — 0 rows in database (nothing to push).");
                else
                    lines.Add($"• {table}: OK — {tableSent:N0} row(s) pushed in {sw.Elapsed:mm\\:ss}.{apiBit}");
                Report($"[{i + 1}/{tables.Count}] {table} done — {tableSent:N0} rows in {sw.Elapsed:mm\\:ss}{apiBit}");
            }
            catch (Exception ex)
            {
                failTables++;
                var why = ExplainSyncFailure(ex);
                lines.Add($"• {table}: FAILED — {why}");
                _logger.LogWarning(ex, "Source sync failed: {Table} → {Dest}", table, destName);
                await _state.AddLogAsync(new SyncLogEntry
                {
                    TimestampUtc = DateTime.UtcNow,
                    Level = LogLevelKind.Error,
                    Operation = "DestinationTableSync",
                    Message = $"{project.Name}: {table} FAILED — {why}",
                    Error = ex.ToString(),
                    StoreCode = project.Id
                }, cancellationToken).ConfigureAwait(false);
            }

            Report($"Progress {i + 1}/{tables.Count} (ok={okTables}, skip={skippedTables}, fail={failTables}, rows={totalSent:N0})");
            await SaveSettingsAsync().ConfigureAwait(false);
        }

        var mode = fullSync ? "Full Sync" : "Sync Now";
        var header =
            $"SYNC REPORT — {project.Name} ({mode})\n" +
            $"Sources: {tables.Count}  |  OK: {okTables}  |  Skipped: {skippedTables}  |  Failed: {failTables}  |  Rows pushed: {totalSent:N0}\n" +
            $"API: {Truncate(project.ApiUrl ?? "", 80)}\n" +
            "────────────────────────────";
        var detail = string.Join("\n", lines);
        var footer = failTables > 0
            ? "\n────────────────────────────\nWhy failed: see FAILED lines above. Fix API URL / key / SQL, then Full Sync again."
            : skippedTables > 0 && okTables == 0
                ? "\n────────────────────────────\nAll selected sources unchanged. Click Full Sync if you want to push anyway."
                : "\n────────────────────────────\nAll selected sources finished.";

        var report = $"{header}\n{detail}{footer}";
        UpdateSchedule(project, Truncate(report.Replace("\n", " | "), 500));
        await _state.AddLogAsync(new SyncLogEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Level = failTables > 0 ? LogLevelKind.Warning : LogLevelKind.Information,
            Operation = "DestinationTableSync",
            Message = $"{project.Name}: {report.Replace("\n", " | ")}",
            StoreCode = project.Id
        }, cancellationToken).ConfigureAwait(false);
        await SaveSettingsAsync().ConfigureAwait(false);

        var ok = failTables == 0 && (okTables > 0 || skippedTables > 0);
        Report(report);
        return (ok, report, totalSent, skippedTables);
    }

    /// <summary>Short plain-language reason for UI report.</summary>
    private static string ExplainSyncFailure(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("SQL Server", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("network-related", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
            return "Database connection failed. Check Database page (host/login) → Test Connection.";
        if (msg.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase))
            return "API key/token rejected (401/403). Save correct API key on Destinations, then retry.";
        if (msg.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase))
            return "API URL not found (404). Check Destinations API URL.";
        if (msg.Contains("HTTP 413", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("too large", StringComparison.OrdinalIgnoreCase))
            return "Payload too large for API. Sync will use smaller batches — retry Full Sync.";
        if (msg.Contains("HTTP 4", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("HTTP 5", StringComparison.OrdinalIgnoreCase))
            return Truncate(msg, 220);
        if (msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase))
            return "Cannot reach API URL (network/DNS). Check internet and Destinations API URL.";
        return Truncate(msg, 220);
    }

    /// <summary>Load all rows then ONE multipart csvFile upload (sku-inventory merge).</summary>
    private async Task<int> PushTableSingleFileAsync(
        DestinationProject project,
        string table,
        string destName,
        int readBatch,
        FormatLock formatLock,
        CancellationToken ct,
        Action<string>? progress)
    {
        var all = new List<Dictionary<string, object?>>();
        await foreach (var batch in _products.StreamTableRowsAsync(table, readBatch, ct).ConfigureAwait(false))
        {
            all.AddRange(batch);
            if (all.Count % 20000 < readBatch || batch.Count < readBatch)
                progress?.Invoke($"{table}: loaded {all.Count:N0} rows…");
        }

        progress?.Invoke($"{table}: loaded {all.Count:N0} — uploading one CSV…");
        if (all.Count == 0)
            return 0;

        await PushTableAsync(project, table, destName, all, ct, formatLock).ConfigureAwait(false);
        progress?.Invoke($"{table}: uploaded {all.Count:N0} rows in one file");
        return all.Count;
    }

    /// <summary>SQL stream producer + N parallel HTTP push workers.</summary>
    private async Task<int> PushTableParallelAsync(
        DestinationProject project,
        string table,
        string destName,
        int batchSize,
        int workers,
        FormatLock formatLock,
        CancellationToken ct,
        Action<int>? onProgress)
    {
        var channel = Channel.CreateBounded<List<Dictionary<string, object?>>>(new BoundedChannelOptions(workers + 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true
        });

        var sent = 0;
        Exception? fail = null;

        var producer = Task.Run(async () =>
        {
            try
            {
                await foreach (var batch in _products.StreamTableRowsAsync(table, batchSize, ct)
                                   .ConfigureAwait(false))
                {
                    var list = batch.ToList();
                    var projected = ProjectMarketplaceOrMapped(project, table, list);
                    if (projected.Count == 0)
                        continue;
                    await channel.Writer.WriteAsync(projected, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                fail ??= ex;
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        var consumers = Enumerable.Range(0, Math.Max(2, workers)).Select(_ => Task.Run(async () =>
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (fail is not null)
                    break;
                try
                {
                    await PushTableAsync(project, table, destName, batch, ct, formatLock).ConfigureAwait(false);
                    var n = Interlocked.Add(ref sent, batch.Count);
                    onProgress?.Invoke(n);
                }
                catch (Exception ex)
                {
                    fail ??= ex;
                    channel.Writer.TryComplete(ex);
                    break;
                }
            }
        }, ct)).ToArray();

        await producer.ConfigureAwait(false);
        await Task.WhenAll(consumers).ConfigureAwait(false);
        if (fail is not null)
            throw fail;
        return sent;
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

                    var active = project.FormatFields.Where(f => f.Enabled).ToList();
                    payloadRows.Add(ProductFormatExporter.MapProduct(product, active));
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

    private sealed class FormatLock
    {
        public string? Label;
        public string? LastBody;
        public int ApiUpdated;
        public int ApiCreated;
        public int ApiFailed;
        public int BatchesOk;
        public bool SawZeroUpdate;
    }

    private async Task PushTableAsync(
        DestinationProject project,
        string sourceTable,
        string destTableName,
        List<Dictionary<string, object?>> rows,
        CancellationToken ct,
        FormatLock? formatLock = null)
    {
        var projected = NormalizeRows(ProjectMarketplaceOrMapped(project, sourceTable, rows));
        if (projected.Count == 0)
            return;

        var client = _httpClientFactory.CreateClient(nameof(DestinationSyncService));
        // Keep template header order (not A→Z sort)
        string csv;
        if (BuiltInSyncQueries.IsProductsFamily(sourceTable))
            csv = ProductFormatExporter.ToCsvOrdered(projected, BuiltInSyncQueries.ProductHeaders);
        else if (BuiltInSyncQueries.IsCaratonePayment(sourceTable))
            csv = ProductFormatExporter.ToCsvOrdered(projected, CaratonePaymentSql.Headers);
        else if (BuiltInSyncQueries.IsCaratoneSale(sourceTable))
            csv = ProductFormatExporter.ToCsvOrdered(projected, CaratoneSaleSql.Headers);
        else if (BuiltInSyncQueries.IsInventoryFamily(sourceTable)
                 || MarketplaceCatalogMapper.IsVendorCatalogUrl(project.ApiUrl))
            csv = ProductFormatExporter.ToCsvOrdered(projected, BuiltInSyncQueries.InventoryHeaders);
        else
            csv = ProductFormatExporter.ToCsvAllColumns(projected);
        var jsonArray = ProductFormatExporter.ToJsonArray(projected);
        var wrapped = JsonSerializer.Serialize(new
        {
            table = destTableName,
            source = sourceTable,
            rowCount = projected.Count,
            rows = projected
        }, JsonOpts);

        var preferCsv = string.Equals(project.BodyFormat, "Csv", StringComparison.OrdinalIgnoreCase);
        var apiUrl = project.ApiUrl ?? "";
        var importUrl = apiUrl.Contains("import", StringComparison.OrdinalIgnoreCase)
                        || apiUrl.Contains("vendor-catalog", StringComparison.OrdinalIgnoreCase)
                        || apiUrl.Contains("sku-inventory", StringComparison.OrdinalIgnoreCase);
        // vendor-catalog multer.single('csvFile') — sending field "file" or extra junk → MulterError Unexpected field
        var vendorCatalog = apiUrl.Contains("vendor-catalog", StringComparison.OrdinalIgnoreCase)
                            || BuiltInSyncQueries.IsProductsFamily(sourceTable);

        var attemptFactories = new List<(string Label, Func<HttpContent> Make)>();
        if (importUrl || preferCsv || vendorCatalog)
        {
            // ONLY csvFile + file bytes (no table/mode extras — those also trip strict multer)
            attemptFactories.Add(("multipart-csvFile",
                () => BuildMultipartCsv(csv, sourceTable, fileFieldName: "csvFile", extras: false)));
            if (!vendorCatalog && string.IsNullOrEmpty(formatLock?.Label))
            {
                attemptFactories.Add(("multipart-csvFile-extras",
                    () => BuildMultipartCsv(csv, sourceTable, fileFieldName: "csvFile", extras: true)));
            }
        }
        else
        {
            attemptFactories.Add(("json-array", () => new StringContent(jsonArray, Encoding.UTF8, "application/json")));
            attemptFactories.Add(("json-wrapped", () => new StringContent(wrapped, Encoding.UTF8, "application/json")));
            attemptFactories.Add(("multipart-csvFile",
                () => BuildMultipartCsv(csv, sourceTable, fileFieldName: "csvFile", extras: false)));
        }

        // After first success, only that format (huge speedup on 1M+ rows)
        if (!string.IsNullOrEmpty(formatLock?.Label))
            attemptFactories = attemptFactories.Where(a => a.Label == formatLock!.Label).ToList();

        var errors = new List<string>();
        foreach (var attempt in attemptFactories)
        {
            try
            {
                using var request = new HttpRequestMessage(new HttpMethod(project.HttpMethod), project.ApiUrl);
                ApplyAuth(project, request);
                request.Headers.TryAddWithoutValidation("X-Table-Name", destTableName);
                request.Headers.TryAddWithoutValidation("X-Source-Name", sourceTable);
                request.Headers.TryAddWithoutValidation("X-Batch-Size", projected.Count.ToString());
                request.Content = attempt.Make();

                using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (formatLock is not null)
                        formatLock.Label = attempt.Label;
                    ApplyApiResponseStats(body, formatLock, projected.Count);
                    _logger.LogInformation("Destination batch OK ({Format}): {Body}",
                        attempt.Label, Truncate(body, 400));
                    return;
                }

                var snip = Truncate(StripHtml(body), 280);
                errors.Add($"{attempt.Label}: HTTP {(int)response.StatusCode}: {snip}");
                if ((int)response.StatusCode is 401 or 403)
                    throw new InvalidOperationException(
                        $"Table {destTableName} ({attempt.Label}): HTTP {(int)response.StatusCode}: {snip}");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{attempt.Label}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"Table {destTableName}: push failed. " + string.Join(" | ", errors));
    }

    private static string StripHtml(string body)
    {
        if (string.IsNullOrEmpty(body) || body.IndexOf('<') < 0)
            return body;
        var t = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
        t = System.Net.WebUtility.HtmlDecode(t);
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
    }

    private static void ApplyApiResponseStats(string body, FormatLock? formatLock, int batchRows)
    {
        if (formatLock is null)
            return;
        formatLock.BatchesOk++;
        formatLock.LastBody = Truncate(body ?? "", 400);

        if (string.IsNullOrWhiteSpace(body))
        {
            // Empty 200 — treat as unknown; don't mark zero-update yet
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var updated = ReadCount(root, "updated", "updatedCount", "updatedRows", "modified", "modifiedCount", "upserted");
            var created = ReadCount(root, "created", "createdCount", "inserted", "imported", "importedCount", "new");
            var failed = ReadCount(root, "failed", "failedCount", "errors", "errorCount");
            var skipped = ReadCount(root, "skipped", "skippedCount", "unchanged");

            if (updated is null && created is null && failed is null && skipped is null)
            {
                // success:true only — may be async job; look for jobId
                if (root.TryGetProperty("jobId", out _) || root.TryGetProperty("job_id", out _)
                    || root.TryGetProperty("importId", out _) || root.TryGetProperty("id", out _))
                {
                    formatLock.LastBody = "QUEUED job — " + formatLock.LastBody;
                    return;
                }
                // No counts at all: don't assume failure
                return;
            }

            formatLock.ApiUpdated += updated ?? 0;
            formatLock.ApiCreated += created ?? 0;
            formatLock.ApiFailed += failed ?? 0;
            if ((updated ?? 0) == 0 && (created ?? 0) == 0)
                formatLock.SawZeroUpdate = true;
        }
        catch
        {
            // non-JSON body
            if (body.Contains("updated\":0", StringComparison.OrdinalIgnoreCase)
                || body.Contains("updated\": 0", StringComparison.OrdinalIgnoreCase))
                formatLock.SawZeroUpdate = true;
        }
    }

    private static int? ReadCount(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                return n;
            if (el.ValueKind == JsonValueKind.Array)
                return el.GetArrayLength();
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s))
                return s;
        }
        // nested data/result
        foreach (var nest in new[] { "data", "result", "results", "summary" })
        {
            if (!root.TryGetProperty(nest, out var child) || child.ValueKind != JsonValueKind.Object)
                continue;
            var inner = ReadCount(child, names);
            if (inner is not null)
                return inner;
        }
        return null;
    }

    /// <summary>
    /// Marketplace multer expects field name <c>csvFile</c> only.
    /// Extra form fields / wrong name (<c>file</c>) → MulterError: Unexpected field.
    /// </summary>
    private static MultipartFormDataContent BuildMultipartCsv(
        string csv,
        string sourceTable,
        string fileFieldName = "csvFile",
        bool extras = false)
    {
        var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        // Stable filename — avoid odd chars from source key in Content-Disposition
        var fileName = BuiltInSyncQueries.IsProductsFamily(sourceTable) ? "products.csv"
            : BuiltInSyncQueries.IsInventoryFamily(sourceTable) ? "inventory.csv"
            : "data.csv";
        form.Add(bytes, fileFieldName, fileName);
        if (extras)
        {
            form.Add(new StringContent("true"), "updateExisting");
            form.Add(new StringContent("true"), "upsert");
            form.Add(new StringContent("merge"), "mode");
        }
        return form;
    }

    /// <summary>ADO.NET values → JSON-safe primitives (avoids empty {} for object boxes).</summary>
    private static List<Dictionary<string, object?>> NormalizeRows(List<Dictionary<string, object?>> rows)
    {
        return rows.Select(r =>
        {
            var n = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in r)
                n[kv.Key] = NormalizeCell(kv.Value);
            return n;
        }).ToList();
    }

    private static object? NormalizeCell(object? value)
    {
        if (value is null or DBNull) return null;
        return value switch
        {
            string or bool or byte or short or int or long or float or double or decimal => value,
            DateTime dt => dt.ToString("o"),
            DateTimeOffset dto => dto.ToString("o"),
            Guid g => g.ToString(),
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static List<Dictionary<string, object?>> ProjectMarketplaceOrMapped(
        DestinationProject project,
        string sourceTable,
        List<Dictionary<string, object?>> rows)
    {
        if (BuiltInSyncQueries.IsProductsFamily(sourceTable))
        {
            return MarketplaceCatalogMapper.OrderTemplateColumns(
                MarketplaceCatalogMapper.ToProductRows(rows),
                BuiltInSyncQueries.ProductHeaders);
        }

        if (BuiltInSyncQueries.IsCaratonePayment(sourceTable))
        {
            return MarketplaceCatalogMapper.OrderTemplateColumns(rows, CaratonePaymentSql.Headers);
        }

        if (BuiltInSyncQueries.IsCaratoneSale(sourceTable))
        {
            return MarketplaceCatalogMapper.OrderTemplateColumns(rows, CaratoneSaleSql.Headers);
        }

        if (BuiltInSyncQueries.IsInventoryFamily(sourceTable))
        {
            return MarketplaceCatalogMapper.OrderTemplateColumns(
                MarketplaceCatalogMapper.ToCatalogRows(rows),
                BuiltInSyncQueries.InventoryHeaders);
        }

        // Legacy: vendor-catalog URL with non-fixed table → inventory shape
        if (MarketplaceCatalogMapper.IsVendorCatalogUrl(project.ApiUrl))
        {
            return MarketplaceCatalogMapper.OrderTemplateColumns(
                MarketplaceCatalogMapper.ToCatalogRows(rows),
                BuiltInSyncQueries.InventoryHeaders);
        }

        return ProjectRowsForTable(project, sourceTable, rows);
    }

    /// <summary>
    /// If project has enabled FormatFields for this source table (Table.Column), keep only those columns.
    /// If none match this table, send all columns.
    /// </summary>
    private static List<Dictionary<string, object?>> ProjectRowsForTable(
        DestinationProject project,
        string sourceTable,
        List<Dictionary<string, object?>> rows)
    {
        var fields = project.FormatFields
            .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.SourceField))
            .Select(f =>
            {
                var src = f.SourceField.Trim();
                var dest = string.IsNullOrWhiteSpace(f.DestinationKey) ? null : f.DestinationKey.Trim();
                var dot = src.LastIndexOf('.');
                string? table = null;
                string col = src;
                if (dot > 0)
                {
                    table = src[..dot];
                    col = src[(dot + 1)..];
                    dest ??= col;
                }
                else
                    dest ??= src;
                return (Table: table, Col: col, Dest: dest!);
            })
            .Where(x => x.Table is null || x.Table.Equals(sourceTable, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Table is not null) // only Table.Column picks filter; bare names ignored here
            .ToList();

        if (fields.Count == 0)
            return rows;

        var projected = rows.Select(r =>
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields)
            {
                if (r.TryGetValue(f.Col, out var v))
                    row[f.Dest] = v;
                else if (r.TryGetValue($"{sourceTable}.{f.Col}", out var v2))
                    row[f.Dest] = v2;
            }
            return row;
        }).ToList();

        // If ticks don't match SQL column names, don't send empty objects — fall back to full rows
        if (projected.Count > 0 && projected.All(r => r.Count == 0))
            return rows;
        return projected;
    }

    private static Dictionary<string, object?> MapRawRow(
        Dictionary<string, object?> source,
        IReadOnlyList<DestinationFormatField> fields)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f.DestinationKey) || string.IsNullOrWhiteSpace(f.SourceField))
                continue;
            row[f.DestinationKey.Trim()] = ResolveSourceValue(source, f.SourceField);
        }
        return row;
    }

    /// <summary>
    /// Supports one column, Table.Column, or multiple: "Sku | Barcode" (first non-empty, else join with space).
    /// </summary>
    private static object? ResolveSourceValue(Dictionary<string, object?> source, string sourceField)
    {
        var parts = sourceField.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        object? FirstHit(string key)
        {
            if (source.TryGetValue(key, out var v) && v is not null && !string.IsNullOrWhiteSpace(Convert.ToString(v)))
                return v;
            // Table.Column → try Column only
            var dot = key.LastIndexOf('.');
            if (dot > 0)
            {
                var col = key[(dot + 1)..];
                if (source.TryGetValue(col, out var v2) && v2 is not null && !string.IsNullOrWhiteSpace(Convert.ToString(v2)))
                    return v2;
            }
            return null;
        }

        if (parts.Length == 1)
            return FirstHit(parts[0]) ?? (source.TryGetValue(parts[0], out var raw) ? raw : null);

        var values = new List<string>();
        foreach (var p in parts)
        {
            var hit = FirstHit(p);
            if (hit is null)
                continue;
            var s = Convert.ToString(hit);
            if (!string.IsNullOrWhiteSpace(s))
                values.Add(s!);
        }
        if (values.Count == 0)
            return null;
        if (values.Count == 1)
            return values[0];
        return string.Join(" ", values);
    }

    private async Task PushAsync(DestinationProject project, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(DestinationSyncService));
        using var request = new HttpRequestMessage(new HttpMethod(project.HttpMethod), project.ApiUrl);
        ApplyAuth(project, request);

        if (string.Equals(project.BodyFormat, "Csv", StringComparison.OrdinalIgnoreCase))
        {
            var active = project.FormatFields.Where(f => f.Enabled).ToList();
            var csv = ProductFormatExporter.ToCsv(rows, active);
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
        if (string.IsNullOrWhiteSpace(apiKey))
            return;

        if (!string.IsNullOrWhiteSpace(project.AuthHeaderName))
        {
            var value = (project.AuthHeaderPrefix ?? string.Empty) + apiKey;
            request.Headers.TryAddWithoutValidation(project.AuthHeaderName, value);
        }

        // Local Valliani API uses X-Api-Key; marketplace uses Bearer above — send both when key present
        if (!string.Equals(project.AuthHeaderName, "X-Api-Key", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
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

    /// <summary>Same-name sync when alias empty; otherwise send destination name.</summary>
    private static string ResolveDestinationTableName(FieldMappingConfig mapping, string sourceTable)
    {
        if (mapping.TableAliases.TryGetValue(sourceTable, out var alias) && !string.IsNullOrWhiteSpace(alias))
            return alias.Trim();
        return sourceTable;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";
}
