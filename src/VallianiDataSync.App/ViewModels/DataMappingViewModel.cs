using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Infrastructure.Destinations;

namespace VallianiDataSync.App.ViewModels;

public partial class MappingRow : ObservableObject
{
    [ObservableProperty] private string _logicalField = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
}

public partial class TablePickRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _tableName = string.Empty;
    /// <summary>Empty = send same source name. Filled = rename for destination API.</summary>
    [ObservableProperty] private string _destinationName = string.Empty;
}

public partial class ColumnPickRow : ObservableObject
{
    [ObservableProperty] private string _tableName = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
}

public partial class DataMappingViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IProductRepository _products;
    private List<TablePickRow> _allTables = new();

    public ObservableCollection<MappingRow> Rows { get; } = new();
    public ObservableCollection<string> DiscoveredColumns { get; } = new();
    public ObservableCollection<TablePickRow> Tables { get; } = new();
    public ObservableCollection<ColumnPickRow> SelectedTableColumns { get; } = new();

    [ObservableProperty] private string _productsTable = "products";
    [ObservableProperty] private string _modifiedAtColumn = string.Empty;
    [ObservableProperty] private bool _syncAllTables;
    [ObservableProperty] private string _tableFilter = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _selectedSummary = "0 tables selected";

    public DataMappingViewModel(ISettingsService settings, IProductRepository products)
    {
        _settings = settings;
        _products = products;
        Load();
    }

    public void Load()
    {
        var mapping = _settings.Current.Mapping;
        ProductsTable = mapping.ProductsTable ?? "products";
        ModifiedAtColumn = mapping.ModifiedAtColumn ?? string.Empty;
        SyncAllTables = mapping.SyncAllTables;
        Rows.Clear();
        DiscoveredColumns.Clear();
        SelectedTableColumns.Clear();
        Tables.Clear();

        foreach (var c in mapping.DiscoveredColumns
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            DiscoveredColumns.Add(c);

        var selected = new HashSet<string>(
            mapping.SelectedTables.Where(x => !string.IsNullOrWhiteSpace(x)),
            StringComparer.OrdinalIgnoreCase);
        _allTables = mapping.DiscoveredTables
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(t =>
            {
                mapping.TableAliases.TryGetValue(t, out var alias);
                return new TablePickRow
                {
                    TableName = t,
                    IsSelected = selected.Contains(t),
                    DestinationName = alias ?? string.Empty
                };
            })
            .ToList();
        ApplyTableFilter();

        foreach (var kv in mapping.TableColumns
                     .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value is not null)
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var col in kv.Value.Where(c => !string.IsNullOrWhiteSpace(c))
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                SelectedTableColumns.Add(new ColumnPickRow { TableName = kv.Key, ColumnName = col });
        }

        var defaults = new FieldMappingConfig().Columns.Keys
            .Concat(mapping.Columns.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k);
        foreach (var key in defaults)
        {
            mapping.Columns.TryGetValue(key, out var column);
            Rows.Add(new MappingRow { LogicalField = key, ColumnName = column ?? string.Empty });
        }

        RefreshSelectedSummary();
    }

    partial void OnTableFilterChanged(string value) => ApplyTableFilter();

    private void ApplyTableFilter()
    {
        Tables.Clear();
        var q = (TableFilter ?? string.Empty).Trim();
        var view = string.IsNullOrEmpty(q)
            ? _allTables
            : _allTables.Where(t => t.TableName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var t in view)
            Tables.Add(t);
        RefreshSelectedSummary();
    }

    private void RefreshSelectedSummary()
    {
        var n = _allTables.Count(t => t.IsSelected);
        SelectedSummary = SyncAllTables
            ? $"Sync ALL ON — every DB table will sync ({_allTables.Count} discovered)"
            : $"{n} of {_allTables.Count} table(s) selected for sync";
    }

    [RelayCommand]
    private async Task LoadAllTablesAsync()
    {
        try
        {
            StatusMessage = "Loading tables from database…";
            var tables = await _products.ListTablesAsync();
            if (tables.Count == 0)
            {
                StatusMessage = "No tables found. Check Database connection (CHAINARY / iTransactDB).";
                return;
            }

            // Keep previous selection + destination rename where possible
            var prevSel = new HashSet<string>(
                _allTables.Where(t => t.IsSelected).Select(t => t.TableName),
                StringComparer.OrdinalIgnoreCase);
            var prevAlias = _allTables
                .Where(t => !string.IsNullOrWhiteSpace(t.DestinationName))
                .ToDictionary(t => t.TableName, t => t.DestinationName.Trim(), StringComparer.OrdinalIgnoreCase);

            BuiltInSyncQueries.EnsureDefaults(_settings.Current.Mapping, tables);
            var aliasDefaults = _settings.Current.Mapping.TableAliases;
            _allTables = tables
                .OrderBy(x => BuiltInSyncQueries.AllKeys.Contains(x, StringComparer.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(t =>
                {
                    var dest = prevAlias.TryGetValue(t, out var a) ? a
                        : (aliasDefaults.TryGetValue(t, out var d) ? d : string.Empty);
                    return new TablePickRow
                    {
                        TableName = t,
                        IsSelected = prevSel.Contains(t),
                        DestinationName = dest ?? string.Empty
                    };
                })
                .ToList();
            SyncAllTables = false;
            ApplyTableFilter();

            _settings.Update(s =>
            {
                BuiltInSyncQueries.EnsureDefaults(s.Mapping, tables);
                s.Mapping.DiscoveredTables = tables.ToList();
                s.Mapping.SyncAllTables = false;
                s.Mapping.SelectedTables = _allTables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
                s.Mapping.TableAliases = CollectAliases();
            });
            await _settings.SaveAsync();
            StatusMessage =
                $"Loaded {tables.Count} sources (including sales / inventory / payments). Use Setup inventory + sales, or Destinations for sync.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetupInventorySalesAsync()
    {
        try
        {
            StatusMessage = "Setting up sales + inventory + payments…";
            var tables = await _products.ListTablesAsync();
            _settings.Update(s =>
            {
                BuiltInSyncQueries.EnableNamedSources(s.Mapping, tables);
                s.Mapping.DiscoveredTables = tables.ToList();
            });
            await _settings.SaveAsync();

            var mapping = _settings.Current.Mapping;
            var selected = new HashSet<string>(mapping.SelectedTables, StringComparer.OrdinalIgnoreCase);
            _allTables = tables
                .OrderBy(x => BuiltInSyncQueries.AllKeys.Contains(x, StringComparer.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(t =>
                {
                    mapping.TableAliases.TryGetValue(t, out var alias);
                    return new TablePickRow
                    {
                        TableName = t,
                        IsSelected = selected.Contains(t),
                        DestinationName = alias ?? string.Empty
                    };
                })
                .ToList();
            SyncAllTables = false;
            ApplyTableFilter();

            StatusMessage =
                "Ready: sales + inventory + payments selected. " +
                "Save. Then each Destinations project picks sources + fields and Sync Now.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Setup failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var t in Tables)
            t.IsSelected = true;
        RefreshSelectedSummary();
    }

    [RelayCommand]
    private void ClearAllTables()
    {
        foreach (var t in _allTables)
            t.IsSelected = false;
        RefreshSelectedSummary();
    }

    [RelayCommand]
    private async Task LoadAllColumnsAsync()
    {
        try
        {
            var selected = _allTables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
            if (selected.Count == 0 && !SyncAllTables)
            {
                StatusMessage = "Select at least one table first (or turn on Sync ALL).";
                return;
            }

            // Sync ALL → use products table name only for column mapping convenience
            if (SyncAllTables && selected.Count == 0)
            {
                var single = string.IsNullOrWhiteSpace(ProductsTable) ? "products" : ProductsTable.Trim();
                selected = new List<string> { single };
            }

            StatusMessage = $"Loading columns from {selected.Count} selected table(s)…";
            var tableCols = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fails = new List<string>();

            foreach (var table in selected)
            {
                try
                {
                    var cols = await _products.ListTableColumnsAsync(table);
                    if (cols.Count == 0)
                    {
                        fails.Add($"{table}: no columns");
                        continue;
                    }
                    tableCols[table] = cols.ToList();
                    foreach (var c in cols)
                        merged.Add(c);
                }
                catch (Exception ex)
                {
                    fails.Add($"{table}: {ex.Message}");
                }
            }

            if (merged.Count == 0)
            {
                StatusMessage = fails.Count > 0
                    ? "Failed to load columns. " + string.Join("; ", fails.Take(3))
                    : "No columns found. Check Database connection.";
                return;
            }

            var ordered = merged.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            DiscoveredColumns.Clear();
            foreach (var c in ordered)
                DiscoveredColumns.Add(c);

            SelectedTableColumns.Clear();
            foreach (var kv in tableCols.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var col in kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    SelectedTableColumns.Add(new ColumnPickRow { TableName = kv.Key, ColumnName = col });
            }

            AutoMap(ordered);

            if (string.IsNullOrWhiteSpace(ProductsTable) || ProductsTable.Equals("products", StringComparison.OrdinalIgnoreCase))
                ProductsTable = selected[0];

            if (ordered.Any(c => c.Equals("updated_at", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(ModifiedAtColumn))
                ModifiedAtColumn = "updated_at";

            // Auto identity map: every discovered column syncs by name
            foreach (var c in ordered)
            {
                if (!Rows.Any(r => r.LogicalField.Equals(c, StringComparison.OrdinalIgnoreCase)))
                    Rows.Add(new MappingRow { LogicalField = c, ColumnName = c });
                else
                {
                    var row = Rows.First(r => r.LogicalField.Equals(c, StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrWhiteSpace(row.ColumnName))
                        row.ColumnName = c;
                }
            }

            SyncAllTables = false;
            _settings.Update(s =>
            {
                s.Mapping.ProductsTable = ProductsTable.Trim();
                s.Mapping.ModifiedAtColumn = string.IsNullOrWhiteSpace(ModifiedAtColumn) ? null : ModifiedAtColumn.Trim();
                s.Mapping.DiscoveredColumns = ordered;
                s.Mapping.TableColumns = tableCols.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value,
                    StringComparer.OrdinalIgnoreCase);
                s.Mapping.DiscoveredTables = _allTables.Select(t => t.TableName).ToList();
                s.Mapping.SelectedTables = _allTables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
                s.Mapping.TableAliases = CollectAliases();
                s.Mapping.SyncAllTables = false;
                s.Mapping.Columns = Rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.ColumnName))
                    .ToDictionary(r => r.LogicalField, r => r.ColumnName.Trim(), StringComparer.OrdinalIgnoreCase);
            });
            await _settings.SaveAsync();
            RefreshSelectedSummary();

            var msg = $"Loaded {ordered.Count} columns from {tableCols.Count} table(s). All columns auto-mapped for sync.";
            if (fails.Count > 0)
                msg += " Issues: " + string.Join("; ", fails.Take(5));
            StatusMessage = msg;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
    }

    private void AutoMap(IReadOnlyList<string> cols)
    {
        var set = cols.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);
        void Map(string logical, params string[] candidates)
        {
            var row = Rows.FirstOrDefault(r => r.LogicalField.Equals(logical, StringComparison.OrdinalIgnoreCase));
            if (row is null)
                return;
            foreach (var c in candidates)
            {
                if (set.TryGetValue(c, out var real))
                {
                    row.ColumnName = real;
                    return;
                }
            }
        }

        Map("Sku", "sku", "SKU");
        Map("ItemNumber", "vendor_model", "item_number", "ItemNumber");
        Map("Name", "description", "name", "ProductName", "title");
        Map("Description", "description", "Description");
        Map("Category", "category_id", "category", "Category");
        Map("Brand", "brand_id", "brand", "Brand");
        Map("Vendor", "vendor", "Vendor");
        Map("Cost", "cost", "Cost");
        Map("RetailPrice", "price", "retail_price", "RetailPrice");
        Map("SalePrice", "sale_price", "SalePrice");
        Map("Quantity", "quantity", "qty", "Quantity");
        Map("Store", "store", "store_code", "StoreCode");
        Map("Barcode", "sku", "barcode", "Barcode");
        Map("Metal", "metal_type", "metal", "Metal");
        Map("Stone", "stone", "Stone");
        Map("Shape", "shape", "Shape");
        Map("Weight", "avg_weight", "weight", "Weight");
        Map("ImagePath", "image_url", "image", "ImagePath");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var selected = _allTables.Where(t => t.IsSelected).Select(t => t.TableName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!SyncAllTables && selected.Count == 0)
        {
            StatusMessage = "Select at least one table, or turn on Sync ALL database tables.";
            return;
        }

        var aliases = CollectAliases();
        var renamed = aliases.Count;
        _settings.Update(s =>
        {
            s.Mapping.ProductsTable = ProductsTable.Trim();
            s.Mapping.ModifiedAtColumn = string.IsNullOrWhiteSpace(ModifiedAtColumn) ? null : ModifiedAtColumn.Trim();
            s.Mapping.DiscoveredColumns = DiscoveredColumns.ToList();
            s.Mapping.DiscoveredTables = _allTables.Select(t => t.TableName).ToList();
            s.Mapping.SyncAllTables = SyncAllTables;
            s.Mapping.SelectedTables = SyncAllTables ? new List<string>() : selected;
            s.Mapping.TableAliases = aliases;
            BuiltInSyncQueries.EnsureDefaults(s.Mapping, s.Mapping.DiscoveredTables);
            s.Mapping.Columns = Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ColumnName))
                .ToDictionary(r => r.LogicalField, r => r.ColumnName.Trim(), StringComparer.OrdinalIgnoreCase);
        });
        await _settings.SaveAsync();
        RefreshSelectedSummary();
        StatusMessage = SyncAllTables
            ? "Saved. Sync ALL ON — Destinations Sync Now sends every DB table."
            : $"Saved. {selected.Count} source(s) will sync; {renamed} renamed for destination (empty Destination name = same name).";
    }

    private Dictionary<string, string> CollectAliases()
        => _allTables
            .Where(t => !string.IsNullOrWhiteSpace(t.DestinationName)
                        && !t.DestinationName.Trim().Equals(t.TableName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(t => t.TableName, t => t.DestinationName.Trim(), StringComparer.OrdinalIgnoreCase);
}
