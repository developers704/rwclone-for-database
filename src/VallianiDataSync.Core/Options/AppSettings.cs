using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Options;

public sealed class AppSettings
{
    public DatabaseOptions Database { get; set; } = new();
    public ApiOptions Api { get; set; } = new();
    public SyncOptions Sync { get; set; } = new();
    public ImageOptions Images { get; set; } = new();
    public FieldMappingConfig Mapping { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
    public List<StoreRecord> Stores { get; set; } = new();
    public List<DestinationProject> Destinations { get; set; } = new();
    public string LocalStateDirectory { get; set; } = "data";
    public string LogsDirectory { get; set; } = "logs";
}
