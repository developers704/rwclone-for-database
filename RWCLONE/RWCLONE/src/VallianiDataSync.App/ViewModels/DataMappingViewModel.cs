using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.App.ViewModels;

public partial class MappingRow : ObservableObject
{
    [ObservableProperty] private string _logicalField = string.Empty;
    [ObservableProperty] private string _columnName = string.Empty;
}

public partial class TablePickRow : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _tableName = string.Empty;
}

public partial class DataMappingViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IProductRepository _products;

    public ObservableCollection<MappingRow> Rows { get; } = new();
    public ObservableCollection<string> DiscoveredColumns { get; } = new();
    public ObservableCollection<TablePickRow> Tables { get; } = new();

    [ObservableProperty] private string _productsTable = "products";
    [ObservableProperty] private string _modifiedAtColumn = string.Empty;
    [ObservableProperty] private bool _syncAllTables = true;
    [ObservableProperty] private string _statusMessage = string.Empty;

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
        Tables.Clear();
        foreach (var c in mapping.DiscoveredColumns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            DiscoveredColumns.Add(c);

        var selected = new HashSet<string>(mapping.SelectedTables, StringComparer.OrdinalIgnoreCase);
        foreach (var t in mapping.DiscoveredTables.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            Tables.Add(new TablePickRow { TableName = t, IsSelected = selected.Count == 0 || selected.Contains(t) });

        var defaults = new FieldMappingConfig().Columns.Keys
            .Concat(mapping.Columns.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k);
        foreach (var key in defaults)
        {
            mapping.Columns.TryGetValue(key, out var column);
            Rows.Add(new MappingRow { LogicalField = key, ColumnName = column ?? string.Empty });
        }
    }

    [RelayCommand]
    private async Task LoadAllTablesAsync()
    {
        try
        {
            StatusMessage = "Loading tables…";
            var tables = await _products.ListTablesAsync();
            if (tables.Count == 0)
            {
                StatusMessage = "No tables found. Check Database connection.";
                return;
            }

            Tables.Clear();
            foreach (var t in tables)
                Tables.Add(new TablePickRow { TableName = t, IsSelected = true });

            _settings.Update(s =>
            {
                s.Mapping.DiscoveredTables = tables.ToList();
                s.Mapping.SelectedTables = tables.ToList();
                s.Mapping.SyncAllTables = true;
            });
            SyncAllTables = true;
            await _settings.SaveAsync();
            StatusMessage = $"Loaded {tables.Count} tables. All selected for sync. Click Save if you change selection.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectAllTables()
    {
        foreach (var t in Tables)
            t.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAllTables()
    {
        foreach (var t in Tables)
            t.IsSelected = false;
    }

    [RelayCommand]
    private async Task LoadAllColumnsAsync()
    {
        try
        {
            StatusMessage = "Loading columns A→Z...";
            var table = string.IsNullOrWhiteSpace(ProductsTable) ? "products" : ProductsTable.Trim();
            var cols = await _products.ListTableColumnsAsync(table);
            if (cols.Count == 0)
            {
                StatusMessage = $"No columns found for table '{table}'. Check Database connection + table name.";
                return;
            }

            DiscoveredColumns.Clear();
            foreach (var c in cols)
                DiscoveredColumns.Add(c);

            AutoMap(cols);

            if (cols.Any(c => c.Equals("updated_at", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(ModifiedAtColumn))
                ModifiedAtColumn = "updated_at";

            _settings.Update(s =>
            {
                s.Mapping.ProductsTable = table;
                s.Mapping.DiscoveredColumns = cols.ToList();
                s.Mapping.ModifiedAtColumn = string.IsNullOrWhiteSpace(ModifiedAtColumn) ? null : ModifiedAtColumn.Trim();
                s.Mapping.Columns = Rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.ColumnName))
                    .ToDictionary(r => r.LogicalField, r => r.ColumnName.Trim(), StringComparer.OrdinalIgnoreCase);
            });
            await _settings.SaveAsync();
            StatusMessage = $"Loaded {cols.Count} columns A→Z from '{table}'.";
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
        var selected = Tables.Where(t => t.IsSelected).Select(t => t.TableName).ToList();
        _settings.Update(s =>
        {
            s.Mapping.ProductsTable = ProductsTable.Trim();
            s.Mapping.ModifiedAtColumn = string.IsNullOrWhiteSpace(ModifiedAtColumn) ? null : ModifiedAtColumn.Trim();
            s.Mapping.DiscoveredColumns = DiscoveredColumns.ToList();
            s.Mapping.DiscoveredTables = Tables.Select(t => t.TableName).ToList();
            s.Mapping.SyncAllTables = SyncAllTables;
            s.Mapping.SelectedTables = SyncAllTables ? new List<string>() : selected;
            s.Mapping.Columns = Rows
                .Where(r => !string.IsNullOrWhiteSpace(r.ColumnName))
                .ToDictionary(r => r.LogicalField, r => r.ColumnName.Trim(), StringComparer.OrdinalIgnoreCase);
        });
        await _settings.SaveAsync();
        StatusMessage = SyncAllTables
            ? "Saved. Sync ALL tables ON — Destinations Sync Now discovers and sends every DB table (2600+ OK)."
            : $"Saved. {selected.Count} selected table(s) will sync.";
    }
}
