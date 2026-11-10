using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Infrastructure.Destinations;

namespace VallianiDataSync.App.ViewModels;

public partial class DestTableRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _tableName = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
}

public partial class DestinationsViewModel : ObservableObject
{
    private const string ApiKeySavedMask = "••••••••••••";
    private const int PreviewRowLimit = 200;

    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IDestinationSyncService _sync;
    private readonly IOtpAuthService _otp;
    private readonly IProductRepository _products;

    private bool _suppressSourceChanged;

    public ObservableCollection<DestinationProject> Projects { get; } = new();
    public ObservableCollection<DestTableRow> SourceOptions { get; } = new();

    [ObservableProperty] private DestinationProject? _selectedProject;
    [ObservableProperty] private DestTableRow? _selectedSource;
    [ObservableProperty] private bool _showCaratoneDates;
    [ObservableProperty] private DataView? _previewView;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _sensitiveOtp1 = string.Empty;
    [ObservableProperty] private string _sensitiveOtp2 = string.Empty;
    [ObservableProperty] private string _selectionSummary = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isIdle = true;

    partial void OnIsBusyChanged(bool value) => IsIdle = !value;

    public DestinationsViewModel(
        ISettingsService settings,
        ICredentialStore credentials,
        IDestinationSyncService sync,
        IOtpAuthService otp,
        IProductRepository products)
    {
        _settings = settings;
        _credentials = credentials;
        _sync = sync;
        _otp = otp;
        _products = products;
        RebuildSourceOptions();
        Load();
    }

