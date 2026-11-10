namespace VallianiDataSync.Core.Options;

public sealed class ApiOptions
{
    public string BaseUrl { get; set; } = "https://localhost:7188";
    public string? StoreId { get; set; }
    public string? ClientId { get; set; }
    public int TimeoutSeconds { get; set; } = 60;
    public int RetryCount { get; set; } = 3;
    public bool RequireHttps { get; set; } = true;
}
