using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using VallianiDataSync.Api.Data;
using VallianiDataSync.Api.Middleware;
using VallianiDataSync.Api.Models;
using VallianiDataSync.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("VallianiApi"));
var apiOptions = builder.Configuration.GetSection("VallianiApi").Get<ApiOptions>() ?? new ApiOptions();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Valliani Data Sync API",
        Version = "v1",
        Description = "Receives product/inventory/price/image sync from Valliani Data Sync desktop app."
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API key via X-Api-Key header",
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDbContext<SyncDbContext>(options =>
{
    if (string.Equals(apiOptions.DatabaseProvider, "sqlserver", StringComparison.OrdinalIgnoreCase))
    {
        var cs = builder.Configuration.GetConnectionString("SqlServer")
                 ?? throw new InvalidOperationException("ConnectionStrings:SqlServer is required when DatabaseProvider=sqlserver");
        options.UseSqlServer(cs);
    }
    else
    {
        var cs = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=App_Data/valliani-sync.db";
        var pathPart = cs.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!Path.IsPathRooted(pathPart))
        {
            var full = Path.Combine(builder.Environment.ContentRootPath, pathPart);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            cs = $"Data Source={full}";
        }
        options.UseSqlite(cs);
    }
});

builder.Services.AddScoped<IProductSyncService, ProductSyncService>();
builder.Services.AddScoped<IImageStorageService, ImageStorageService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    await DbSeed.EnsureSeedAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Valliani Data Sync API v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

var imageRoot = Path.IsPathRooted(apiOptions.ImageStoragePath)
    ? apiOptions.ImageStoragePath
    : Path.Combine(app.Environment.ContentRootPath, apiOptions.ImageStoragePath);
Directory.CreateDirectory(imageRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(imageRoot),
    RequestPath = "/files"
});

app.UseMiddleware<ApiKeyMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Valliani Data Sync API" }));

app.MapPost("/api/v1/auth/validate", (IOptions<ApiOptions> opt) =>
    Results.Ok(new AuthValidateResponse { Success = true, Message = "ok" }));

app.MapGet("/api/v1/stores", async (SyncDbContext db) =>
{
    var stores = await db.Stores.AsNoTracking()
        .Where(s => s.IsActive)
        .OrderBy(s => s.Name)
        .Select(s => new StoreDto { Id = s.Id.ToString(), Code = s.Code, Name = s.Name, IsActive = s.IsActive })
        .ToListAsync();
    return Results.Ok(stores);
});

app.MapPost("/api/v1/products/sync", async (SyncBatchRequest request, IProductSyncService sync, SyncDbContext db, HttpRequest http) =>
{
    var result = await sync.UpsertProductsAsync(request.Items, request.Replace, http.HttpContext.RequestAborted);
    await LogActivity(db, http, "products", "sync", $"uploaded={result.Uploaded} updated={result.Updated} failed={result.Failed}");
    return Results.Ok(result);
});

app.MapPost("/api/v1/inventory/sync", async (SyncBatchRequest request, IProductSyncService sync, SyncDbContext db, HttpRequest http) =>
{
    var result = await sync.UpsertInventoryAsync(request.Items, http.HttpContext.RequestAborted);
    await LogActivity(db, http, "inventory", "sync", $"updated={result.Updated} failed={result.Failed}");
    return Results.Ok(result);
});

/// <summary>
/// Receives Destinations table batches (sales / inventory / payments).
/// Accepts JSON array, { table, source, rows }, or multipart CSV (file/csv).
/// </summary>
app.MapPost("/api/v1/tables/sync", async (HttpRequest http, SyncDbContext db) =>
{
    var table = http.Headers["X-Table-Name"].FirstOrDefault()
                ?? http.Headers["X-Source-Name"].FirstOrDefault()
                ?? "unknown";
    var source = http.Headers["X-Source-Name"].FirstOrDefault() ?? table;
    var rowCount = 0;

    if (http.HasFormContentType)
    {
        var form = await http.ReadFormAsync();
        table = form["table"].FirstOrDefault() ?? table;
        source = form["source"].FirstOrDefault() ?? source;
        var file = form.Files.GetFile("file") ?? form.Files.GetFile("csv");
        if (file is not null)
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            rowCount = Math.Max(0, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        }
        else if (!string.IsNullOrWhiteSpace(form["csv"]))
        {
            var text = form["csv"].ToString();
            rowCount = Math.Max(0, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
        }
    }
    else
    {
        using var reader = new StreamReader(http.Body);
        var body = await reader.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    rowCount = doc.RootElement.GetArrayLength();
                else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("table", out var t))
                        table = t.GetString() ?? table;
                    if (doc.RootElement.TryGetProperty("source", out var s))
                        source = s.GetString() ?? source;
                    if (doc.RootElement.TryGetProperty("rows", out var rows)
                        && rows.ValueKind == System.Text.Json.JsonValueKind.Array)
                        rowCount = rows.GetArrayLength();
                    else if (doc.RootElement.TryGetProperty("rowCount", out var rc)
                             && rc.TryGetInt32(out var n))
                        rowCount = n;
                }
            }
            catch
            {
                // CSV posted as text/csv
                rowCount = Math.Max(0, body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1);
            }
        }
    }

    await LogActivity(db, http, "tables", "sync", $"table={table} source={source} rows={rowCount}");
    return Results.Ok(new { success = true, table, source, rowCount, message = $"Accepted {rowCount} row(s) for {table}" });
});

