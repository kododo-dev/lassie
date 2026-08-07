# License Creation and Verification Implementation Plan

## Overview

Implement roadmap slice `S-02` (the north star): an admin creates a license — a text label and an
optional expiry date — and the system generates a unique API key, shown exactly once. A client
app then verifies that license's validity through a machine-to-machine (M2M) API endpoint,
authenticating with that key.

## Current State Analysis

- No `License` entity, no API-key handling, and no second authentication scheme exist anywhere in
  the codebase — this slice is fully greenfield (confirmed by repo-wide search in
  `context/changes/license-creation-and-verification/research.md`).
- `LassieDbContext` currently registers only `AuditLog` and `User` (`src/Data/LassieDbContext.cs`).
  `User` deliberately does **not** implement `IAuditable`, specifically because its `PasswordHash`
  must never land in `AuditLog.Snapshot` (`context/archive/2026-08-05-admin-auth-foundation/plan.md:118`).
- `AddAuditLogEntries` (`src/Data/LassieDbContext.cs:42-61`) snapshots an `IAuditable` entity's
  entire `OriginalValues` into `AuditLog.Snapshot` (jsonb) on every `Modified`/`Deleted` save, with
  no field-level exclusion mechanism. `Added` is never audited.
- `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)`
  (`src/Program.cs:23-34`) registers Cookie as both the only and the default scheme.
  `AddAuthorization()` has no policies configured, and nothing in `Program.cs` currently attaches
  `.RequireAuthorization()`/`[AllowAnonymous]` to any endpoint — the Blazor auth gate lives entirely
  at the component level (`Routes.razor`'s `<AuthorizeRouteView>`), which has no effect on
  minimal-API routes.
- The only existing minimal-API route is the scaffolded `app.MapGet("/weatherforecast", ...)`
  (`src/Program.cs:118-130`) — no auth, no established request/response JSON convention.
- `PasswordHasher<User>` (`src/Program.cs:40`) is the only secret-hashing code in the app; it embeds
  a random salt per hash (PBKDF2), which is why `Login.razor` can only *verify* a password against
  an already-identified row (found via the plaintext, indexed `Email`) — it cannot support
  "hash the incoming value and look up a row by equality," which is exactly what verifying an
  incoming API key needs.
- The panel shell (`MainLayout.razor`, Pico.css, the relative-link convention) from the reverted
  `module-catalog-management` change is in place and reusable for this slice's new pages.

## Desired End State

An admin, logged into the panel, can create a license by entering a text label and an optional
expiry date. On success, the panel shows the generated API key exactly once, masked by default,
with a reveal toggle and a copy-to-clipboard button — the raw key is never persisted, never appears
in a URL, and is not retrievable again afterward. A client app can then call a verification
endpoint with that key (via a header) and receive `{"valid": true|false}` based on whether the
license is unexpired; a missing or unrecognized key returns `401`; anything else (an actual
service/database problem) surfaces as a `5xx`, never as `valid: false`.

**Verification**: manually walk create → reveal → copy → verify (valid, expired, and
missing/bogus-key cases) against the deployed app at `https://kododo.dev/lassie`, checking
response time and that the key never appears in server logs — see Phase 3 Manual Verification.

### Key Discoveries:

- `src/Data/LassieDbContext.cs:23-25` — unique single-column index pattern
  (`.HasIndex(x => x.Prop).IsUnique()`) to reuse for both `Label` and `ApiKeyHash`.
- `src/Migrations/20260807174444_RemoveLicenseFields.cs` — most recent migration, cleanest style
  template (`bigint` identity PKs, `text` columns, `timestamp with time zone` for
  `DateTimeOffset`).
- `context/changes/module-catalog-management/plan.md` (plan-review findings, reused here):
  namespace imports need an explicit `@using` in `_Imports.razor`; links/`NavigateTo` calls must be
  relative (no leading `/`) to survive the production `/lassie` path-base; `DbUpdateException`
  handling must be narrowed to `Npgsql.PostgresException { SqlState: "23505" }` before showing a
  duplicate-value message, generic error otherwise.
- `System.Buffers.Text.Base64Url` (BCL, .NET 9+, already available — no new package) — URL/header-
  safe encoding for the raw generated key, avoiding the `+`/`/`/`=` characters of standard base64.

## What We're NOT Doing

- No license list view (`S-05`), no edit/audit-history (`S-03`), no deactivate/reactivate (`S-04`).
  `License` does **not** implement `IAuditable` in this slice — deferred to `S-03`, which must also
  decide how to keep `ApiKeyHash` out of `AuditLog.Snapshot` once edits start being audited.
- No `IsActive`/status column — validity in this slice is expiry-only
  (`ExpiresOn is null || ExpiresOn >= today (UTC)`).
- No API-key rotation/regeneration (FR-008 Non-Goal) — one key per license, generated once at
  creation.
- No new `AuthenticationScheme` registration for the verification endpoint — the handler validates
  the presented key manually.
- No rate limiting on the verification endpoint (not required by the PRD).
- No automated tests — manual verification only, matching every prior slice (`F-01`, `F-02`,
  reverted `S-01`) in this project so far.
- No changes to `/weatherforecast` or the deploy pipeline's post-deploy health check — it keeps
  polling `/weatherforecast` as a trivial liveness probe; the new endpoint's `401` response for a
  bogus/missing key is a valid HTTP response, not the `502`/`000` the health check watches for, so
  nothing about swapping it in is required or attempted here.
- No detailed invalid-reason breakdown in the verification response (expired vs. anything else) —
  FR-010 explicitly rejected this for MVP.

## Implementation Approach

Three phases, persistence → panel UI → integration, matching the shape that worked for the
(subsequently reverted) `module-catalog-management` change:

1. Model `License` and a static `ApiKeyHasher` utility (generate + deterministic-hash), wire up
   `LassieDbContext`, and ship the migration.
2. Build the panel's creation flow: a form that, on success, transitions in place (no navigation)
   to a reveal-once view of the new key.
3. Add the M2M verification endpoint and manually verify the full loop — including the two NFRs
   that matter most here (key secrecy, response time) — against the deployed app.

## Critical Implementation Details

- **The raw API key must never touch a URL, query string, or log line.** It exists only in the
  license-creation page's in-memory component state between generation and the admin copying it —
  never persisted anywhere (only its hash is), never passed via `NavigateTo`/redirect (which would
  either lose it or leak it into browser history / server access logs), and the verification
  endpoint must read it from a request **header** (not a query string), since query strings land in
  Caddy/ASP.NET Core access logs by default.
- **The reveal panel is a state transition within the same component, not a navigation.** Because
  the raw key is never stored, a fresh page load (via redirect) has no way to retrieve it again —
  `CreateLicense.razor` must render its "created" view directly from local state right after
  `SaveChangesAsync` succeeds, using the same `InteractiveServer` circuit, not `NavigateTo`.
- **Disambiguating which unique constraint was violated.** Unlike `module-catalog-management`
  (one unique index), this entity has two (`Label`, `ApiKeyHash`). On a narrowed
  `DbUpdateException`/`PostgresException { SqlState: "23505" }`, inspect
  `PostgresException.ConstraintName` to show "etykieta już istnieje" for a `Label` collision;
  treat an `ApiKeyHash` collision (astronomically unlikely at 256 bits of entropy) with the same
  generic error path as any other unexpected `DbUpdateException`.

## Phase 1: Data model, key generation/hashing, migration

### Overview

Model `License`, add a static key-generation/hashing utility, wire both into `LassieDbContext`,
and ship the EF Core migration.

### Changes Required:

#### 1. `License` entity

**File**: `src/Data/Licenses/License.cs`

**Intent**: The flat, standalone license unit (PRD Non-Goals: no customer/tenant entity). Follows
the `User` entity's shape exactly (`long Id`, `required string` for non-nullable text).

**Contract**:
```csharp
namespace Lassie.Data.Licenses;

public class License
{
    public long Id { get; set; }
    public required string Label { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public required string ApiKeyHash { get; set; }
}
```
`ExpiresOn` is a calendar date, not an instant — deliberately `DateOnly?`, not `DateTimeOffset?`
(see plan review F1: a picked-date-to-instant conversion has no obvious "right" time-of-day and
risks a silent one-day-early expiry). Maps to Postgres `date` (a new column-type precedent in this
codebase; existing precedent is only `timestamp with time zone`). Does **not** implement
`IAuditable` (see Current State Analysis / What We're NOT Doing).

#### 2. API-key generation and hashing

**File**: `src/Data/Licenses/ApiKeyHasher.cs`

**Intent**: Generate a high-entropy raw key (shown to the admin once) and its deterministic hash
(persisted, used for lookup on verification). A static class, not a DI singleton like
`PasswordHasher<User>` — that type is DI-registered because it implements Identity's
`IPasswordHasher<TUser>` interface shape; SHA-256 needs no such abstraction or per-instance state.

**Contract**:
```csharp
public static class ApiKeyHasher
{
    public static (string RawKey, string Hash) Generate();
    public static string Hash(string rawKey);
}
```
- `RawKey`: 32 bytes from `RandomNumberGenerator.GetBytes(32)`, encoded with
  `Base64Url.EncodeToString` (header/URL-safe, no new package).
- `Hash`: `Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)))` — deterministic,
  so the same raw key always hashes to the same value, enabling an indexed equality lookup.
  `Generate()` calls `Hash` internally so both entry points share one implementation.

#### 3. `LassieDbContext` registration

**File**: `src/Data/LassieDbContext.cs`

**Intent**: Register `License` and its two unique constraints, following the existing `User.Email`
pattern.

**Contract**: Add `public DbSet<License> Licenses => Set<License>();`, plus the matching
`using Lassie.Data.Licenses;` (same pattern as the existing `Lassie.Data.Auditing`/
`Lassie.Data.Users` imports). In `OnModelCreating`, add
`modelBuilder.Entity<License>().HasIndex(l => l.Label).IsUnique();` and
`modelBuilder.Entity<License>().HasIndex(l => l.ApiKeyHash).IsUnique();`.

#### 4. Migration

**File**: `src/Migrations/<timestamp>_AddLicenses.cs` (+ `.Designer.cs`, snapshot update)

**Intent**: Create the `Licenses` table with both unique indexes.

**Contract**: Generated via `dotnet ef migrations add AddLicenses --project src/lassie.csproj` —
no hand-editing; follow the codebase's existing migration conventions (`bigint` identity PK, `text`
columns) plus a `date`-typed nullable `ExpiresOn` column (EF Core's default mapping for
`DateOnly?`, npgsql maps this to Postgres `date`).

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build src/lassie.csproj`
- No pending model changes after the migration: `dotnet ef migrations has-pending-model-changes --project src/lassie.csproj`

#### Manual Verification:

- `dotnet run --project src/lassie.csproj` locally applies the new migration on startup with no
  errors; the `Licenses` table exists in the local Postgres DB with `Label` and `ApiKeyHash` both
  uniquely indexed.
- Sanity-check `ApiKeyHasher` locally (e.g. a scratch call from `Program.cs` during dev, removed
  before committing, or the C# interactive REPL): two calls to `Generate()` produce different raw
  keys and hashes; `Hash(rawKey)` recomputed from a generated `RawKey` matches its `Hash`.

---

## Phase 2: Panel creation flow with reveal-once key

### Overview

A form to create a license that, on success, transitions in place to a reveal-once view of the
generated key.

### Changes Required:

#### 1. License creation page

**File**: `src/Components/Pages/CreateLicense.razor` (route: `/licenses/new`)

**Intent**: `@attribute [Authorize]`, `@rendermode InteractiveServer` (matching `PanelHome.razor`'s
precedent for an authenticated, interactive page — not the static-SSR pattern `Login`/`Logout` use,
since this page needs no `HttpContext.SignInAsync`). Injects `LassieDbContext` directly, matching
the app's no-service-layer convention. Two local UI states in one component: a form (`Label` text
input, optional `ExpiresOn` date input via Blazor's built-in `InputDate<DateOnly?>`) and, after a
successful save, a "created" view rendered from local state — never a navigation (see Critical
Implementation Details).

**Contract**: The form model marks `Label` `[Required]` and the form uses
`<DataAnnotationsValidator/>` (matching the precedent in the reverted `LicenseFields.razor`'s
`FieldFormModel`), so a blank submission shows a clear "required" message rather than surfacing as
a confusing duplicate-label error on a second blank attempt. On submit, call
`ApiKeyHasher.Generate()`, construct the `License`, `Add` + `SaveChangesAsync`. Catch `DbUpdateException` narrowed to
`Npgsql.PostgresException { SqlState: "23505" }`; if `ConstraintName` matches the `Label` index,
show a "ta etykieta już istnieje" message and keep the form; for anything else, show a generic
error. On success, keep the raw key in a local field (e.g. `_createdRawKey`) and render the
"created" view: label, expiry, and the key masked by default (`••••••••`) with a reveal-toggle
button and a copy-to-clipboard button (`IJSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", ...)`
— the app's first JS interop call; no existing precedent to follow, but it's a built-in Blazor
Server capability, no new package).

