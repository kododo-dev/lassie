using Lassie.Components;
using Lassie.Data;
using Lassie.Data.Licenses;
using Lassie.Data.Users;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<LassieDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
    var forecast =  Enumerable.Range(1, 5).Select(index =>
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
app.MapGet("/api/license/verify", async (HttpRequest request, LassieDbContext context) =>
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

    var valid = license.ExpiresOn is null || license.ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow);
    return Results.Ok(new { valid });
})
.WithName("VerifyLicense");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
