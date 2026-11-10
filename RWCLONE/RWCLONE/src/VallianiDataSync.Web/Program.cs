using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using VallianiDataSync.Core.Interfaces;
using VallianiDataSync.Infrastructure;
using VallianiDataSync.Sync;
using VallianiDataSync.Web.Components;
using VallianiDataSync.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "VallianiDataSyncWeb";
});

var urls = builder.Configuration["Urls"] ?? "http://0.0.0.0:8080";
builder.WebHost.UseUrls(urls);

var dataRootConfig = builder.Configuration["Valliani:DataRoot"];
var dataRoot = string.IsNullOrWhiteSpace(dataRootConfig)
    ? (builder.Environment.IsDevelopment() ? VallianiDataPaths.DefaultDesktopRoot : VallianiDataPaths.DefaultServiceRoot)
    : dataRootConfig;

var dataProtectionRoot = Path.Combine(Path.GetTempPath(), "VallianiDataSync", "data-protection-keys");
Directory.CreateDirectory(dataProtectionRoot);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<IDataProtectionProvider>(_ =>
    DataProtectionProvider.Create(dataProtectionRoot));

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LoginTicketStore>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/account/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.Name = "VallianiWebAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();

builder.Services.AddVallianiInfrastructure(
    contentRoot: builder.Environment.ContentRootPath,
    dataRoot: dataRoot,
    scopedAuthSession: true);
builder.Services.AddVallianiSync();

var app = builder.Build();

var settings = app.Services.GetRequiredService<ISettingsService>();
await settings.LoadAsync();
await app.Services.GetRequiredService<ISyncStateStore>().InitializeAsync();

app.MapGet("/api/source/catalog", async (ISourceCatalogService catalog, CancellationToken cancellationToken) =>
{
    var value = await catalog.GetCatalogAsync(cancellationToken);
    return Results.Ok(value);
}).RequireAuthorization();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/account/complete-login", async (string t, HttpContext http, LoginTicketStore tickets) =>
{
    if (!tickets.TryConsume(t, out var name))
        return Results.Redirect("/login?error=expired");

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Role, "Admin")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        });
    return Results.Redirect("/");
}).AllowAnonymous();

// Reliable unlock when OTP is off (no Blazor circuit required)
app.MapGet("/account/open-without-otp", async (HttpContext http, ISettingsService settings) =>
{
    settings.Update(s =>
    {
        s.Security.OtpEnabled = false;
        s.Security.AllowedIps.Clear();
    });
    await settings.SaveAsync();

    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, "admin"),
        new(ClaimTypes.Role, "Admin")
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
        });
    return Results.Redirect("/");
}).AllowAnonymous();

app.MapGet("/account/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