#### 2. Nav link

**File**: `src/Components/Layout/MainLayout.razor`

**Intent**: Make the creation page reachable from the panel shell.

**Contract**: Add `<a href="licenses/new">New License</a>` — relative, no leading slash, per the
established `/lassie` path-base convention.

#### 3. Namespace import

**File**: `src/Components/_Imports.razor`

**Intent**: None needed beyond what already exists — `CreateLicense.razor` lives directly under
`Components/Pages/`, same as `PanelHome.razor`, so no new `@using` is required (the namespace-import
gotcha from `module-catalog-management` only applied to the new `Components/Layout/` folder, which
already has its import). Confirm this holds during implementation; add the import only if the
build actually fails without it.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification:

- Logged in, navigate to `/licenses/new` (via the new nav link), create a license with a label and
  an expiry date; the reveal-once view shows the masked key; the reveal button unmasks it; the copy
  button copies it (paste elsewhere to confirm).
- Creating a second license with the same label shows the friendly duplicate-label error, not an
  unhandled-exception page.
- Verify the relative-link/path-base convention against the deployed app at
  `https://kododo.dev/lassie/licenses/new` (not just local `dotnet run`), per the established
  gotcha in `context/foundation/lessons.md`/prior plan-reviews.
- Panel remains usable on a phone-width viewport (PRD NFR: responsive, no functionality lost).

