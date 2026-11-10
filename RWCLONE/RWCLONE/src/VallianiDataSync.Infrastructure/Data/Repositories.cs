using System.Data.Common;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Mapping;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Core.Options;
using VallianiDataSync.Infrastructure.Mapping;

namespace VallianiDataSync.Infrastructure.Data;

public sealed class LocalDatabaseService : ILocalDatabaseService
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly ILogger<LocalDatabaseService> _logger;

    public LocalDatabaseService(DatabaseConnectionFactory factory, ILogger<LocalDatabaseService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return (true, $"Connected successfully via {_factory.Provider} (result={result}).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection test failed");
            return (false, ex.Message);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var (ok, _) = await TestConnectionAsync(cancellationToken).ConfigureAwait(false);
        return ok;
    }
}

public sealed class ProductRepository : IProductRepository
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProductRepository> _logger;

    public ProductRepository(DatabaseConnectionFactory factory, ISettingsService settings, ILogger<ProductRepository> logger)
    {
        _factory = factory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> CountProductsAsync(string? storeCode, CancellationToken cancellationToken = default)
    {
        var mapping = _settings.Current.Mapping;
        var table = Q(mapping.ProductsTable ?? "ItemMaster");
        var storeColumnName = GetMappedColumn(mapping, "Store");

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        if (storeColumnName is null || string.IsNullOrWhiteSpace(storeCode))
        {
            cmd.CommandText = $"SELECT COUNT(1) FROM {table}";
        }
        else
        {
            cmd.CommandText = $"SELECT COUNT(1) FROM {table} WHERE {Q(storeColumnName)} = @StoreCode";
            AddParam(cmd, "@StoreCode", storeCode);
        }
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<ProductRecord>> GetProductsAsync(
        string? storeCode,
        DateTime? modifiedSinceUtc,
        int take,
        int skip,
        CancellationToken cancellationToken = default)
    {
        var mapping = _settings.Current.Mapping;
        var mapper = new ConfigurableProductMapper(mapping);
        var table = Q(mapping.ProductsTable ?? "ItemMaster");
        var storeColumnName = GetMappedColumn(mapping, "Store");
        var modifiedColumn = string.IsNullOrWhiteSpace(mapping.ModifiedAtColumn)
            ? null
            : Q(mapping.ModifiedAtColumn);
        var defaultStore = storeCode
            ?? _settings.Current.Sync.SelectedStoreCode
            ?? "MAIN";

        var where = new List<string>();
        if (storeColumnName is not null && !string.IsNullOrWhiteSpace(storeCode))
            where.Add($"{Q(storeColumnName)} = @StoreCode");
        if (modifiedColumn is not null && modifiedSinceUtc is not null)
            where.Add($"{modifiedColumn} > @ModifiedSince");

        string sql;
        if (_factory.Provider == DatabaseProviderKind.MySql)
        {
            sql = $"SELECT * FROM {table}";
            if (where.Count > 0)
                sql += " WHERE " + string.Join(" AND ", where);
            sql += " LIMIT @Take OFFSET @Skip";
        }
        else
        {
            sql = $"SELECT * FROM {table}";
            if (where.Count > 0)
                sql += " WHERE " + string.Join(" AND ", where);
            sql += " ORDER BY (SELECT NULL) OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";
        }

        var list = new List<ProductRecord>();
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        if (storeColumnName is not null && !string.IsNullOrWhiteSpace(storeCode))
            AddParam(cmd, "@StoreCode", storeCode);
        AddParam(cmd, "@Skip", skip);
        AddParam(cmd, "@Take", take);
        if (modifiedColumn is not null && modifiedSinceUtc is not null)
            AddParam(cmd, "@ModifiedSince", modifiedSinceUtc);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var product = mapper.MapRow(reader);
                if (string.IsNullOrWhiteSpace(product.StoreCode))
                    product.StoreCode = defaultStore;
                if (!string.IsNullOrWhiteSpace(product.Sku))
                    list.Add(product);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product query failed for store {StoreCode}", storeCode);
            throw;
        }

        return list;
    }

    public async Task<IReadOnlyList<string>> ListTableColumnsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        string schema = "";
        string table = tableName;
        if (tableName.Contains('.', StringComparison.Ordinal))
        {
            var parts = tableName.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                schema = SanitizeIdentifier(parts[0]);
                table = SanitizeIdentifier(parts[1]);
            }
        }
        var safeTable = SanitizeIdentifier(table);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        if (_factory.Provider == DatabaseProviderKind.MySql)
        {
            cmd.CommandText = @"SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t
ORDER BY ORDINAL_POSITION";
            AddParam(cmd, "@t", safeTable);
        }
        else if (!string.IsNullOrEmpty(schema))
        {
            cmd.CommandText = @"SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t
ORDER BY ORDINAL_POSITION";
            AddParam(cmd, "@s", schema);
            AddParam(cmd, "@t", safeTable);
        }
        else
        {
            cmd.CommandText = @"SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @t
ORDER BY ORDINAL_POSITION";
            AddParam(cmd, "@t", safeTable);
        }

        var cols = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            cols.Add(reader.GetString(0));

        // MySQL table names can be case-sensitive depending on OS; try lowercase if empty
        if (cols.Count == 0 && _factory.Provider == DatabaseProviderKind.MySql
            && !string.Equals(safeTable, safeTable.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return await ListTableColumnsAsync(safeTable.ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
        }

        return cols;
    }

    public async Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();

        if (_factory.Provider == DatabaseProviderKind.MySql)
        {
            cmd.CommandText = @"
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME";
        }
        else
        {
            cmd.CommandText = @"
SELECT
  CASE WHEN TABLE_SCHEMA = N'dbo' THEN TABLE_NAME
       ELSE TABLE_SCHEMA + N'.' + TABLE_NAME END AS TableName
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
  AND TABLE_CATALOG = DB_NAME()
  AND TABLE_SCHEMA NOT IN (N'sys', N'INFORMATION_SCHEMA', N'guest')
  AND TABLE_NAME NOT IN (N'sysdiagrams')
ORDER BY TABLE_SCHEMA, TABLE_NAME";
        }

        cmd.CommandTimeout = 300;
        var tables = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            tables.Add(reader.GetString(0));
        return tables;
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> ReadTableRowsAsync(
        string tableName,
        int take,
        int skip,
        CancellationToken cancellationToken = default)
    {
        var table = Q(tableName);
        take = Math.Clamp(take, 1, 2000);
        skip = Math.Max(0, skip);

        string sql;
        if (_factory.Provider == DatabaseProviderKind.MySql)
            sql = $"SELECT * FROM {table} LIMIT @Take OFFSET @Skip";
        else
            sql = $"SELECT * FROM {table} ORDER BY (SELECT NULL) OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

        var rows = new List<Dictionary<string, object?>>();
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        AddParam(cmd, "@Skip", skip);
        AddParam(cmd, "@Take", take);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (value is DateTime dt)
                    value = dt.ToString("o");
                else if (value is DateTimeOffset dto)
                    value = dto.ToString("o");
                else if (value is byte[] bytes)
                    value = Convert.ToBase64String(bytes);
                row[name] = value;
            }
            rows.Add(row);
        }
        return rows;
    }

    private static string? GetMappedColumn(FieldMappingConfig mapping, string logical)
    {
        if (!mapping.Columns.TryGetValue(logical, out var column) || string.IsNullOrWhiteSpace(column))
            return null;
        return column.Trim();
    }

    private string Q(string name)
    {
        if (name.Contains('.', StringComparison.Ordinal))
        {
            var parts = name.Split('.', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new InvalidOperationException("Invalid schema.table identifier.");
            var schema = SanitizeIdentifier(parts[0]);
            var table = SanitizeIdentifier(parts[1]);
            return _factory.Provider == DatabaseProviderKind.MySql
                ? $"`{schema}`.`{table}`"
                : $"[{schema}].[{table}]";
        }

        var safe = SanitizeIdentifier(name);
        return _factory.Provider == DatabaseProviderKind.MySql ? $"`{safe}`" : $"[{safe}]";
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // ponytail: identifier whitelist keeps dynamic table/column names injectable-safe without a full SQL AST parser.
    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            throw new InvalidOperationException("Invalid SQL identifier.");
        foreach (var ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '$' or '#'))
                throw new InvalidOperationException($"Invalid SQL identifier character in '{name}'.");
        }
        return name;
    }
}

public sealed class InventoryRepository : IInventoryRepository
{
    private readonly IProductRepository _products;

    public InventoryRepository(IProductRepository products) => _products = products;

    public Task<IReadOnlyList<ProductRecord>> GetInventoryAsync(
        string? storeCode,
        DateTime? modifiedSinceUtc,
        int take,
        int skip,
        CancellationToken cancellationToken = default)
        => _products.GetProductsAsync(storeCode, modifiedSinceUtc, take, skip, cancellationToken);
}
