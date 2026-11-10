using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VallianiDataSync.Api.Data;
using VallianiDataSync.Api.Services;

namespace VallianiDataSync.Api.Middleware;

public sealed class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOptions<ApiOptions> options)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var key)
            || string.IsNullOrWhiteSpace(key)
            || !string.Equals(key.ToString(), options.Value.ApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "invalid api key" });
            return;
        }

        await _next(context);
    }
}

public sealed class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;

    public IdempotencyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, SyncDbContext db)
    {
        if (!HttpMethods.IsPost(context.Request.Method)
            || !context.Request.Headers.TryGetValue("Idempotency-Key", out var keyValues))
        {
            await _next(context);
            return;
        }

        var key = keyValues.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            await _next(context);
            return;
        }

        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == key)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            context.Response.StatusCode = existing.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(existing.ResponseJson);
            return;
        }

        var originalBody = context.Response.Body;
        await using var mem = new MemoryStream();
        context.Response.Body = mem;
        await _next(context);
        mem.Position = 0;
        var bodyText = await new StreamReader(mem).ReadToEndAsync();
        mem.Position = 0;
        await mem.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        if (context.Response.StatusCode is >= 200 and < 300)
        {
            db.IdempotencyRecords.Add(new Models.IdempotencyRecord
            {
                Key = key,
                Endpoint = context.Request.Path,
                ResponseJson = bodyText,
                StatusCode = context.Response.StatusCode,
                CreatedAtUtc = DateTime.UtcNow
            });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // race: another request saved same key — ignore
            }
        }
    }
}

public static class DbSeed
{
    public static async Task EnsureSeedAsync(SyncDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        if (!await db.Stores.AnyAsync())
        {
            db.Stores.AddRange(
                new Models.Store { Code = "Vallani_Jewelers", Name = "Vallani Jewelers", IsActive = true },
                new Models.Store { Code = "Great_Mall", Name = "Great Mall", IsActive = true },
                new Models.Store { Code = "Valley_Fair", Name = "Valley Fair", IsActive = true }
            );
            await db.SaveChangesAsync();
        }
    }
}
