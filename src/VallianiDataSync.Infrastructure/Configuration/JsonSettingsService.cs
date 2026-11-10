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
            _current = Sanitize(loaded ?? new AppSettings());
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
        => Sanitize(
            JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)
            ?? new AppSettings());

    /// <summary>
    /// Strip null list entries / null dictionary keys that crash startup
    /// (ArgumentNullException Parameter 'key') on some PCs with older settings.json.
    /// </summary>
    private static AppSettings Sanitize(AppSettings s)
    {
        s.Mapping ??= new FieldMappingConfig();
        s.Destinations ??= new List<DestinationProject>();
        s.Stores ??= new List<StoreRecord>();

        var m = s.Mapping;
        m.SelectedTables = CleanList(m.SelectedTables);
        m.DiscoveredTables = CleanList(m.DiscoveredTables);
        m.DiscoveredColumns = CleanList(m.DiscoveredColumns);
        m.TableQueries = CleanDict(m.TableQueries);
        m.TableAliases = CleanDict(m.TableAliases);
        m.Columns = CleanDict(m.Columns);
        m.TableColumns = CleanDictList(m.TableColumns);

        foreach (var d in s.Destinations)
        {
            if (d is null) continue;
            if (string.IsNullOrWhiteSpace(d.Id))
                d.Id = Guid.NewGuid().ToString("N");
            d.SyncSources = CleanList(d.SyncSources);
            d.FormatFields ??= new List<DestinationFormatField>();
            d.FormatFields = d.FormatFields
                .Where(f => f is not null)
                .Select(f =>
                {
                    f.SourceField ??= string.Empty;
                    f.DestinationKey ??= string.Empty;
                    return f;
                })
                .ToList();
        }

        s.Destinations = s.Destinations.Where(d => d is not null).ToList()!;
        return s;
    }

    private static List<string> CleanList(List<string>? list)
        => (list ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();

    private static Dictionary<string, string> CleanDict(Dictionary<string, string>? dict)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dict is null) return result;
        foreach (var kv in dict)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            result[kv.Key.Trim()] = kv.Value ?? string.Empty;
        }
        return result;
    }

    private static Dictionary<string, List<string>> CleanDictList(Dictionary<string, List<string>>? dict)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (dict is null) return result;
        foreach (var kv in dict)
        {
            if (string.IsNullOrWhiteSpace(kv.Key)) continue;
            result[kv.Key.Trim()] = CleanList(kv.Value);
        }
        return result;
    }
}

