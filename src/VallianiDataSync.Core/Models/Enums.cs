namespace VallianiDataSync.Core.Models;

public enum SyncMode
{
    Incremental = 0,
    Full = 1
}

public enum SyncChannel
{
    Products = 1,
    Inventory = 2,
    Prices = 4,
    Images = 8
}

[Flags]
public enum SyncChannels
{
    None = 0,
    Products = 1,
    Inventory = 2,
    Prices = 4,
    Images = 8,
    All = Products | Inventory | Prices | Images
}

public enum SyncJobStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
    Cancelled
}

public enum LogLevelKind
{
    Trace,
    Debug,
    Information,
    Warning,
    Error
}
