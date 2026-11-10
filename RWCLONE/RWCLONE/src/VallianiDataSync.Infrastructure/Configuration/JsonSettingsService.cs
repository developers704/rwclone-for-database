using System.Text.Json;
using Microsoft.Extensions.Logging;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;
using VallianiDataSync.Core.Options;

namespace VallianiDataSync.Infrastructure.Configuration;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _path;
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly object _gate = new();
    private AppSettings _current = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonSettingsService(ILogger<JsonSettingsService> logger, string? path = null)
    {
        _logger = logger;
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VallianiDataSync");
        Directory.CreateDirectory(root);
        _path = path ?? Path.Combine(root, "settings.json");
    }

    public AppSettings Current
    {
        get { lock (_gate) return _current; }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            _logger.LogInformation("No settings file yet at {Path}; using defaults", _path);
            return;
        }

        await using var stream = File.OpenRead(_path);
        var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _current = loaded ?? new AppSettings();
            // Old settings.json lacked SyncAllTables (deserializes as false). Default ON so Sync sends every table.
            if (_current.Mapping.SelectedTables.Count == 0)
                _current.Mapping.SyncAllTables = true;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        AppSettings snapshot;
        lock (_gate)
            snapshot = Clone(_current);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Settings saved to {Path}", _path);
    }

    public void Update(Action<AppSettings> mutate)
    {
        lock (_gate)
            mutate(_current);
    }

    private static AppSettings Clone(AppSettings source)
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions) ?? new AppSettings();
}