    public void Load()
    {
        BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);
        Projects.Clear();
        foreach (var p in _settings.Current.Destinations)
            Projects.Add(p);
        if (SelectedProject is null && Projects.Count > 0)
            SelectedProject = Projects[0];
        else
            LoadSelected();
    }

    partial void OnSelectedProjectChanged(DestinationProject? value) => LoadSelected();

    partial void OnSelectedSourceChanged(DestTableRow? value)
    {
        if (_suppressSourceChanged) return;
        ApplySourceSelection(value);
        _ = LoadPreviewAsync();
    }

    private void RebuildSourceOptions()
    {
        SourceOptions.Clear();
        foreach (var key in BuiltInSyncQueries.AllKeys)
        {
            SourceOptions.Add(new DestTableRow
            {
                TableName = key,
                DisplayName = $"{key} — {BuiltInSyncQueries.DisplayName(key)}"
            });
        }
    }

    private void LoadSelected()
    {
        PreviewView = null;
        if (SelectedProject is null)
        {
            ApiKey = string.Empty;
            SelectionSummary = "";
            ShowCaratoneDates = false;
            _suppressSourceChanged = true;
            SelectedSource = null;
            _suppressSourceChanged = false;
            return;
        }

        ApiKey = _credentials.HasSecret(CredentialKeys.DestinationApiKey(SelectedProject.Id))
            ? ApiKeySavedMask
            : string.Empty;

        BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);
        var saved = (SelectedProject.SyncSources ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s) && BuiltInSyncQueries.IsFixedSource(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Single source only — take first saved, else first default, else first option
        var pick = saved.FirstOrDefault()
                   ?? BuiltInSyncQueries.DefaultKeys.FirstOrDefault()
                   ?? BuiltInSyncQueries.AllKeys.FirstOrDefault();

        _suppressSourceChanged = true;
        SelectedSource = SourceOptions.FirstOrDefault(s =>
                               s.TableName.Equals(pick, StringComparison.OrdinalIgnoreCase))
                           ?? SourceOptions.FirstOrDefault();
        _suppressSourceChanged = false;

        ApplySourceSelection(SelectedSource);
        StatusMessage = _credentials.HasSecret(CredentialKeys.DestinationApiKey(SelectedProject.Id))
            ? "API token saved (••••). Leave as •••• or paste a new token to replace."
            : "No API token yet — paste token then Save.";
        _ = LoadPreviewAsync();
    }

    private void ApplySourceSelection(DestTableRow? source)
    {
        var key = source?.TableName;
        ShowCaratoneDates = BuiltInSyncQueries.IsCaratoneDated(key);
        if (ShowCaratoneDates && SelectedProject is not null)
            SelectedProject.EnsureCaratoneDatesDefaultToday();

        SelectionSummary = source is null
            ? "No source selected"
            : ShowCaratoneDates
                ? $"Source: {source.TableName} — dates default to PC today for daily sync"
                : $"Source: {source.TableName}";
    }

    private string? CurrentSourceKey() =>
        SelectedSource?.TableName is { Length: > 0 } t && BuiltInSyncQueries.IsFixedSource(t)
            ? t
            : null;

    private List<DestinationFormatField> BuildFormatFields(string table)
    {
        IReadOnlyList<string> headers =
            BuiltInSyncQueries.IsInventoryFamily(table) ? BuiltInSyncQueries.InventoryHeaders
            : BuiltInSyncQueries.IsProductsFamily(table) ? BuiltInSyncQueries.ProductHeaders
            : BuiltInSyncQueries.IsCaratoneSale(table) ? CaratoneSaleSql.Headers
            : BuiltInSyncQueries.IsCaratonePayment(table) ? CaratonePaymentSql.Headers
            : Array.Empty<string>();

        if (headers.Count == 0 && PreviewView?.Table is { } dt)
            headers = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();

        return headers.Select(h => new DestinationFormatField
        {
            SourceField = $"{table}.{h}",
            DestinationKey = h,
            Enabled = true
        }).ToList();
    }

    private static DataTable ToDataTable(IReadOnlyList<Dictionary<string, object?>> rows, IReadOnlyList<string>? preferHeaders)
    {
        var table = new DataTable();
        if (rows.Count == 0 && preferHeaders is { Count: > 0 })
        {
            foreach (var h in preferHeaders)
                table.Columns.Add(h);
            return table;
        }

        var cols = preferHeaders is { Count: > 0 }
            ? preferHeaders.ToList()
            : rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var c in cols)
            table.Columns.Add(c);

        foreach (var row in rows)
        {
            var dr = table.NewRow();
            foreach (DataColumn col in table.Columns)
            {
                if (row.TryGetValue(col.ColumnName, out var v) && v is not null)
                    dr[col.ColumnName] = v;
                else
                    dr[col.ColumnName] = DBNull.Value;
            }
            table.Rows.Add(dr);
        }

        return table;
    }

    [RelayCommand]
    private async Task LoadPreviewAsync()
    {
        var table = CurrentSourceKey();
        if (table is null)
        {
            PreviewView = null;
            StatusMessage = "Pick a source from the dropdown.";
            return;
        }

        if (SelectedProject is null)
        {
            StatusMessage = "Select a project first.";
            return;
        }

        if (IsBusy) return;
        IsBusy = true;
        string? restoreSql = null;
        try
        {
            StatusMessage = $"Loading preview for {table}…";
            if (BuiltInSyncQueries.IsCaratoneDated(table))
            {
                SelectedProject.EnsureCaratoneDatesDefaultToday();
                SelectedProject.TryGetSaleReportDates(out var from, out var to);
                var mapping = _settings.Current.Mapping;
                BuiltInSyncQueries.EnsureDefaults(mapping);
                restoreSql = mapping.TableQueries.TryGetValue(table, out var prev) ? prev : null;
                mapping.TableQueries[table] = BuiltInSyncQueries.IsCaratonePayment(table)
                    ? CaratonePaymentSql.ApplyDates(CaratonePaymentSql.Template, from, to)
                    : CaratoneSaleSql.ApplyDates(CaratoneSaleSql.Template, from, to);
            }

            var rows = await _products.ReadTableRowsAsync(table, PreviewRowLimit, 0).ConfigureAwait(true);
            IReadOnlyList<string>? headers =
                BuiltInSyncQueries.IsCaratonePayment(table) ? CaratonePaymentSql.Headers
                : BuiltInSyncQueries.IsCaratoneSale(table) ? CaratoneSaleSql.Headers
                : BuiltInSyncQueries.IsInventoryFamily(table) ? BuiltInSyncQueries.InventoryHeaders
                : BuiltInSyncQueries.IsProductsFamily(table) ? BuiltInSyncQueries.ProductHeaders
                : null;

            var projected = headers is not null
                ? MarketplaceCatalogMapper.OrderTemplateColumns(rows.ToList(), headers)
                : rows.ToList();

            var dt = ToDataTable(projected, headers);
            PreviewView = dt.DefaultView;
            SelectionSummary = $"{table}: showing {dt.Rows.Count} row(s) × {dt.Columns.Count} col(s) (preview)";
            StatusMessage = dt.Rows.Count == 0
                ? $"{table}: no rows for current filter/dates."
                : $"{table}: preview loaded ({dt.Rows.Count} rows).";
        }
        catch (Exception ex)
        {
            PreviewView = null;
            StatusMessage = $"Preview failed: {ex.Message}";
        }
        finally
        {
            if (restoreSql is not null)
                _settings.Current.Mapping.TableQueries[table] = restoreSql;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportSelectedCsvAsync()
    {
        var table = CurrentSourceKey();
        if (table is null)
        {
            StatusMessage = "Pick a source, then Export CSV.";
            MessageBox.Show(StatusMessage, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsBusy) return;
        IsBusy = true;

        BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VallianiDataSync",
            "csv-export",
            stamp);
        Directory.CreateDirectory(folder);
        var fileName = $"{table}_{stamp}.csv";
        var path = Path.Combine(folder, fileName);

        string? restoreSql = null;
        try
        {
            if (BuiltInSyncQueries.IsCaratoneDated(table) && SelectedProject is not null)
            {
                SelectedProject.EnsureCaratoneDatesDefaultToday();
                SelectedProject.TryGetSaleReportDates(out var saleFrom, out var saleTo);
                var mapping = _settings.Current.Mapping;
                BuiltInSyncQueries.EnsureDefaults(mapping);
                restoreSql = mapping.TableQueries.TryGetValue(table, out var prev) ? prev : null;
                mapping.TableQueries[table] = BuiltInSyncQueries.IsCaratonePayment(table)
                    ? CaratonePaymentSql.ApplyDates(CaratonePaymentSql.Template, saleFrom, saleTo)
                    : CaratoneSaleSql.ApplyDates(CaratoneSaleSql.Template, saleFrom, saleTo);
            }

            StatusMessage = $"Exporting {table} → {fileName}…";

            IReadOnlyList<string>? orderedHeaders =
                BuiltInSyncQueries.IsCaratonePayment(table) ? CaratonePaymentSql.Headers
                : BuiltInSyncQueries.IsCaratoneSale(table) ? CaratoneSaleSql.Headers
                : BuiltInSyncQueries.IsInventoryFamily(table) ? BuiltInSyncQueries.InventoryHeaders
                : BuiltInSyncQueries.IsProductsFamily(table) ? BuiltInSyncQueries.ProductHeaders
                : null;

            var totalRows = 0;
            await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            await using (var writer = new StreamWriter(fs, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                var headerWritten = false;
                var keys = orderedHeaders?.ToList() ?? new List<string>();

                await foreach (var batch in _products.StreamTableRowsAsync(table, 2000).ConfigureAwait(true))
                {
                    IReadOnlyList<Dictionary<string, object?>> projected = orderedHeaders is not null
                        ? MarketplaceCatalogMapper.OrderTemplateColumns(batch.ToList(), orderedHeaders)
                        : batch;

                    if (!headerWritten)
                    {
                        if (keys.Count == 0)
                        {
                            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var row in projected)
                            foreach (var k in row.Keys)
                                if (seen.Add(k)) keys.Add(k);
                            keys.Sort(StringComparer.OrdinalIgnoreCase);
                        }
                        if (keys.Count == 0)
                            throw new InvalidOperationException("No columns returned from SQL for this source.");
                        ProductFormatExporter.WriteCsvHeader(writer, keys);
                        headerWritten = true;
                    }

                    ProductFormatExporter.WriteCsvRows(writer, projected, keys);
                    totalRows += projected.Count;
                    StatusMessage = $"Exporting {table}… {totalRows:N0} rows written";
                    await writer.FlushAsync().ConfigureAwait(true);
                }

                if (!headerWritten)
                {
                    if (keys.Count == 0)
                        keys.Add("empty");
                    ProductFormatExporter.WriteCsvHeader(writer, keys);
                }
            }

            StatusMessage = $"CSV export done: {totalRows:N0} row(s).\n{path}";
            MessageBox.Show($"{fileName}\n{totalRows:N0} rows\n\n{folder}", "Export CSV",
                MessageBoxButton.OK, MessageBoxImage.Information);
            try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); }
            catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            try { if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path); } catch { /* ignore */ }
            StatusMessage = $"Export failed: {ex.Message}";
            MessageBox.Show(
                "Export CSV failed.\n\n" + ex.Message +
                "\n\nCheck Database connection (Server / Database page), then try again.",
                "Export CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (restoreSql is not null)
                _settings.Current.Mapping.TableQueries[table] = restoreSql;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        if (!await EnsureOtpIfNeededAsync("Admin: create destination").ConfigureAwait(true))
            return;

        var defaultSource = BuiltInSyncQueries.DefaultKeys.FirstOrDefault()
                            ?? BuiltInSyncQueries.AllKeys.FirstOrDefault()
                            ?? "inventory";
        var project = new DestinationProject
        {
            Name = $"Project {Projects.Count + 1}",
            IntervalMinutes = 120,
            SyncSources = new List<string> { defaultSource },
            FormatFields = new List<DestinationFormatField>(),
            SaleReportDateFrom = DateTime.Today,
            SaleReportDateTo = DateTime.Today,
            SaleDateIsRange = false
        };
        _settings.Update(s =>
        {
            BuiltInSyncQueries.EnsureDefaults(s.Mapping);
            s.Destinations.Add(project);
        });
        await _settings.SaveAsync();
        Projects.Add(project);
        SelectedProject = project;
        StatusMessage = "Project created — pick one source from the dropdown.";
    }

    [RelayCommand]
    private async Task DeleteProjectAsync()
    {
        if (SelectedProject is null) return;
        if (!await EnsureOtpIfNeededAsync("Admin: delete destination").ConfigureAwait(true))
            return;

        var id = SelectedProject.Id;
        _credentials.DeleteSecret(CredentialKeys.DestinationApiKey(id));
        _settings.Update(s => s.Destinations.RemoveAll(p => p.Id == id));
        await _settings.SaveAsync();
        Projects.Remove(SelectedProject);
        SelectedProject = Projects.FirstOrDefault();
        StatusMessage = "Project deleted.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Select a project first.";
            return;
        }
        if (!await EnsureOtpIfNeededAsync("Admin: save destination / API").ConfigureAwait(true))
            return;

        var table = CurrentSourceKey();
        if (table is null)
        {
            StatusMessage = "Pick one source from the dropdown, then Save.";
            return;
        }

        if (BuiltInSyncQueries.IsCaratoneDated(table))
            SelectedProject.EnsureCaratoneDatesDefaultToday();

        var selectedCols = BuildFormatFields(table);
        var id = SelectedProject.Id;
        var typedKey = ApiKey?.Trim() ?? "";
        var keepExistingKey = typedKey.Length == 0 || typedKey == ApiKeySavedMask;

        _settings.Update(s =>
        {
            BuiltInSyncQueries.EnsureDefaults(s.Mapping);
            var p = s.Destinations.FirstOrDefault(x => x.Id == id);
            if (p is null) return;
            p.Name = SelectedProject.Name.Trim();
            p.Enabled = SelectedProject.Enabled;
            p.AutoSyncEnabled = SelectedProject.AutoSyncEnabled;
            p.IntervalMinutes = Math.Max(15, SelectedProject.IntervalMinutes);
            p.ApiUrl = SelectedProject.ApiUrl.Trim();
            p.FieldsUrl = null;
            p.HttpMethod = "POST";
            p.BodyFormat = p.ApiUrl.Contains("sku-inventory", StringComparison.OrdinalIgnoreCase)
                           || p.ApiUrl.Contains("import", StringComparison.OrdinalIgnoreCase)
                           || p.ApiUrl.Contains("vendor-catalog", StringComparison.OrdinalIgnoreCase)
                           || BuiltInSyncQueries.WantsSingleFileSource(table)
                ? "Csv"
                : (string.IsNullOrWhiteSpace(SelectedProject.BodyFormat) ? "JsonArray" : SelectedProject.BodyFormat);
            p.AuthHeaderName = "Authorization";
            p.AuthHeaderPrefix = "Bearer ";
            p.SyncSources = new List<string> { table };
            p.FormatFields = selectedCols;
            p.SaleDateIsRange = SelectedProject.SaleDateIsRange;
            p.SaleReportDateFrom = SelectedProject.SaleReportDateFrom ?? DateTime.Today;
            p.SaleReportDateTo = SelectedProject.SaleDateIsRange
                ? (SelectedProject.SaleReportDateTo ?? p.SaleReportDateFrom)
                : p.SaleReportDateFrom;
        });

        if (!keepExistingKey)
            _credentials.SaveSecret(CredentialKeys.DestinationApiKey(id), typedKey);

        await _settings.SaveAsync();
        Load();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == id);
        if (SelectedProject is not null && _credentials.HasSecret(CredentialKeys.DestinationApiKey(id)))
            ApiKey = ApiKeySavedMask;

        StatusMessage = _credentials.HasSecret(CredentialKeys.DestinationApiKey(id))
            ? $"Saved. Source={table}. Token on file (••••)."
            : "Saved — WARNING: paste API token then Save again.";
    }

    [RelayCommand]
    private async Task SyncNowAsync() => await RunSyncAsync(fullSync: false);

    [RelayCommand]
    private async Task FullSyncAsync() => await RunSyncAsync(fullSync: true);

    private async Task RunSyncAsync(bool fullSync)
    {
        if (SelectedProject is null) return;

        if (!SelectedProject.Enabled)
        {
            StatusMessage = "Project is Paused. Tick Enabled, click Save, then Sync again.";
            MessageBox.Show(StatusMessage, "Destination sync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedProject.ApiUrl))
        {
            StatusMessage = "API URL is empty. Set the live destination API URL, Save, then Sync.";
            MessageBox.Show(StatusMessage, "Destination sync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var table = CurrentSourceKey();
        if (table is null)
        {
            StatusMessage = "Pick one source from the dropdown, then Sync.";
            MessageBox.Show(StatusMessage, "Destination sync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (BuiltInSyncQueries.IsCaratoneDated(table))
            SelectedProject.EnsureCaratoneDatesDefaultToday();

        var selectedCols = BuildFormatFields(table);
        var id = SelectedProject.Id;
        var apiUrl = SelectedProject.ApiUrl.Trim();
        var bodyFormat = apiUrl.Contains("import", StringComparison.OrdinalIgnoreCase)
                         || apiUrl.Contains("vendor-catalog", StringComparison.OrdinalIgnoreCase)
            ? "Csv"
            : (string.IsNullOrWhiteSpace(SelectedProject.BodyFormat) ? "JsonArray" : SelectedProject.BodyFormat);

        _settings.Update(s =>
        {
            BuiltInSyncQueries.EnsureDefaults(s.Mapping);
            var p = s.Destinations.FirstOrDefault(x => x.Id == id);
            if (p is null) return;
            p.Enabled = true;
            p.ApiUrl = apiUrl;
            p.BodyFormat = bodyFormat;
            p.SyncSources = new List<string> { table };
            p.FormatFields = selectedCols;
            p.SaleDateIsRange = SelectedProject.SaleDateIsRange;
            p.SaleReportDateFrom = SelectedProject.SaleReportDateFrom ?? DateTime.Today;
            p.SaleReportDateTo = SelectedProject.SaleDateIsRange
                ? (SelectedProject.SaleReportDateTo ?? p.SaleReportDateFrom)
                : p.SaleReportDateFrom;
        });
        await _settings.SaveAsync();

        StatusMessage = fullSync ? $"Full sync {table}…" : $"Syncing {table}…";

        var progress = new Progress<string>(msg =>
        {
            StatusMessage = msg;
            if (SelectedProject is not null && SelectedProject.Id == id)
                SelectedProject.LastSyncMessage = msg;
        });
        var (ok, message, sent, skipped) = await _sync.SyncProjectAsync(id, fullSync: fullSync, progress: progress);
        var title = !ok
            ? "Destination sync — errors"
            : sent == 0 && skipped > 0
                ? "Destination sync — nothing new"
                : "Destination sync report";

        Load();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == id);
        if (SelectedProject is not null)
            SelectedProject.LastSyncMessage = message;
        StatusMessage = ok
            ? $"Report: {sent:N0} row(s) pushed, {skipped} source(s) skipped."
            : "Sync had errors — see report popup for each source.";
        MessageBox.Show(message, title, MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    [RelayCommand]
    private async Task SendSensitiveOtpAsync()
    {
        var (ok, message) = await _otp.SendSensitiveOtpsAsync("Admin destination change");
        StatusMessage = message;
    }

    private async Task<bool> EnsureOtpIfNeededAsync(string reason)
    {
        if (!_settings.Current.Security.OtpEnabled)
            return true;
        if (string.IsNullOrWhiteSpace(SensitiveOtp1) || string.IsNullOrWhiteSpace(SensitiveOtp2))
        {
            StatusMessage = $"{reason}: click Send OTP, enter both email codes, then try again.";
            MessageBox.Show(StatusMessage, "Admin OTP required", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var (ok, message) = await _otp.VerifySensitiveOtpsAsync(SensitiveOtp1, SensitiveOtp2);
        StatusMessage = message;
        SensitiveOtp1 = string.Empty;
        SensitiveOtp2 = string.Empty;
        if (!ok)
            MessageBox.Show(message, "Admin OTP failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        return ok;
    }
}
