using System.Collections.ObjectModel;
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

public partial class DestColumnRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _tableName = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
}

public partial class DestinationsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IDestinationSyncService _sync;
    private readonly IOtpAuthService _otp;
    private readonly IProductRepository _products;

    private List<DestColumnRow> _allColumns = new();

    public ObservableCollection<DestinationProject> Projects { get; } = new();
    public ObservableCollection<DestTableRow> Tables { get; } = new();
    public ObservableCollection<DestColumnRow> Columns { get; } = new();
    public IReadOnlyList<string> BodyFormats { get; } = new[] { "JsonArray", "Csv" };

    [ObservableProperty] private DestinationProject? _selectedProject;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _sensitiveOtp1 = string.Empty;
    [ObservableProperty] private string _sensitiveOtp2 = string.Empty;
    [ObservableProperty] private string _formatFileHint = "";
    [ObservableProperty] private string _selectionSummary = "";

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

    private const string ApiKeySavedMask = "••••••••••••";

    private void LoadSelected()
    {
        if (SelectedProject is null)
        {
            ApiKey = string.Empty;
            FormatFileHint = "Select a project.";
            Tables.Clear();
            Columns.Clear();
            SelectionSummary = "";
            return;
        }

        // Show mask when key is already stored (field used to look "wiped" after Save)
        ApiKey = _credentials.HasSecret(CredentialKeys.DestinationApiKey(SelectedProject.Id))
            ? ApiKeySavedMask
            : string.Empty;

        BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);
        var saved = new HashSet<string>(
            (SelectedProject.SyncSources ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)),
            StringComparer.OrdinalIgnoreCase);
        // Default: sales/inventory/payments; marketplace-inv-test only if previously saved
        var anyFixed = saved.Any(BuiltInSyncQueries.IsFixedSource);
        Tables.Clear();
        foreach (var key in BuiltInSyncQueries.AllKeys)
        {
            Tables.Add(new DestTableRow
            {
                TableName = key,
                DisplayName = BuiltInSyncQueries.DisplayName(key),
                IsSelected = anyFixed
                    ? saved.Contains(key)
                    : BuiltInSyncQueries.DefaultKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
            });
        }

        RebuildColumnsFromSaved();
        RefreshSummary();
        FormatFileHint =
            "Inventory / products / caratone-sale / caratone-payment (set dates below). " +
            "Marketplace CSV field = csvFile. Token: paste → Save (••••).";

        StatusMessage = _credentials.HasSecret(CredentialKeys.DestinationApiKey(SelectedProject.Id))
            ? "API token saved (••••). Leave as •••• or paste a new token to replace."
            : "No API token yet — paste token then Save.";
    }

    private void RebuildColumnsFromSaved()
    {
        var mapping = _settings.Current.Mapping;
        var savedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SelectedProject is not null)
        {
            foreach (var f in SelectedProject.FormatFields.Where(f => f.Enabled))
            {
                var key = string.IsNullOrWhiteSpace(f.SourceField) ? f.DestinationKey : f.SourceField;
                if (!string.IsNullOrWhiteSpace(key))
                    savedCols.Add(key.Trim());
            }
        }

        _allColumns = new List<DestColumnRow>();
        foreach (var key in BuiltInSyncQueries.AllKeys)
        {
            if (!mapping.TableColumns.TryGetValue(key, out var cols) || cols.Count == 0)
                continue;
            foreach (var col in cols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var full = $"{key}.{col}";
                _allColumns.Add(new DestColumnRow
                {
                    TableName = key,
                    ColumnName = col,
                    IsSelected = savedCols.Count == 0 || savedCols.Contains(full) || savedCols.Contains(col)
                });
            }
        }
        RefreshColumnsView();
    }

    private void RefreshColumnsView()
    {
        Columns.Clear();
        var selected = new HashSet<string>(
            Tables.Where(t => t.IsSelected).Select(t => t.TableName),
            StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allColumns
                     .Where(c => selected.Count == 0 || selected.Contains(c.TableName))
                     .OrderBy(c => c.TableName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(c => c.ColumnName, StringComparer.OrdinalIgnoreCase))
            Columns.Add(c);
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var t = Tables.Count(x => x.IsSelected);
        var sel = new HashSet<string>(Tables.Where(x => x.IsSelected).Select(x => x.TableName), StringComparer.OrdinalIgnoreCase);
        var c = _allColumns.Count(x => x.IsSelected && (sel.Count == 0 || sel.Contains(x.TableName)));
        SelectionSummary = c == 0
            ? $"{t} source(s) selected — all fields will sync"
            : $"{t} source(s), {c} field(s) selected";
    }

    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var t in Tables) t.IsSelected = true;
        RefreshColumnsView();
        StatusMessage = "All 3 sources selected.";
    }

    [RelayCommand]
    private void SelectNoneTables()
    {
        foreach (var t in Tables) t.IsSelected = false;
        RefreshColumnsView();
        StatusMessage = "No sources selected.";
    }

    [RelayCommand]
    private void ApplyTableTicks() => RefreshColumnsView();

    [RelayCommand]
    private async Task LoadFieldsAsync()
    {
        var selected = Tables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Tick at least one source (sales / inventory / payments), then Load fields.";
            return;
        }

        try
        {
            BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);
            StatusMessage = $"Loading fields for {selected.Count} source(s) from database…";
            var prev = new HashSet<string>(
                _allColumns.Where(c => c.IsSelected).Select(c => $"{c.TableName}.{c.ColumnName}"),
                StringComparer.OrdinalIgnoreCase);
            if (prev.Count == 0 && SelectedProject is not null)
            {
                foreach (var f in SelectedProject.FormatFields.Where(f => f.Enabled))
                {
                    var key = string.IsNullOrWhiteSpace(f.SourceField) ? f.DestinationKey : f.SourceField;
                    if (!string.IsNullOrWhiteSpace(key)) prev.Add(key.Trim());
                }
            }

            var next = _allColumns
                .Where(c => !selected.Contains(c.TableName, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var cache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in selected)
            {
                IReadOnlyList<string> cols =
                    BuiltInSyncQueries.IsInventoryFamily(table) ? BuiltInSyncQueries.InventoryHeaders
                    : BuiltInSyncQueries.IsProductsFamily(table) ? BuiltInSyncQueries.ProductHeaders
                    : BuiltInSyncQueries.IsCaratoneSale(table) ? CaratoneSaleSql.Headers
                    : BuiltInSyncQueries.IsCaratonePayment(table) ? CaratonePaymentSql.Headers
                    : await _products.ListTableColumnsAsync(table).ConfigureAwait(true);
                cache[table] = cols.ToList();
                foreach (var col in cols)
                {
                    var full = $"{table}.{col}";
                    next.Add(new DestColumnRow
                    {
                        TableName = table,
                        ColumnName = col,
                        IsSelected = true
                    });
                }
            }

            _allColumns = next;
            _settings.Update(s =>
            {
                BuiltInSyncQueries.EnsureDefaults(s.Mapping);
                foreach (var kv in cache)
                    s.Mapping.TableColumns[kv.Key] = kv.Value;
                s.Mapping.TableColumns[BuiltInSyncQueries.Inventory] = BuiltInSyncQueries.InventoryHeaders.ToList();
                s.Mapping.TableColumns[BuiltInSyncQueries.InventoryTest] = BuiltInSyncQueries.InventoryHeaders.ToList();
                s.Mapping.TableColumns[BuiltInSyncQueries.Products] = BuiltInSyncQueries.ProductHeaders.ToList();
                s.Mapping.TableColumns[BuiltInSyncQueries.ProductsTest] = BuiltInSyncQueries.ProductHeaders.ToList();
                s.Mapping.TableColumns[BuiltInSyncQueries.CaratoneSale] = CaratoneSaleSql.Headers.ToList();
                s.Mapping.TableColumns[BuiltInSyncQueries.CaratonePayment] = CaratonePaymentSql.Headers.ToList();
            });
            await _settings.SaveAsync().ConfigureAwait(true);
            RefreshColumnsView();
            StatusMessage = selected.Any(BuiltInSyncQueries.IsCaratonePayment)
                ? "caratone-payment: 15 payment headers. Set single/range dates → Save → Full Sync."
                : selected.Any(BuiltInSyncQueries.IsCaratoneSale)
                ? "caratone-sale: 25 report headers. Set single/range dates → Save → Full Sync."
                : selected.Any(t => t.Equals(BuiltInSyncQueries.ProductsTest, StringComparison.OrdinalIgnoreCase))
                ? "marketplace-products-test: TOP 3,000. Save → Full Sync to verify API, then use FULL."
                : selected.Any(BuiltInSyncQueries.IsProductsFamily)
                ? "marketplace-products: 35 catalog headers pinned. Save → Full Sync (one CSV)."
                : selected.Any(BuiltInSyncQueries.IsInventoryFamily)
                ? "Inventory fields pinned (8 headers). marketplace-inv-test = 10k test. Save → Full Sync."
                : $"Loaded {_allColumns.Count(c => selected.Contains(c.TableName, StringComparer.OrdinalIgnoreCase))} field(s). Save when ready.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load fields: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAllFields()
    {
        var visible = new HashSet<string>(Columns.Select(c => $"{c.TableName}.{c.ColumnName}"), StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allColumns)
            if (visible.Contains($"{c.TableName}.{c.ColumnName}"))
                c.IsSelected = true;
        RefreshColumnsView();
        StatusMessage = $"All {Columns.Count} field(s) selected.";
    }

    [RelayCommand]
    private void SelectNoneFields()
    {
        var visible = new HashSet<string>(Columns.Select(c => $"{c.TableName}.{c.ColumnName}"), StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allColumns)
            if (visible.Contains($"{c.TableName}.{c.ColumnName}"))
                c.IsSelected = false;
        RefreshColumnsView();
        StatusMessage = "Fields cleared — sync will send all columns of selected sources.";
    }

    [RelayCommand]
    private async Task ExportSelectedCsvAsync()
    {
        var selected = Tables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Tick at least one source, then Export CSV.";
            MessageBox.Show(StatusMessage, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping);

        // Auto path — no folder picker. One CSV per selected source.
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "VallianiDataSync",
            "csv-export",
            stamp);
        Directory.CreateDirectory(folder);

        var batchSize = Math.Clamp(_settings.Current.Sync.BatchSize, 50, 500);
        var ok = 0;
        var fail = 0;
        var totalRows = 0;
        var files = new List<string>();

        if (selected.Any(BuiltInSyncQueries.IsCaratoneDated)
            && (SelectedProject is null || !SelectedProject.TryGetSaleReportDates(out _, out _)))
        {
            StatusMessage = "caratone-sale / caratone-payment needs From date (and To if range). Set dates → Save → Export.";
            MessageBox.Show(StatusMessage, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = $"Exporting {selected.Count} source(s) to Documents\\VallianiDataSync\\csv-export\\{stamp}…";

        foreach (var table in selected)
        {
            string? restoreSql = null;
            try
            {
                if (BuiltInSyncQueries.IsCaratoneDated(table) && SelectedProject is not null
                    && SelectedProject.TryGetSaleReportDates(out var saleFrom, out var saleTo))
                {
                    var mapping = _settings.Current.Mapping;
                    BuiltInSyncQueries.EnsureDefaults(mapping);
                    restoreSql = mapping.TableQueries.TryGetValue(table, out var prev) ? prev : null;
                    mapping.TableQueries[table] = BuiltInSyncQueries.IsCaratonePayment(table)
                        ? CaratonePaymentSql.ApplyDates(CaratonePaymentSql.Template, saleFrom, saleTo)
                        : CaratoneSaleSql.ApplyDates(CaratoneSaleSql.Template, saleFrom, saleTo);
                }

                var rows = new List<Dictionary<string, object?>>();
                await foreach (var batch in _products.StreamTableRowsAsync(table, Math.Max(batchSize, 2000))
                                   .ConfigureAwait(true))
                {
                    rows.AddRange(batch);
                }

                IReadOnlyList<string>? orderedHeaders =
                    BuiltInSyncQueries.IsCaratonePayment(table) ? CaratonePaymentSql.Headers
                    : BuiltInSyncQueries.IsCaratoneSale(table) ? CaratoneSaleSql.Headers
                    : null;
                var projected = orderedHeaders is not null
                    ? MarketplaceCatalogMapper.OrderTemplateColumns(rows, orderedHeaders)
                    : ProjectLocal(table, rows);
                var fileName = $"{table}_{stamp}.csv";
                var path = Path.Combine(folder, fileName);
                var csv = orderedHeaders is not null
                    ? ProductFormatExporter.ToCsvOrdered(projected, orderedHeaders)
                    : ProductFormatExporter.ToCsvAllColumns(projected);
                await File.WriteAllTextAsync(path, csv).ConfigureAwait(true);
                ok++;
                totalRows += projected.Count;
                files.Add(fileName);
                StatusMessage = $"Exported {ok}/{selected.Count}: {fileName} ({projected.Count} rows)…";
            }
            catch (Exception ex)
            {
                fail++;
                var err = ex.Message.Length <= 60 ? ex.Message : ex.Message[..60] + "…";
                files.Add($"{table}: FAILED ({err})");
            }
            finally
            {
                if (restoreSql is not null)
                    _settings.Current.Mapping.TableQueries[table] = restoreSql;
            }
        }

        var result = $"CSV export done: {ok} file(s), {totalRows} row(s)" +
                     (fail > 0 ? $", {fail} failed." : ".") +
                     $"\n\nFolder:\n{folder}\n\nFiles:\n" + string.Join("\n", files);
        StatusMessage = $"CSV export done: {ok} file(s), {totalRows} row(s). Folder: {folder}";
        MessageBox.Show(result, "Export CSV", MessageBoxButton.OK,
            fail > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private List<Dictionary<string, object?>> ProjectLocal(string table, List<Dictionary<string, object?>> rows)
    {
        var fields = _allColumns
            .Where(c => c.IsSelected && c.TableName.Equals(table, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ColumnName)
            .ToList();
        if (fields.Count == 0)
            return rows;
        return rows.Select(r =>
        {
            var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields)
                if (r.TryGetValue(f, out var v))
                    d[f] = v;
            return d;
        }).ToList();
    }

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        if (!await EnsureOtpIfNeededAsync("Admin: create destination").ConfigureAwait(true))
            return;

        var project = new DestinationProject
        {
            Name = $"Project {Projects.Count + 1}",
            IntervalMinutes = 120,
            SyncSources = BuiltInSyncQueries.DefaultKeys.ToList(),
            FormatFields = new List<DestinationFormatField>()
        };
        _settings.Update(s =>
        {
            BuiltInSyncQueries.EnsureDefaults(s.Mapping);
            s.Destinations.Add(project);
        });
        await _settings.SaveAsync();
        Projects.Add(project);
        SelectedProject = project;
        StatusMessage = "Project created. For quick test tick marketplace-inv-test only; then Inventory FULL.";
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

        var selectedTables = Tables.Where(t => t.IsSelected).Select(t => t.TableName)
            .Where(BuiltInSyncQueries.IsFixedSource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedTables.Count == 0)
        {
            StatusMessage = "Select at least one source (sales / inventory / products / caratone-sale / caratone-payment / payments).";
            return;
        }

        if (selectedTables.Any(BuiltInSyncQueries.IsCaratoneDated)
            && !SelectedProject.TryGetSaleReportDates(out _, out _))
        {
            StatusMessage = "caratone-sale / caratone-payment: pick From date (or range + To), then Save.";
            return;
        }

        // Template sources → fixed headers only
        var selectedCols = new List<DestinationFormatField>();
        foreach (var invKey in selectedTables.Where(BuiltInSyncQueries.IsInventoryFamily))
        {
            foreach (var h in BuiltInSyncQueries.InventoryHeaders)
            {
                selectedCols.Add(new DestinationFormatField
                {
                    SourceField = $"{invKey}.{h}",
                    DestinationKey = h,
                    Enabled = true
                });
            }
        }
        foreach (var prodKey in selectedTables.Where(BuiltInSyncQueries.IsProductsFamily))
        {
            foreach (var h in BuiltInSyncQueries.ProductHeaders)
            {
                selectedCols.Add(new DestinationFormatField
                {
                    SourceField = $"{prodKey}.{h}",
                    DestinationKey = h,
                    Enabled = true
                });
            }
        }
        foreach (var saleKey in selectedTables.Where(BuiltInSyncQueries.IsCaratoneSale))
        {
            foreach (var h in CaratoneSaleSql.Headers)
            {
                selectedCols.Add(new DestinationFormatField
                {
                    SourceField = $"{saleKey}.{h}",
                    DestinationKey = h,
                    Enabled = true
                });
            }
        }
        foreach (var payKey in selectedTables.Where(BuiltInSyncQueries.IsCaratonePayment))
        {
            foreach (var h in CaratonePaymentSql.Headers)
            {
                selectedCols.Add(new DestinationFormatField
                {
                    SourceField = $"{payKey}.{h}",
                    DestinationKey = h,
                    Enabled = true
                });
            }
        }
        foreach (var c in _allColumns.Where(c =>
                     c.IsSelected
                     && selectedTables.Contains(c.TableName, StringComparer.OrdinalIgnoreCase)
                     && !BuiltInSyncQueries.IsInventoryFamily(c.TableName)
                     && !BuiltInSyncQueries.IsProductsFamily(c.TableName)
                     && !BuiltInSyncQueries.IsCaratoneDated(c.TableName)))
        {
            selectedCols.Add(new DestinationFormatField
            {
                SourceField = $"{c.TableName}.{c.ColumnName}",
                DestinationKey = c.ColumnName,
                Enabled = true
            });
        }

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
                           || selectedTables.Any(BuiltInSyncQueries.WantsSingleFileSource)
                ? "Csv"
                : (string.IsNullOrWhiteSpace(SelectedProject.BodyFormat) ? "JsonArray" : SelectedProject.BodyFormat);
            p.AuthHeaderName = "Authorization";
            p.AuthHeaderPrefix = "Bearer ";
            p.SyncSources = selectedTables;
            p.FormatFields = selectedCols;
            p.SaleDateIsRange = SelectedProject.SaleDateIsRange;
            p.SaleReportDateFrom = SelectedProject.SaleReportDateFrom;
            p.SaleReportDateTo = SelectedProject.SaleDateIsRange
                ? SelectedProject.SaleReportDateTo
                : SelectedProject.SaleReportDateFrom;
        });

        if (!keepExistingKey)
            _credentials.SaveSecret(CredentialKeys.DestinationApiKey(id), typedKey);

        await _settings.SaveAsync();
        Load();
        SelectedProject = Projects.FirstOrDefault(p => p.Id == id);
        // Keep mask visible so it does not look like the token was deleted
        if (SelectedProject is not null && _credentials.HasSecret(CredentialKeys.DestinationApiKey(id)))
            ApiKey = ApiKeySavedMask;

        StatusMessage = _credentials.HasSecret(CredentialKeys.DestinationApiKey(id))
            ? selectedTables.Any(BuiltInSyncQueries.IsProductsFamily)
                ? "Saved. Token on file (••••). marketplace-products: 35-col catalog → one CSV Full Sync."
                : $"Saved. Token on file (••••). Inventory headers: {string.Join(", ", BuiltInSyncQueries.InventoryHeaders)}."
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

        // Persist current ticks so sync matches what you see on screen
        var selectedTables = Tables.Where(t => t.IsSelected).Select(t => t.TableName)
            .Where(BuiltInSyncQueries.IsFixedSource)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedTables.Count == 0)
        {
            StatusMessage = "Tick at least one source (sales / inventory / payments), then Sync.";
            MessageBox.Show(StatusMessage, "Destination sync", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedCols = _allColumns
            .Where(c => c.IsSelected && selectedTables.Contains(c.TableName, StringComparer.OrdinalIgnoreCase))
            .Select(c => new DestinationFormatField
            {
                SourceField = $"{c.TableName}.{c.ColumnName}",
                DestinationKey = c.ColumnName,
                Enabled = true
            })
            .ToList();

        var id = SelectedProject.Id;
        var apiUrl = SelectedProject.ApiUrl.Trim();
        // Marketplace import URLs expect CSV upload; otherwise keep JsonArray
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
            p.SyncSources = selectedTables;
            p.FormatFields = selectedCols; // empty = all columns of selected sources
        });
        await _settings.SaveAsync();

        StatusMessage = fullSync
            ? $"Full sync {selectedTables.Count} source(s) to live API…"
            : $"Syncing {selectedTables.Count} source(s) to live API…";

        var progress = new Progress<string>(msg =>
        {
            StatusMessage = msg;
            if (SelectedProject is not null && SelectedProject.Id == id)
                SelectedProject.LastSyncMessage = msg;
        });
        var (ok, message, sent, skipped) = await _sync.SyncProjectAsync(id, fullSync: fullSync, progress: progress);
        // message = full SYNC REPORT (per-source OK / SKIPPED / FAILED + why)
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
