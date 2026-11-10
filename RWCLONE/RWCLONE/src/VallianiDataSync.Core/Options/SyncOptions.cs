using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Core.Options;

public sealed class SyncOptions
{
    public bool ManualOnly { get; set; } = true;
    public bool AutomaticEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 15;
    public bool UseDailySchedule { get; set; }
    public TimeSpan? DailyAtLocalTime { get; set; }
    public SyncMode DefaultMode { get; set; } = SyncMode.Incremental;
    public SyncChannels Channels { get; set; } = SyncChannels.All;
    public int BatchSize { get; set; } = 200;
    public string? SelectedStoreCode { get; set; }
}