---

## Phase 3: Verification API endpoint (M2M)

### Overview

A minimal-API endpoint that authenticates via a header-supplied API key and returns the license's
validity, with error handling that keeps "service unavailable" and "license invalid" distinct.

### Changes Required:

#### 1. Verification endpoint

**File**: `src/Program.cs`

**Intent**: Register a new minimal-API route, alongside the existing `/weatherforecast` sample,
that reads the API key from a request header (not a query string — see Critical Implementation
Details), looks it up by its deterministic hash, and returns validity. No `.RequireAuthorization()`
and no new `AuthenticationScheme` — the handler validates the key itself.

**Contract**: `app.MapGet("/api/license/verify", ...)`, reading the `X-Api-Key` header.
- Header missing/empty → `Results.Unauthorized()` (`401`).
- Header present but `ApiKeyHasher.Hash(presentedKey)` matches no `License.ApiKeyHash` →
  `Results.Unauthorized()` (`401`) — the PRD's acceptance criteria don't distinguish
  invalid-format from unrecognized keys.
- Match found → `Results.Ok(new { valid = license.ExpiresOn is null || license.ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow) })`
  (`200`) — `>=` (not `>`) so the license stays valid through the entirety of its expiry date in
  UTC, matching the admin's "valid through this date" intent from Phase 2.
