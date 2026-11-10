using System.Text.Json;
using Microsoft.Data.Sqlite;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Core.Models;

namespace VallianiDataSync.Infrastructure.State;

public sealed class SqliteSyncStateStore : ISyncStateStore
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteSyncStateStore(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VallianiDataSync",
            "data");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "sync-state.db");
    }

    private SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = CreateConnection();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS product_hashes (
                  store_code TEXT NOT NULL,
                  sku TEXT NOT NULL,
                  content_hash TEXT NOT NULL,
                  updated_at TEXT NOT NULL,
                  PRIMARY KEY (store_code, sku)
                );
                CREATE TABLE IF NOT EXISTS sync_jobs (
                  id TEXT PRIMARY KEY,
                  store_code TEXT NOT NULL,
                  sku TEXT NOT NULL,
                  channel INTEGER NOT NULL,
                  status INTEGER NOT NULL,
                  payload_json TEXT NOT NULL,
                  content_hash TEXT NOT NULL,
                  idempotency_key TEXT,
                  attempt_count INTEGER NOT NULL,
                  last_error TEXT,
                  created_at TEXT NOT NULL,
                  updated_at TEXT,
                  completed_at TEXT
                );
                CREATE TABLE IF NOT EXISTS sync_meta (
                  key TEXT PRIMARY KEY,
                  value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS sync_logs (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  timestamp_utc TEXT NOT NULL,
                  level INTEGER NOT NULL,
                  store_code TEXT,
                  sku TEXT,
                  operation TEXT NOT NULL,
                  api_endpoint TEXT,
                  response TEXT,
                  error TEXT,
                  message TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS image_state (
                  store_code TEXT NOT NULL,
                  sku TEXT NOT NULL,
                  local_path TEXT NOT NULL,
                  content_hash TEXT,
                  local_modified TEXT,
                  status INTEGER NOT NULL,
                  remote_url TEXT,
                  error TEXT,
                  PRIMARY KEY (store_code, sku)
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetLastHashAsync(string storeCode, string sku, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM product_hashes WHERE store_code=$s AND sku=$k";
        cmd.Parameters.AddWithValue("$s", storeCode);
        cmd.Parameters.AddWithValue("$k", sku);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    public async Task SetLastHashAsync(string storeCode, string sku, string hash, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_hashes(store_code, sku, content_hash, updated_at)
            VALUES ($s,$k,$h,$u)
            ON CONFLICT(store_code, sku) DO UPDATE SET content_hash=$h, updated_at=$u
            """;
        cmd.Parameters.AddWithValue("$s", storeCode);
        cmd.Parameters.AddWithValue("$k", sku);
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(SyncJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO sync_jobs
            (id, store_code, sku, channel, status, payload_json, content_hash, idempotency_key, attempt_count, last_error, created_at, updated_at, completed_at)
            VALUES ($id,$s,$sku,$ch,$st,$p,$h,$ik,$a,$e,$c,$u,$d)
            """;
        BindJob(cmd, job);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncJob>> DequeuePendingAsync(int take, CancellationToken cancellationToken = default)
    {
        var list = new List<SyncJob>();
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, store_code, sku, channel, status, payload_json, content_hash, idempotency_key, attempt_count, last_error, created_at, updated_at, completed_at
            FROM sync_jobs
            WHERE status IN (0, 3)
            ORDER BY created_at
            LIMIT $take
            """;
        cmd.Parameters.AddWithValue("$take", take);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            list.Add(ReadJob(reader));
        return list;
    }

    public async Task UpdateJobAsync(SyncJob job, CancellationToken cancellationToken = default)
    {
        await EnqueueAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTime?> GetLastSuccessfulSyncAsync(string? storeCode, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_meta WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", MetaKey(storeCode));
        var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    public async Task SetLastSuccessfulSyncAsync(string storeCode, DateTime utc, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sync_meta(key, value) VALUES ($k,$v)
            ON CONFLICT(key) DO UPDATE SET value=$v
            """;
        cmd.Parameters.AddWithValue("$k", MetaKey(storeCode));
        cmd.Parameters.AddWithValue("$v", utc.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddLogAsync(SyncLogEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sync_logs(timestamp_utc, level, store_code, sku, operation, api_endpoint, response, error, message)
            VALUES ($t,$l,$s,$sku,$o,$a,$r,$e,$m)
            """;
        cmd.Parameters.AddWithValue("$t", entry.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$l", (int)entry.Level);
        cmd.Parameters.AddWithValue("$s", (object?)entry.StoreCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sku", (object?)entry.Sku ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$o", entry.Operation);
        cmd.Parameters.AddWithValue("$a", (object?)entry.ApiEndpoint ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", (object?)entry.Response ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$e", (object?)entry.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$m", entry.Message);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SyncLogEntry>> QueryLogsAsync(string? search, string? storeCode, LogLevelKind? level, int take, CancellationToken cancellationToken = default)
    {
        var list = new List<SyncLogEntry>();
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, timestamp_utc, level, store_code, sku, operation, api_endpoint, response, error, message
            FROM sync_logs
            WHERE ($store IS NULL OR store_code=$store)
              AND ($level IS NULL OR level=$level)
              AND ($search IS NULL OR message LIKE '%' || $search || '%' OR IFNULL(sku,'') LIKE '%' || $search || '%' OR IFNULL(error,'') LIKE '%' || $search || '%')
            ORDER BY id DESC
            LIMIT $take
            """;
        cmd.Parameters.AddWithValue("$store", (object?)storeCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$level", level.HasValue ? (object)(int)level.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$search", (object?)search ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$take", take);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new SyncLogEntry
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                Level = (LogLevelKind)reader.GetInt32(2),
                StoreCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                Sku = reader.IsDBNull(4) ? null : reader.GetString(4),
                Operation = reader.GetString(5),
                ApiEndpoint = reader.IsDBNull(6) ? null : reader.GetString(6),
                Response = reader.IsDBNull(7) ? null : reader.GetString(7),
                Error = reader.IsDBNull(8) ? null : reader.GetString(8),
                Message = reader.GetString(9)
            });
        }
        return list;
    }

    public async Task SaveImageStateAsync(ImageSyncItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO image_state(store_code, sku, local_path, content_hash, local_modified, status, remote_url, error)
            VALUES ($s,$sku,$p,$h,$m,$st,$u,$e)
            ON CONFLICT(store_code, sku) DO UPDATE SET
              local_path=$p, content_hash=$h, local_modified=$m, status=$st, remote_url=$u, error=$e
            """;
        cmd.Parameters.AddWithValue("$s", item.StoreCode);
        cmd.Parameters.AddWithValue("$sku", item.Sku);
        cmd.Parameters.AddWithValue("$p", item.LocalPath);
        cmd.Parameters.AddWithValue("$h", (object?)item.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$m", item.LocalModifiedUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$st", (int)item.Status);
        cmd.Parameters.AddWithValue("$u", (object?)item.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$e", (object?)item.Error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ImageSyncItem?> GetImageStateAsync(string storeCode, string sku, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT store_code, sku, local_path, content_hash, local_modified, status, remote_url, error FROM image_state WHERE store_code=$s AND sku=$k";
        cmd.Parameters.AddWithValue("$s", storeCode);
        cmd.Parameters.AddWithValue("$k", sku);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new ImageSyncItem
        {
            StoreCode = reader.GetString(0),
            Sku = reader.GetString(1),
            LocalPath = reader.GetString(2),
            ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
            LocalModifiedUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
            Status = (ImageSyncItemStatus)reader.GetInt32(5),
            RemoteUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
            Error = reader.IsDBNull(7) ? null : reader.GetString(7)
        };
    }

    public async Task<ImageSyncSummary> GetImageSummaryAsync(string? storeCode, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT
              COUNT(1),
              SUM(CASE WHEN status=0 THEN 1 ELSE 0 END),
              SUM(CASE WHEN status=1 THEN 1 ELSE 0 END),
              SUM(CASE WHEN status=2 THEN 1 ELSE 0 END),
              SUM(CASE WHEN status=3 THEN 1 ELSE 0 END)
            FROM image_state
            WHERE ($s IS NULL OR store_code=$s)
            """;
        cmd.Parameters.AddWithValue("$s", (object?)storeCode ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return new ImageSyncSummary();
        return new ImageSyncSummary
        {
            Total = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            Pending = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            Uploaded = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            Failed = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            Missing = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
        };
    }

    private static string MetaKey(string? storeCode) => $"last_success:{(storeCode ?? "*")}";

    private static void BindJob(SqliteCommand cmd, SyncJob job)
    {
        cmd.Parameters.AddWithValue("$id", job.Id.ToString());
        cmd.Parameters.AddWithValue("$s", job.StoreCode);
        cmd.Parameters.AddWithValue("$sku", job.Sku);
        cmd.Parameters.AddWithValue("$ch", (int)job.Channel);
        cmd.Parameters.AddWithValue("$st", (int)job.Status);
        cmd.Parameters.AddWithValue("$p", job.PayloadJson);
        cmd.Parameters.AddWithValue("$h", job.ContentHash);
        cmd.Parameters.AddWithValue("$ik", (object?)job.IdempotencyKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$a", job.AttemptCount);
        cmd.Parameters.AddWithValue("$e", (object?)job.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", job.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$u", job.UpdatedAtUtc?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$d", job.CompletedAtUtc?.ToString("O") ?? (object)DBNull.Value);
    }

    private static SyncJob ReadJob(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        StoreCode = reader.GetString(1),
        Sku = reader.GetString(2),
        Channel = (SyncChannel)reader.GetInt32(3),
        Status = (SyncJobStatus)reader.GetInt32(4),
        PayloadJson = reader.GetString(5),
        ContentHash = reader.GetString(6),
        IdempotencyKey = reader.IsDBNull(7) ? null : reader.GetString(7),
        AttemptCount = reader.GetInt32(8),
        LastError = reader.IsDBNull(9) ? null : reader.GetString(9),
        CreatedAtUtc = DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind),
        UpdatedAtUtc = reader.IsDBNull(11) ? null : DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind),
        CompletedAtUtc = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind)
    };
}
