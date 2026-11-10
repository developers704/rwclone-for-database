using VallianiDataSync.Core.Models;
using VallianiDataSync.Core.Options;

namespace VallianiDataSync.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    void Update(Action<AppSettings> mutate);
}
