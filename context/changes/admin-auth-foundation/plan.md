# Admin Authentication Foundation Implementation Plan

## Overview

Wire cookie-based authentication into the panel, backed by a `Users` table with a single seeded admin account, using Blazor Server for the first UI the project has ever had. This is roadmap item `F-02` in `context/foundation/roadmap.md` — the foundation every panel slice (S-01 through S-05) depends on for gating access.

## Current State Analysis

- `src/` has zero UI — `Program.cs` is still minimal-API only (`/weatherforecast`, `AddOpenApi`, `LassieDbContext` DI registration, `Database.Migrate()` at startup). No `Microsoft.AspNetCore.Authentication`/`Authorization` services registered, no Razor/Blazor components, no `wwwroot`.
- `LassieDbContext` (`src/Data/LassieDbContext.cs`) has one `DbSet<AuditLog>` and an audit-on-save convention (`IAuditable` + `SaveChanges` override). No `User`/`Admin` entity exists.
- `deploy/docker-compose.yml` / `deploy/.env.example` already establish the project's secrets convention: a value is added to `.env` on the VPS, passed through `docker-compose.yml`'s `environment:` block, read via `IConfiguration` in the app. Local dev mirrors it via `src/appsettings.Development.json` (no containerized app locally — only Postgres runs in `docker-compose.dev.yml`).
- The VPS reaches Lassie at `kododo.dev/lassie` via Caddy `handle_path /lassie* { reverse_proxy lassie:8080 }`, which **strips** `/lassie` from the incoming request before proxying — confirmed in `context/foundation/infrastructure.md` and the VPS layout notes. `deploy/docker-compose.yml` already sets `ASPNETCORE_PATHBASE: /lassie`, but nothing in `Program.cs` currently reads or applies it; this was inert for the `/weatherforecast` endpoint (a leaf GET with no generated links or redirects) but becomes load-bearing the moment the app generates any self-referencing URL — which a login redirect and Blazor's own asset/SignalR negotiation both do.
- No test project exists (confirmed absent in F-01; carried forward as a decision here too — see Key Discoveries).

### Key Discoveries:

- `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` doesn't actually use its `TUser` generic parameter anywhere internally — it's safe to instantiate as `PasswordHasher<User>` against a plain EF entity without pulling in the full ASP.NET Core Identity system (stores, `UserManager`, role management), which would be disproportionate for one flat admin account (PRD Access Control: "brak rozróżnienia ról w MVP").
- Blazor Server **interactive** components cannot call `HttpContext.SignInAsync`/`SignOutAsync` — the SignalR circuit that drives interactivity runs after the initial HTTP response has already started, so `HttpContext` mutation isn't available there. Login/logout must be plain **static server-rendered (non-interactive)** pages that handle the form POST directly, confirmed against current ASP.NET Core / Blazor Web App guidance.
- `AddCascadingAuthenticationState()` (the .NET 8+ Blazor Web App pattern) reads `HttpContext.User` at circuit start and cascades it as `AuthenticationState` automatically once cookie authentication is registered — no custom `AuthenticationStateProvider` needed for this slice.
- Caddy's `handle_path` already strips `/lassie` from the inbound path, so **server-side route matching inside the app needs no path-base awareness at all**. The gap is purely in URLs the app *generates* pointing back at itself (auth challenge redirects, Blazor's `<base href>`-driven asset/SignalR negotiate URLs) — those must still embed `/lassie` so the *next* browser request routes through Caddy correctly. The standard `UsePathBase` middleware won't help here because it only strips a prefix that's already present in `Request.Path` — since Caddy already removed it, the path is never there to strip. The fix is a small unconditional middleware that force-sets `HttpContext.Request.PathBase` from configuration, independent of what's in `Request.Path`.

## What We're NOT Doing

- Full ASP.NET Core Identity (stores, `UserManager`, roles, lockout, 2FA) — one flat admin account doesn't need it (see Key Discoveries).
- Self-registration or any UI for creating additional admin accounts — out of scope per PRD; the only account is the startup-seeded one.
- Password reset / forgot-password flow — explicitly out of scope per PRD (`## Non-Goals`).
- Change-own-password while logged in — not in FR-011; deferred to a future slice if ever needed.
- Brute-force protection / login lockout / rate limiting — accepted risk for MVP scale (single admin, private VPS, no public registration).
- Standing up a test project — still deferred, consistent with F-01; this slice is verified manually.
- Any real panel content (license/module CRUD) — `PanelHome` is a placeholder proving the auth gate works; S-01 replaces it.
- Modifying the shared Caddyfile — Caddy's `reverse_proxy` already proxies WebSocket upgrades transparently (needed for Blazor Server's SignalR circuit), so no Caddy-side change is required for this slice.

