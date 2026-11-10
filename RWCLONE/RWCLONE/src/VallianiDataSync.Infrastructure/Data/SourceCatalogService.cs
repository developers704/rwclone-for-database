using Microsoft.Data.SqlClient;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.Data;

/// <summary>
/// Reads SQL Server metadata only. It never creates, updates, or deletes source data.
/// </summary>
public sealed class SourceCatalogService : ISourceCatalogService
{
    private readonly DatabaseConnectionFactory _factory;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private SourceCatalog? _cached;
    private DateTime _cachedAtUtc;

    public SourceCatalogService(DatabaseConnectionFactory factory) => _factory = factory;

    public async Task<SourceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromMinutes(10))
            return _cached;

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAtUtc < TimeSpan.FromMinutes(10))
                return _cached;

            var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
            _cached = catalog;
            _cachedAtUtc = DateTime.UtcNow;
            return catalog;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<SourceCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        if (_factory.Provider != VallianiDataSync.Core.Options.DatabaseProviderKind.SqlServer)
            throw new InvalidOperationException("Source catalog currently requires SQL Server metadata.");

        var catalog = new SourceCatalog();
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string tableSql = """
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME;
            """;
        await using (var command = new SqlCommand(tableSql, (SqlConnection)connection) { CommandTimeout = 60 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                catalog.Tables.Add(new SourceTable { Schema = reader.GetString(0), Name = reader.GetString(1) });
        }

        const string columnSql = """
            SELECT TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;
            """;
        await using (var command = new SqlCommand(columnSql, (SqlConnection)connection) { CommandTimeout = 60 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                var target = catalog.Tables.FirstOrDefault(t => t.Schema == schema && t.Name == table);
                if (target is null) continue;
                var name = reader.GetString(3);
                target.Columns.Add(new SourceColumn
                {
                    Name = name,
                    Ordinal = reader.GetInt32(2),
                    DataType = reader.GetString(4),
                    IsNullable = string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase),
                    FieldId = $"{schema}.{table}.{name}"
                });
            }
        }

        const string relationshipSql = """
            SELECT fk.name,
                   SCHEMA_NAME(parent.schema_id) + '.' + parent.name,
                   parentColumn.name,
                   SCHEMA_NAME(referenced.schema_id) + '.' + referenced.name,
                   referencedColumn.name
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            INNER JOIN sys.tables parent ON fkc.parent_object_id = parent.object_id
            INNER JOIN sys.columns parentColumn ON parentColumn.object_id = parent.object_id AND parentColumn.column_id = fkc.parent_column_id
            INNER JOIN sys.tables referenced ON fkc.referenced_object_id = referenced.object_id
            INNER JOIN sys.columns referencedColumn ON referencedColumn.object_id = referenced.object_id AND referencedColumn.column_id = fkc.referenced_column_id
            ORDER BY parent.name, referenced.name, fk.name;
            """;
        await using (var command = new SqlCommand(relationshipSql, (SqlConnection)connection) { CommandTimeout = 60 })
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                catalog.Relationships.Add(new SourceRelationship
                {
                    Name = reader.GetString(0),
                    FromTable = reader.GetString(1),
                    FromColumn = reader.GetString(2),
                    ToTable = reader.GetString(3),
                    ToColumn = reader.GetString(4)
                });
        }

        var primaryKeys = await ReadPrimaryKeysAsync((SqlConnection)connection, cancellationToken).ConfigureAwait(false);
        foreach (var table in catalog.Tables)
        foreach (var column in table.Columns)
            column.IsPrimaryKey = primaryKeys.Contains($"{table.Schema}.{table.Name}.{column.Name}");

        return catalog;
    }

    private static async Task<HashSet<string>> ReadPrimaryKeysAsync(SqlConnection connection, CancellationToken token)
    {
        const string sql = """
            SELECT s.name + '.' + t.name + '.' + c.name
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.tables t ON t.object_id = i.object_id
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 1;
            """;
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(reader.GetString(0));
        return result;
    }
}
