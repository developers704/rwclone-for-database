namespace VallianiDataSync.Core.Models;

public sealed class DestinationFormatField
{
    public string SourceField { get; set; } = string.Empty;
    public string DestinationKey { get; set; } = string.Empty;
}

public sealed class DestinationProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New project";
    public bool Enabled { get; set; }
    public bool AutoSyncEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 120;
    public string ApiUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
    /// <summary>JsonArray or Csv</summary>
    public string BodyFormat { get; set; } = "JsonArray";
    public string AuthHeaderName { get; set; } = "Authorization";
    /// <summary>Optional prefix e.g. "Bearer "</summary>
    public string AuthHeaderPrefix { get; set; } = "Bearer ";
    public List<DestinationFormatField> FormatFields { get; set; } = new();
    /// <summary>Original uploaded template file name (reference only).</summary>
    public string? FormatTemplateFileName { get; set; }
    /// <summary>Optional raw template text saved for audit.</summary>
    public string? FormatTemplateRaw { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public string? LastSyncMessage { get; set; }
    public DateTime? NextSyncLocal { get; set; }
}