## Critical Implementation Details

### Login/logout must be static SSR, not interactive

`Login.razor` and `Logout.razor` must NOT declare `@rendermode InteractiveServer` (nor inherit it from a layout that does). They run as plain static server-rendered pages so `HttpContext.SignInAsync`/`SignOutAsync` are callable directly from their form-post handlers. `PanelHome.razor` (and everything under it going forward) uses interactive server rendering as normal — only the two auth pages are the exception, and it must stay that way as the panel grows.

### Reverse-proxy path base

Add an unconditional middleware early in `Program.cs` (before `UseAuthentication`/`UseRouting`-equivalent calls) that sets `context.Request.PathBase = new PathString(configuredPathBase)` whenever configuration key `ASPNETCORE_PATHBASE` is non-empty — do not use the built-in `UsePathBase()` middleware, since it only strips a prefix that's already present in `Request.Path`, and Caddy's `handle_path` has already removed it by the time the request reaches Kestrel. `Components/App.razor`'s `<base href>` must read the same configured value (defaulting to `/` when unset, which is the case in local dev) so Blazor's client script builds correct asset and SignalR negotiate URLs. This is inert in local dev (no `ASPNETCORE_PATHBASE` set there) and only observable in production — Phase 3's production verification step is what actually proves it.

### Discovered during Phase 3 production verification (two fixes, both confirmed only observable in production)

