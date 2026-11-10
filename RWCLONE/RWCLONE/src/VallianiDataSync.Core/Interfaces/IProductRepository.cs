using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface IProductRepository
{
    Task<IReadOnlyList<ProductRecord>> GetProductsAsync(
        string? storeCode,
        DateTime? modifiedSinceUtc,
        int take,
        int skip,
        CancellationToken cancellationToken = default);

    Task<int> CountProductsAsync(string? storeCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListTableColumnsAsync(string tableName, CancellationToken cancellationToken = default);

    /// <summary>User tables only (excludes system schemas).</summary>
    Task<IReadOnlyList<string>> ListTablesAsync(CancellationToken cancellationToken = default);

    /// <summary>Paged raw rows for multi-table sync.</summary>
    Task<IReadOnlyList<Dictionary<string, object?>>> ReadTableRowsAsync(
        string tableName,
        int take,
        int skip,
        CancellationToken cancellationToken = default);
}
