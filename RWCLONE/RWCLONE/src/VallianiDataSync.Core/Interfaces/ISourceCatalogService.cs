using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Interfaces;

public interface ISourceCatalogService
{
    Task<SourceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default);
}