- **Scheme leaks into generated redirect URLs.** Caddy terminates TLS and talks plain HTTP to the container, so `Request.Scheme` is always `"http"` from Kestrel's point of view — this leaked into the cookie challenge's absolute redirect `Location` header (`http://kododo.dev/lassie/login...` instead of `https://...`). Fixed by adding `app.UseForwardedHeaders(...)` (trusting `X-Forwarded-Proto`/`X-Forwarded-For` from any source, since Kestrel is never reached except through Caddy on the internal Docker network) right after `var app = builder.Build();`, before the path-base middleware. Confirmed via a local Docker build with a simulated `X-Forwarded-Proto: https` header.
- **Static web assets (including Blazor's own `_framework/blazor.web.js`) were entirely missing from the published Docker image** — `wwwroot` didn't exist in the container at all, `_framework/blazor.web.js` 404'd. Root cause: the Dockerfile's `COPY src/lassie.csproj src/` → `dotnet restore` → `COPY src/ src/` → `dotnet publish --no-restore` layer-caching pattern produces an incomplete static-web-assets manifest for Blazor projects specifically (confirmed by reproducing locally: a plain `dotnet publish` on the full source tree produces a correct ~14KB manifest; the Docker two-step restore/publish sequence produces an empty one). Fixed by collapsing the Dockerfile to a single `COPY src/ src/` → `dotnet publish` (no separate early restore, no `--no-restore`) — trades away some restore-layer caching for correctness, acceptable at this project's size.

## Phase 1: Blazor Server scaffolding + cookie-auth infrastructure

### Overview

Get Blazor Server rendering behind the existing minimal-API host, with cookie authentication, cascading auth state, and reverse-proxy path-base handling wired — but no login logic yet, so a fully public placeholder page is the proof point.

### Changes Required:

#### 1. Program.cs services and middleware

**File**: `src/Program.cs`

**Intent**: Register everything Blazor Server + cookie auth need, in an order that actually works, without yet gating anything behind `[Authorize]`.

**Contract**: Add, before `builder.Build()`: `builder.Services.AddRazorComponents().AddInteractiveServerComponents();`, `builder.Services.AddCascadingAuthenticationState();`, and `builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => { options.LoginPath = "/login"; options.LogoutPath = "/logout"; options.ExpireTimeSpan = TimeSpan.FromDays(14); options.SlidingExpiration = true; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; })` plus `builder.Services.AddAuthorization();`.

After `var app = builder.Build();` and before the existing migration-scope block, add the unconditional path-base middleware described in Critical Implementation Details (read `app.Configuration["ASPNETCORE_PATHBASE"]`, no-op if empty/null).

Pipeline order after the existing `app.UseHttpsRedirection();`: `app.UseStaticFiles();` → `app.UseAuthentication();` → `app.UseAuthorization();` → `app.UseAntiforgery();` → then endpoint mapping (`app.MapRazorComponents<App>().AddInteractiveServerRenderMode();` alongside the existing `app.MapGet("/weatherforecast", ...)`).

#### 2. Blazor host and router components

**File**: `src/Components/App.razor` (new), `src/Components/Routes.razor` (new), `src/Components/_Imports.razor` (new)

**Intent**: The root HTML document Blazor renders into, and the router that dispatches to pages under `Components/Pages/`.

**Contract**: `App.razor` is the standard .NET 8+ Blazor Web App root component — `<html>`/`<head>`/`<body>` shell referencing `_framework/blazor.web.js`, with `<base href>` set from the same configured path-base value used by the middleware (default `/`). `Routes.razor` uses `<AuthorizeRouteView>`, not plain `<RouteView>` — confirmed against Microsoft docs that plain `RouteView` silently ignores `[Authorize]` entirely (renders the routed component unconditionally), so it's the only mechanism that actually evaluates the cascaded `AuthenticationState` from `AddCascadingAuthenticationState()`. Its `NotAuthorized` fragment redirects unauthenticated users via a small dedicated `src/Components/RedirectToLogin.razor` component (new — `@inject NavigationManager`, calls `NavigateTo("/login", forceLoad: true)` in `OnInitialized`); an authenticated-but-unauthorized branch renders a plain "not authorized" message (currently unreachable dead code — no policies beyond plain `[Authorize]` exist yet — but forward-compatible). `_Imports.razor` includes the routing/authorization/components usings every page needs.

#### 3. Placeholder panel page

**File**: `src/Components/Pages/PanelHome.razor` (new)

**Intent**: Prove the whole rendering pipeline (Blazor Server + reverse-proxy path base + static assets) works end-to-end, with no auth gate yet — Phase 3 adds `[Authorize]` once login exists.

**Contract**: `@page "/"`, interactive server render mode, renders a static "Lassie admin panel — placeholder" heading. No data, no auth attribute in this phase.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- `dotnet run --project src/lassie.csproj` starts without throwing
- Navigating to `http://localhost:5092/` renders the placeholder page with no browser console errors (SignalR circuit connects, static assets load)

---

## Phase 2: Users table + password hashing + startup seed

### Overview

Add the `User` entity and its migration, register `PasswordHasher<User>`, and seed exactly one admin account on startup from configuration — resolving the roadmap's open "how does the first admin account get provisioned" unknown.

### Changes Required:

#### 1. EF Core Identity package

**File**: `src/lassie.csproj`

**Intent**: Pull in `PasswordHasher<TUser>` without the full ASP.NET Core Identity system.

**Contract**: Add `PackageReference` for `Microsoft.Extensions.Identity.Core`, pinned to the latest stable version compatible with `net10.0`.

#### 2. User entity

**File**: `src/Data/Users/User.cs` (new)

**Intent**: The single account type the panel authenticates against. Deliberately does NOT implement `IAuditable` — a password hash is exactly the kind of value that must never land in a readable `AuditLog.Snapshot` (see roadmap Q&A / lessons.md's audit-secrecy spirit), and there's no audit use case for a single-admin credential today.

**Contract**: `public class User { public long Id { get; set; } public required string Email { get; set; } public required string PasswordHash { get; set; } }` — follows the same plain-EF-entity convention as `AuditLog`.

#### 3. DbContext wiring

**File**: `src/Data/LassieDbContext.cs`

**Intent**: Register the new entity and enforce email uniqueness at the database level.

**Contract**: Add `DbSet<User> Users => Set<User>();` and, in `OnModelCreating`, `modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();`.

#### 4. Migration

**File**: `src/Migrations/*_AddUsers.cs` (generated)

**Intent**: Apply the new table via the same `dotnet ef migrations add` → auto-apply-on-startup pipeline F-01 established.

**Contract**: Run `dotnet ef migrations add AddUsers --project src/lassie.csproj`, verify it applies cleanly against the local dev database.

#### 5. Startup seed

**File**: `src/Program.cs`

**Intent**: Guarantee exactly one admin account exists after first boot, without a manual SQL step.

**Contract**: Register `builder.Services.AddSingleton<PasswordHasher<User>>();` before `builder.Build()` — Phase 3's `Login.razor` also depends on resolving this. In the same DI scope block that already runs `Database.Migrate()`, after migration completes: if `context.Users` has no rows, read `ADMIN_EMAIL` and `ADMIN_PASSWORD` from configuration — throw a clear `InvalidOperationException` if either is missing or empty (fail fast, matching F-01's philosophy: an unusable admin panel should crash loudly, not boot silently broken). Otherwise hash the password via `PasswordHasher<User>.HashPassword` and insert the one row. If `context.Users` already has rows, skip entirely (idempotent across restarts — the seed only ever runs once, on the very first boot with an empty table).

#### 6. Local dev configuration

**File**: `src/appsettings.Development.json`

**Intent**: Give local dev a seedable admin without touching production secrets.

**Contract**: Add top-level `"ADMIN_EMAIL"` / `"ADMIN_PASSWORD"` keys (flat, matching the production env var names exactly so `IConfiguration` resolves both the same way regardless of environment) with a throwaway local value (e.g. `admin@localhost` / a simple dev password).

#### 7. Production secrets plumbing

**File**: `deploy/docker-compose.yml`, `deploy/.env.example`

**Intent**: Extend the existing `.env` → `docker-compose.yml environment:` pattern to the two new secrets.

**Contract**: Add `ADMIN_EMAIL: "${ADMIN_EMAIL}"` and `ADMIN_PASSWORD: "${ADMIN_PASSWORD}"` to `deploy/docker-compose.yml`'s `environment:` block; add both to `deploy/.env.example` with a comment noting they only matter for the very first boot (the seed is a no-op afterward, so rotating `.env` later has no effect without a manual DB change).

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `src/Migrations/*_AddUsers.cs` exists after `dotnet ef migrations add AddUsers`

#### Manual Verification:

- `docker compose -f docker-compose.dev.yml up -d && dotnet run --project src/lassie.csproj` boots cleanly; `psql` against the local dev database shows exactly one row in `Users` matching `ADMIN_EMAIL`, with `PasswordHash` looking like an opaque hash blob, never the plaintext password
- Restarting the app a second time does not create a duplicate row or throw

---

## Phase 3: Login/logout pages + protected gate + production verification

### Overview

Add the actual login/logout pages (static SSR, per Critical Implementation Details), gate `PanelHome` behind `[Authorize]`, log login attempts, and prove the whole cycle works both locally and against production behind the `/lassie` path prefix.

### Changes Required:

#### 1. Login page

**File**: `src/Components/Pages/Login.razor` (new)

**Intent**: Verify credentials against the seeded `User` row and establish the cookie session.

**Contract**: `@page "/login"`, static server rendering (no `@rendermode`) so `HttpContext` is available. Use `EditForm` bound to a `[SupplyParameterFromForm]` model with `Email`/`Password` — this auto-injects `<AntiforgeryToken />`, which `app.UseAntiforgery()` (wired in Phase 1) requires; a plain `<form>` would need that token added manually and is not used here. On submit: look up `User` by email, verify via `PasswordHasher<User>.VerifyHashedPassword`; on success, build a `ClaimsPrincipal` (at minimum `ClaimTypes.NameIdentifier` = user id, `ClaimTypes.Name` = email), call `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal)`, then redirect to `/` (`NavigationManager.NavigateTo("/", forceLoad: true)` — the force-load is necessary to start a fresh circuit that picks up the new auth cookie). On failure, re-render the page with a generic "invalid email or password" message (never reveal which field was wrong). Log every attempt via `ILogger` — `LogInformation` on success, `LogWarning` on failure — logging the email but never the password.

#### 2. Logout page

**File**: `src/Components/Pages/Logout.razor` (new)

**Intent**: End the session.

**Contract**: `@page "/logout"`, static server rendering, calls `HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` then redirects to `/login`.

#### 3. Gate the placeholder panel page

**File**: `src/Components/Pages/PanelHome.razor`, `src/Components/RedirectToLogin.razor` (new — see Phase 1 item 2's `Routes.razor` contract)

**Intent**: This is the "requests to panel actions without a valid session are rejected" contract from F-02's roadmap outcome.

**Contract**: Add `@attribute [Authorize]`. Show the logged-in user's email and a link to `/logout`. Unauthenticated access to `/` now redirects to `/login` (the cookie scheme's configured `LoginPath`, driven automatically by `[Authorize]` + `AddAuthorization()`).

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- Local: navigating to `/` while logged out redirects to `/login`
- Local: submitting wrong credentials shows the generic error, logs a warning, sets no cookie
- Local: submitting correct credentials redirects to `/`, shows the placeholder with the logged-in email, and a page reload keeps the session (cookie persists)
- Local: clicking logout redirects to `/login`, and `/` immediately redirects back to `/login` again
- Production: push to `main`; after deploy, the same full cycle (`https://kododo.dev/lassie/login` → wrong creds → right creds → `/lassie` shows placeholder → logout → redirected correctly) works with every generated link/redirect correctly carrying the `/lassie` prefix — this is what actually proves the path-base middleware from Critical Implementation Details is correct, since local dev never exercises it
- Production: `docker compose logs lassie` on the VPS shows no startup errors and the expected login/logout `ILogger` lines when exercised

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to close out the change.

---

## Testing Strategy

No automated test project exists yet (deferred again in this slice — see "What We're NOT Doing"). All verification is manual, as detailed in each phase's Manual Verification section above.

### Manual Testing Steps:

1. Local: `dotnet run`, confirm the placeholder renders with no console errors (Phase 1).
2. Local: seed check via `psql` — one `Users` row, hashed password, idempotent across restarts (Phase 2).
3. Local: full login → session-persists-on-reload → logout → re-gated cycle (Phase 3).
4. Production: push to `main`, repeat the full login/logout cycle at `kododo.dev/lassie`, confirming path-prefixed redirects and assets all resolve correctly (Phase 3).

## Performance Considerations

Blazor Server holds one stateful SignalR circuit per connected browser tab — acceptable at MVP scale (single admin, low concurrency per PRD's `target_scale`). No query or caching concerns: `Users` has exactly one row.

## Migration Notes

Second migration ever created for this project (`InitialCreate` and `AddAuditLogEntityIndex` already exist from F-01). Same `dotnet ef migrations add` → commit → auto-apply-on-deploy convention.

## References

- Roadmap item: `context/foundation/roadmap.md` → `F-02`
- PRD requirements: `context/foundation/prd.md` → FR-011, Access Control
- Prior pattern: `context/archive/2026-08-04-persistence-layer-foundation/plan.md` (migration/seed/fail-fast conventions)
- Lessons: `context/foundation/lessons.md` (audit load-before-mutate rule — informs why `User` is deliberately not `IAuditable`)
- Infrastructure: `context/foundation/infrastructure.md` (Caddy `handle_path` behavior behind `kododo.dev/lassie`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Blazor Server scaffolding + cookie-auth infrastructure

#### Automated

- [x] 1.1 `dotnet build src/lassie.csproj` succeeds — 3c96ebb

#### Manual

- [x] 1.2 `dotnet run --project src/lassie.csproj` starts without throwing — 3c96ebb
- [x] 1.3 `http://localhost:5092/` renders the placeholder page with no browser console errors — 3c96ebb

### Phase 2: Users table + password hashing + startup seed

#### Automated

- [x] 2.1 `dotnet build src/lassie.csproj` succeeds — 68e8d51
- [x] 2.2 `src/Migrations/*_AddUsers.cs` exists after `dotnet ef migrations add AddUsers` — 68e8d51

#### Manual

- [x] 2.3 Local dev boot seeds exactly one `Users` row matching `ADMIN_EMAIL`, with a hashed (non-plaintext) `PasswordHash` — 68e8d51
- [x] 2.4 Restarting the app a second time does not duplicate or error — 68e8d51

### Phase 3: Login/logout pages + protected gate + production verification

#### Automated

- [x] 3.1 `dotnet build src/lassie.csproj` succeeds — c52ace3

#### Manual

- [x] 3.2 Local: unauthenticated `/` redirects to `/login` — c52ace3
- [x] 3.3 Local: wrong credentials show generic error, log a warning, set no cookie — c52ace3
- [x] 3.4 Local: correct credentials log in, session persists across reload — c52ace3
- [x] 3.5 Local: logout redirects to `/login` and re-gates `/` — c52ace3
- [ ] 3.6 Production: full login/logout cycle works at `kododo.dev/lassie` with correct path-prefixed redirects
- [ ] 3.7 Production: `docker compose logs lassie` shows clean startup and expected login/logout log lines
