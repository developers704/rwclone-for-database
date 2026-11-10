using System.Text.Json;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/test", async (HttpRequest req) =>
{
    TestRequest? body;
    try
    {
        body = await JsonSerializer.DeserializeAsync<TestRequest>(
            req.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch
    {
        return Results.Json(new { ok = false, message = "Invalid JSON body." });
    }

    if (body is null || string.IsNullOrWhiteSpace(body.Host) || string.IsNullOrWhiteSpace(body.Database))
        return Results.Json(new { ok = false, message = "Host and Database are required." });

    var port = body.Port <= 0 ? 1433 : body.Port;
    var timeout = body.TimeoutSeconds <= 0 ? 15 : Math.Clamp(body.TimeoutSeconds, 3, 60);

    var csb = new SqlConnectionStringBuilder
    {
        DataSource = $"{body.Host.Trim()},{port}",
        InitialCatalog = body.Database.Trim(),
        ConnectTimeout = timeout,
        TrustServerCertificate = true,
        Encrypt = true
    };

    if (body.WindowsAuth)
    {
        csb.IntegratedSecurity = true;
    }
    else
    {
        if (string.IsNullOrWhiteSpace(body.Username))
            return Results.Json(new { ok = false, message = "Username required when Windows Auth is off." });
        csb.UserID = body.Username.Trim();
        csb.Password = body.Password ?? "";
    }

    try
    {
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT @@VERSION, DB_NAME(), SYSTEM_USER;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        string version = "", db = "", user = "";
        if (await reader.ReadAsync())
        {
            version = reader.GetString(0);
            db = reader.IsDBNull(1) ? "" : reader.GetString(1);
            user = reader.IsDBNull(2) ? "" : reader.GetString(2);
        }
        var shortVer = version.Split('\n')[0].Trim();
        return Results.Json(new
        {
            ok = true,
            message = $"Connected OK. Database={db}, User={user}",
            version = shortVer
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, message = ex.Message });
    }
});

app.Run("http://127.0.0.1:5055");

internal sealed class TestRequest
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool WindowsAuth { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
}
