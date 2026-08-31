using Lassie.Components;
using Lassie.Data;
using Lassie.Data.Licenses;
using Lassie.Data.Users;
using Lassie.Data.Verification;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<LassieDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddScoped<ThemeState>();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

builder.Services.AddAuthorization();

// PasswordHasher<TUser>'s only state is immutable config fields plus a thread-safe
// RandomNumberGenerator — safe as a Singleton even though AddIdentityCore defaults to Scoped.
builder.Services.AddSingleton<PasswordHasher<User>>();

// Verification-audit pipeline: the verify endpoint hands each resolved call to the queue
// (non-blocking), a background writer batch-persists them off the response path, and a
// second background service prunes rows past the retention window.
builder.Services.AddSingleton<IVerificationEventQueue, VerificationEventQueue>();
builder.Services.AddHostedService<VerificationEventWriter>();
builder.Services.AddHostedService<VerificationEventRetentionService>();

var app = builder.Build();

// Caddy terminates TLS and talks plain HTTP to this container, so Kestrel sees
// Request.Scheme as "http" unless told otherwise — that leaks into generated absolute
// URLs (e.g. the cookie challenge's redirect Location header ends up http:// instead of
// https://). Trust X-Forwarded-Proto from any source: Kestrel is only ever reached
// through Caddy on the internal Docker network, never exposed directly.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Caddy's `handle_path /lassie*` already strips the prefix before proxying, so it's
// never present in Request.Path for UsePathBase() to strip — force it onto PathBase
// instead, purely so the app generates correct self-referencing URLs (auth redirects,
// Blazor's <base href>-driven asset/SignalR negotiate URLs). No-op when unset (local dev).
var pathBase = app.Configuration["ASPNETCORE_PATHBASE"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.Use((context, next) =>
    {
        context.Request.PathBase = new PathString(pathBase);
        return next();
    });
}

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<LassieDbContext>();
    context.Database.Migrate();

    if (!context.Users.Any())
    {
        var adminEmail = app.Configuration["ADMIN_EMAIL"];
        var adminPassword = app.Configuration["ADMIN_PASSWORD"];

        if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
        {
            throw new InvalidOperationException(
                "No admin account exists and ADMIN_EMAIL/ADMIN_PASSWORD are not configured. " +
                "Set both so the first admin account can be seeded.");
        }

        var passwordHasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>();
        var admin = new User { Email = adminEmail, PasswordHash = string.Empty };
        admin.PasswordHash = passwordHasher.HashPassword(admin, adminPassword);

        context.Users.Add(admin);
        context.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Machine-to-machine verification API: authenticates via a per-license API key sent as a
// header (never a query string — query strings land in Caddy/ASP.NET Core access logs).
// No .RequireAuthorization()/AuthenticationScheme — the handler validates the key itself,
// so a missing/unrecognized key returns a plain 401 rather than a cookie-scheme redirect.
// No broad try/catch: an unexpected failure (e.g. DB unreachable) must propagate to the
// framework's default 5xx handling, never be coerced into `valid: false`.
app.MapGet("/api/license/verify", async (
    HttpRequest request,
    HttpContext http,
    LassieDbContext context,
    IVerificationEventQueue verificationEvents,
    ILoggerFactory loggerFactory) =>
{
    var apiKey = request.Headers["X-Api-Key"].ToString();
    if (string.IsNullOrEmpty(apiKey))
    {
        return Results.Unauthorized();
    }

    var hash = ApiKeyHasher.Hash(apiKey);
    var license = await context.Licenses.SingleOrDefaultAsync(l => l.ApiKeyHash == hash);
    if (license is null)
    {
        return Results.Unauthorized();
    }

    var valid = license.Status == LicenseStatus.Active;

    // Audit side-effect. Building the event (reading the connection IP / headers) and the
    // enqueue are wrapped so a failure here is logged and swallowed — a valid license must
    // never surface as a 5xx because of audit code. The lookup above keeps its no-try/catch
    // stance so a genuine DB outage still propagates as 5xx.
    try
    {
        verificationEvents.Enqueue(new LicenseVerificationEvent
        {
            LicenseId = license.Id,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            ClientIp = http.Connection.RemoteIpAddress?.ToString(),
            UserAgent = VerificationRequestFields.Truncate(request.Headers.UserAgent.ToString(), 512),
            ForwardedForRaw = VerificationRequestFields.Truncate(VerificationRequestFields.ReadForwardedFor(request), 256),
            ObservedStatus = license.Status,
        });
    }
    catch (Exception ex)
    {
        loggerFactory.CreateLogger("Lassie.VerifyLicense")
            .LogWarning(ex, "Failed to enqueue verification audit event for license {LicenseId}.", license.Id);
    }

    return Results.Ok(new { valid });
})
.WithName("VerifyLicense");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program;