app.MapPost("/api/v1/prices/sync", async (SyncBatchRequest request, IProductSyncService sync, SyncDbContext db, HttpRequest http) =>
{
    var result = await sync.UpsertPricesAsync(request.Items, http.HttpContext.RequestAborted);
    await LogActivity(db, http, "prices", "sync", $"updated={result.Updated} failed={result.Failed}");
    return Results.Ok(result);
});

app.MapPost("/api/v1/images/upload", async (
    HttpRequest request,
    IImageStorageService images,
    SyncDbContext db) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { success = false, message = "multipart/form-data required" });

    var form = await request.ReadFormAsync();
    var storeCode = form["storeCode"].ToString();
    var sku = form["sku"].ToString();
    var contentHash = form["contentHash"].ToString();
    var file = form.Files.GetFile("file");
    if (file is null || string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(sku))
        return Results.BadRequest(new { success = false, message = "storeCode, sku, file required" });

    var result = await images.SaveAsync(storeCode, sku, contentHash, file, request.HttpContext.RequestAborted);
    await LogActivity(db, request, "images", "upload", $"{storeCode}/{sku}");
    return Results.Ok(result);
});

app.MapPost("/api/v1/sync/complete", async (SyncCompleteRequest body, SyncDbContext db, HttpRequest http) =>
{
    var run = await db.SyncRuns.FirstOrDefaultAsync(r => r.Id == body.RunId);
    if (run is null)
    {
        run = new SyncRun { Id = body.RunId == Guid.Empty ? Guid.NewGuid() : body.RunId };
        db.SyncRuns.Add(run);
    }

    run.StoreCode = body.StoreCode ?? run.StoreCode ?? "*";
    run.Uploaded = body.Uploaded;
    run.Updated = body.Updated;
    run.Failed = body.Failed;
    run.Status = "completed";
    run.CompletedAtUtc = body.CompletedAtUtc ?? DateTime.UtcNow;
    run.LastUpdatedUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();
    await LogActivity(db, http, "sync", "complete", $"run={run.Id} failed={run.Failed}");
    return Results.Ok(new { success = true, runId = run.Id });
});

app.MapGet("/api/v1/sync/status", async ([FromQuery] Guid runId, SyncDbContext db) =>
{
    var run = await db.SyncRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId);
    if (run is null)
        return Results.NotFound(new { success = false, message = "run not found" });
    return Results.Ok(new SyncStatusDto
    {
        RunId = run.Id,
        Status = run.Status,
        LastUpdatedUtc = run.LastUpdatedUtc
    });
});

// Extra RWLink-like helpers for ops / website
app.MapGet("/api/v1/products", async ([FromQuery] string? storeCode, [FromQuery] string? search, [FromQuery] int? take, SyncDbContext db) =>
{
    var limit = take is null or <= 0 ? 100 : Math.Min(take.Value, 1000);
    var q = db.Products.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(storeCode))
        q = q.Where(p => p.StoreCode == storeCode);
    if (!string.IsNullOrWhiteSpace(search))
        q = q.Where(p => p.Sku.Contains(search) || (p.Name != null && p.Name.Contains(search)));
    var items = await q.OrderByDescending(p => p.UpdatedAtUtc).Take(limit).ToListAsync();
    return Results.Ok(items);
});

app.MapGet("/api/v1/products/{storeCode}/{sku}", async (string storeCode, string sku, SyncDbContext db) =>
{
    var item = await db.Products.AsNoTracking()
        .FirstOrDefaultAsync(p => p.StoreCode == storeCode && p.Sku == sku);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/v1/activity", async ([FromQuery] int take, SyncDbContext db) =>
{
    take = take <= 0 ? 50 : Math.Min(take, 500);
    var logs = await db.ActivityLogs.AsNoTracking()
        .OrderByDescending(a => a.Id)
        .Take(take)
        .ToListAsync();
    return Results.Ok(logs);
});

app.MapPost("/api/v1/activity", async (ActivityRequest body, SyncDbContext db, HttpRequest http) =>
{
    await LogActivity(db, http, body.Module, body.Activity, body.Description, body.StoreCode);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/v1/stores", async (StoreDto body, SyncDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(body.Code))
        return Results.BadRequest(new { success = false, message = "code required" });
    var existing = await db.Stores.FirstOrDefaultAsync(s => s.Code == body.Code);
    if (existing is null)
    {
        existing = new Store { Code = body.Code.Trim(), Name = body.Name ?? body.Code, IsActive = body.IsActive };
        db.Stores.Add(existing);
    }
    else
    {
        existing.Name = body.Name ?? existing.Name;
        existing.IsActive = body.IsActive;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new StoreDto { Id = existing.Id.ToString(), Code = existing.Code, Name = existing.Name, IsActive = existing.IsActive });
});

app.Run();

static async Task LogActivity(SyncDbContext db, HttpRequest http, string module, string activity, string? description, string? storeCode = null)
{
    http.Headers.TryGetValue("X-Client-Id", out var clientId);
    db.ActivityLogs.Add(new ActivityLog
    {
        Module = module,
        Activity = activity,
        Description = description,
        StoreCode = storeCode,
        ClientId = clientId.ToString(),
        TimestampUtc = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
}

public partial class Program;