- No broad `try`/`catch` around the lookup — an unexpected exception (DB unreachable, etc.) must
  propagate to ASP.NET Core's default `5xx` handling, never be coerced into `valid: false`.

#### 2. Example requests

**File**: `src/lassie.http`

**Intent**: Establish the request/response convention this file lacked before (it only had the
scaffolded `/weatherforecast` example).

**Contract**: Add a `GET {{lassie_HostAddress}}/api/license/verify` example with an
`X-Api-Key: {{apiKey}}` header (placeholder variable), and one with the header omitted, matching
the file's existing `###`-separated style.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification:

- Against the deployed app (`https://kododo.dev/lassie`): create a license via the panel, then
  `GET /lassie/api/license/verify` with `X-Api-Key: <the real key>` → `200 {"valid":true}`.
- Same call with a missing or bogus key → `401`.
- Create a license with an expiry date in the past, call verify with its real key →
  `200 {"valid":false}`.
- Measure response time (e.g. `curl -w "%{time_total}\n"`) — comfortably under the 500ms guardrail.
- Check Caddy/ASP.NET Core access logs after a verify call — the raw key must not appear anywhere
  in them (confirms the header-not-query-string choice holds in practice, including through the
  reverse proxy).
- Code review confirms no exception handler around the endpoint's lookup logic could coerce an
  unexpected failure into `valid: false` (a live outage test against the shared VPS is
  deliberately not attempted here — too risky for a box hosting other people's apps).

---

## Testing Strategy

### Manual Testing Steps:

1. Phase 1: local migration + `ApiKeyHasher` sanity check (see Phase 1 Manual Verification).
2. Phase 2: full create → reveal → copy flow in the panel, duplicate-label error path, path-base
   check against production, responsive check on a phone-width viewport.
3. Phase 3: end-to-end verify calls (valid, expired, missing/bogus key) against the deployed app,
   response-time measurement, and a log check for key leakage.

No automated test project is introduced in this slice (explicit decision — see What We're NOT
Doing).

## Performance Considerations

The verification endpoint is a single indexed-column equality lookup (`ApiKeyHash`) against a
table sized for the PRD's `target_scale: small/low`, so no caching or query optimization is
needed to meet the <500ms guardrail. `ApiKeyHasher.Hash` is a single SHA-256 pass — negligible
compared to the DB round-trip.

## Migration Notes

Standard forward-only migration (`AddLicenses`) — no existing data to migrate, since `License`
doesn't exist yet anywhere. Auto-applies on next deploy via the established
`context.Database.Migrate()` startup pattern (`src/Program.cs:71-95`).

## References

- Research: `context/changes/license-creation-and-verification/research.md`
- Reused conventions: `context/changes/module-catalog-management/plan.md` (plan-review findings),
  `context/archive/2026-08-05-admin-auth-foundation/plan.md` (relative-link/path-base gotcha,
  `IAuditable` rationale for excluding secrets)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Data model, key generation/hashing, migration

#### Automated

- [x] 1.1 Build succeeds: `dotnet build src/lassie.csproj` — 1dc9e8c
- [x] 1.2 No pending model changes: `dotnet ef migrations has-pending-model-changes --project src/lassie.csproj` — 1dc9e8c

#### Manual

- [x] 1.3 Local migration applies cleanly; `Licenses` table exists with both unique indexes — 1dc9e8c
- [x] 1.4 `ApiKeyHasher` round-trip and uniqueness sanity-checked locally — 1dc9e8c

### Phase 2: Panel creation flow with reveal-once key

#### Automated

- [x] 2.1 Build succeeds: `dotnet build src/lassie.csproj` — 1ea0a61

#### Manual

- [x] 2.2 Create license via `/licenses/new`; reveal/copy buttons work on the generated key — 1ea0a61
- [x] 2.3 Duplicate label shows a friendly error, not an unhandled exception — 1ea0a61
- [x] 2.4 Relative-link/path-base convention verified against the deployed `/lassie` prefix
- [x] 2.5 Panel usable on a phone-width viewport

### Phase 3: Verification API endpoint (M2M)

#### Automated

- [ ] 3.1 Build succeeds: `dotnet build src/lassie.csproj`

#### Manual

- [ ] 3.2 Valid key against deployed `/lassie/api/license/verify` returns `200 {"valid":true}`
- [ ] 3.3 Missing/bogus key returns `401`
- [ ] 3.4 Expired license's real key returns `200 {"valid":false}`
- [ ] 3.5 Response time measured comfortably under 500ms
- [ ] 3.6 Raw key confirmed absent from Caddy/ASP.NET Core access logs
- [ ] 3.7 Code review confirms unexpected failures surface as `5xx`, never `valid:false`
