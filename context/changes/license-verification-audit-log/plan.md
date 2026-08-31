# License Verification Audit Log — Implementation Plan

## Overview

Record one row per **resolved** call to `GET /api/license/verify` — timestamp, license, caller
IP, User-Agent, and the license status observed at that moment — and give the admin a paginated
per-license view of that history in the panel. The write happens off the verification response
path so it cannot add latency to or fail the response. Rows are pruned after 90 days.

Collection only. Detecting that a license is being used by more than the one process it is meant
for is a **separate future issue**; this change just captures the trail that issue will need.

## Current State Analysis

- **Verify endpoint** (`src/Program.cs:143-161`): minimal-API `GET /api/license/verify`, injects
  `HttpRequest` + `LassieDbContext` only. Reads the `X-Api-Key` header, hashes it
  (`src/Data/Licenses/ApiKeyHasher.cs`), looks up `Licenses` by the unique `ApiKeyHash` column,
  returns `200 {"valid": <bool>}` where `valid = license.Status == LicenseStatus.Active`. Missing
  or unrecognized key → bare `401`. **No broad `try/catch`** — a DB/unexpected failure must
  propagate as `5xx`, never be coerced to `valid:false`.
- **Real client IP is already resolvable.** `app.UseForwardedHeaders(...)` runs first in the
  pipeline (`src/Program.cs:54-60`) with both `KnownProxies` and `KnownIPNetworks` cleared, so
  `HttpContext.Connection.RemoteIpAddress` is rewritten from the right-most `X-Forwarded-For`
  entry Caddy sets — i.e. the true external caller. Single proxy hop, no CDN. The handler does not
  currently take `HttpContext`, so it has no way to read this yet.
- **`AuditLog` cannot be reused.** `LassieDbContext.AddAuditLogEntries()` (`src/Data/LassieDbContext.cs:53-85`)
  is hard-gated on `EntityState.Modified or Deleted` and builds a snapshot from a mutated tracked
  entity's `OriginalValues`. A verify call is a pure read — it never calls `SaveChanges`, never
  trips that path. `ChangeType` is a closed 2-value enum. A verification-event log needs its own
  entity, `DbSet`, migration, and an explicit write.
- **No pagination anywhere in the panel.** `PanelHome.razor:57` loads *all* licenses into a
  client-side `MudDataGrid` (`Items=`). There is **no license detail page** — only
  `/licenses/{Id:long}/edit` (`src/Components/Pages/EditLicense.razor`).
- **No HTTP-logging / request-capture middleware exists** anywhere in the pipeline.
- **Entity conventions** (`src/Data/*`, `src/Data/LassieDbContext.cs:15-37`): `long Id` identity
  (`UseIdentityByDefaultColumns`), `text` columns, `timestamp with time zone`, `boolean`; Npgsql /
  Postgres 17; fluent config **inline** in `OnModelCreating` (no `IEntityTypeConfiguration`
  classes); hand-written `Up`/`Down` migration pair + `.Designer.cs` + single model snapshot;
  `context.Database.Migrate()` at startup (`src/Program.cs:79`).
- **Blazor Server per-circuit `DbContext`** is long-lived — read-only queries must use
  `.AsNoTracking()` (`context/foundation/lessons.md:35-43`).
- **Tests**: `IntegrationTestBase` + `[Collection("Postgres")]` + `[Trait("Category","Integration")]`;
  `VerifyEndpointKeySecrecyTests.cs` is the reference for hitting `/api/license/verify` (seed via
  `DbContext`, act via `HttpClient`). Under `TestServer` there is no socket, so `RemoteIpAddress`
  is `null` and `UseForwardedHeaders` is a no-op unless the test sets `X-Forwarded-For` on the
  request explicitly.

Full findings: `context/changes/license-verification-audit-log/research.md`.

## Desired End State

- Every verify call that resolves to a license writes exactly one `LicenseVerificationEvent` row,
  asynchronously, with no measurable change to verify response latency (still well under the
  500ms guardrail; current steady-state ~110-140ms).
- A verify call with a missing or unrecognized key writes **nothing**.
- An audit-write failure (DB slow/down/constraint) never changes the verify response and never
  produces a `5xx` from the verify path — it is logged and dropped.
- `LicenseVerificationEvent` rows older than the configured retention window (default 90 days) are
  removed by a background sweep.
- From the panel, the admin can open `/licenses/{id}/verifications` and page through that
  license's verification history, newest first, showing timestamp, caller IP, observed status,
  and User-Agent. The page is reachable from the license list and from the edit page.
- `prd.md` carries an FR and an NFR line covering this feature and its retention/privacy posture.

**Verification:** integration tests below; manual walkthrough in the panel against a license that
has been polled a few times with different `X-Forwarded-For` values.

### Key Discoveries:

- `src/Program.cs:143-161` — the verify handler; add `HttpContext` to its parameter list to reach
  `Connection.RemoteIpAddress` and the request headers.
- `src/Program.cs:54-60` — forwarded-headers already wired; IP resolution is free.
- `src/Data/LassieDbContext.cs:53-85` — why the generic audit path does not apply here.
- `src/Components/Pages/PanelHome.razor:28-49` — `MudDataGrid` usage + the actions column where a
  "history" button goes; `:57` — the `.AsNoTracking()` read pattern.
- `src/Components/Pages/EditLicense.razor:19-23` — the "not found" alert pattern to mirror.
- `src/Components/Shared/LicenseStatusBadge.razor` (used at `PanelHome.razor:33`) — reuse for the
  status column.
- `context/foundation/lessons.md:35-43` — per-circuit `DbContext` → `.AsNoTracking()`.

## What We're NOT Doing

- **No detection / alerting / thresholds.** No "distinct IP count", no "used on N devices"
  warning, no seat limit on `License`. Separate future issue.
- **No logging of unauthenticated calls** (missing/unrecognized key). Non-null `LicenseId` FK.
- **No client-side change.** The client keeps sending only `X-Api-Key`. `UserAgent` is stored as
  received (often empty). A follow-up note recommends giving the client a descriptive UA.
- **No change to the verify response contract** — still `{"valid": <bool>}`.
- **No new panel-wide pagination abstraction** — `MudDataGrid`'s `ServerData` is used directly on
  the one new page; `PanelHome` is left as-is.
- **No cold archive / export** of pruned rows. Pruned means gone.
- **No IP hashing, geolocation, or ASN lookup** at write time. Raw IP stored; any geo work belongs
  to the later detection issue.
- **No reuse of `AuditLog`** and no change to `AddAuditLogEntries()`.

## Implementation Approach

A dedicated `LicenseVerificationEvent` entity, written through an in-process producer/consumer
seam: the verify handler builds the event and hands it to a singleton `VerificationEventQueue`
(a bounded `Channel<T>`) with a **non-blocking, non-throwing** `TryEnqueue`. A
`VerificationEventWriter : BackgroundService` drains the channel, batches, and inserts on its own
DI scope with a swallowed try/catch. A second `VerificationEventRetentionService : BackgroundService`
periodically bulk-deletes expired rows. The panel gets one new `@rendermode InteractiveServer`
page using `MudDataGrid` `ServerData` for true server-side paging.

This deliberately **inverts** the S-03 audit pattern (which puts audit rows in the *same*
transaction as the change for atomicity — `context/archive/2026-08-04-persistence-layer-foundation/plan.md:44`).
Here atomicity is traded away on purpose: the verify response's latency and its
"service-unavailable ≠ license-invalid" guarantee outrank not-losing-an-audit-row.

## Critical Implementation Details

- **Enqueue placement.** The event is built and enqueued only on the path where `license` is
  non-null, *after* `valid` is computed, and never before a `Results.Unauthorized()` return.
  Bounded channel + `TryWrite` means `Enqueue` itself won't throw; the *construction* (reading
  `RemoteIpAddress`, the headers, `license.Status`, the truncation) can, so the entire
  build+enqueue block sits inside one narrow `try/catch` that logs and swallows — never rethrows.
  The lookup itself stays outside any try/catch so genuine outages still become `5xx`.
- **Writer uses its own scope.** The `BackgroundService` must resolve a fresh
  `LassieDbContext` from `IServiceScopeFactory` per drain batch — it must not capture the
  request/circuit-scoped context. Insert with plain `Add`/`AddRange` + `SaveChangesAsync`; these
  rows are not `IAuditable`, so `AddAuditLogEntries()` is a no-op for them.
- **Graceful vs ungraceful stop.** On `StopAsync`, drain and flush whatever is buffered. Accept
  that a hard crash loses the in-memory buffer — documented, acceptable for an audit-not-ledger.
- **Bounded channel full policy.** `BoundedChannelFullMode.DropWrite`; increment a counter and
  log at Warning when a drop happens, so sustained overload is visible. Capacity is config
  (`Verification:QueueCapacity`, default e.g. 10000).
- **Hosted services must not run under `WebApplicationFactory`.** `IntegrationTestBase` shares one
  `NpgsqlConnection` across the request-scoped context (enlisted in the test transaction via
  middleware — `LassieWebApplicationFactory.cs:41-58`). A `BackgroundService` resolving its own
  scope on that same connection, off the request pipeline and on a background thread, breaks both
  transaction enlistment and Npgsql's single-command-per-connection rule — for the new tests *and*
  every existing `[Collection("Postgres")]` test. `LassieWebApplicationFactory` must
  `services.RemoveAll<IHostedService>()` so the writer and retention service never start in these
  tests. The pipeline is verified in three seams instead (see Testing Strategy).
- **No cross-thread flush hook.** Because the writer is tested in isolation (not end-to-end
  through the host), there is no `WaitForIdleAsync` on the production queue. The writer's
  one-batch drain is `internal` + `InternalsVisibleTo(Lassie.Tests)` so a test can drive one
  batch directly. Tests must not `Task.Delay` to "wait for" a write.
- **TestServer has no socket.** The enqueue-seam test asserting on `ClientIp` must set
  `X-Forwarded-For` explicitly on the `HttpRequestMessage`; otherwise `RemoteIpAddress` is `null`
  and the enqueued `ClientIp` is `null` (itself a valid case to assert).
- **Panel page rendering.** `MudDataGrid` `ServerData` callback runs on the circuit's
  `DbContext` — use `.AsNoTracking()` and keep the query transient (no stored `List<T>`).

## Phase 1: Data model + migration

### Overview

Add the `LicenseVerificationEvent` entity, register it on `LassieDbContext` with indexes, generate
the migration, and record the feature in `prd.md`.

### Changes Required:

#### 1. New entity

**File**: `src/Data/Licenses/LicenseVerificationEvent.cs`

**Intent**: A plain POCO, one row per resolved verify call. Not `IAuditable`. Captures who called,
when, from where, and what status the license had at that instant.

**Contract**: Properties —
- `long Id` (identity PK, repo convention)
- `long LicenseId` + `License License` navigation (required FK)
- `DateTimeOffset OccurredAtUtc`
- `string? ClientIp` — `HttpContext.Connection.RemoteIpAddress?.ToString()`; nullable
- `string? UserAgent` — the `User-Agent` request header verbatim, truncated to 512 chars before
  enqueue; nullable
- `string? ForwardedForRaw` — the raw inbound `X-Forwarded-For` / `X-Original-For` value,
  truncated to 256 chars; nullable. Defensive: lets the true IP be re-derived if a CDN / extra
  proxy hop is ever put in front (see research §2). Carry a one-line code comment tying it to the
  "single Caddy hop" assumption in Current State Analysis, so a future proxy-topology change
  prompts a look at `ClientIp`.
- `LicenseStatus ObservedStatus` — `license.Status` at call time (`Active` / `Expired` /
  `Deactivated`); richer than a bare bool, no analytics/Non-Goal concern. The panel renders
  "valid" as `ObservedStatus == Active`.

No API key, no key hash, on this entity — ever.

#### 2. DbContext registration

**File**: `src/Data/LassieDbContext.cs`

**Intent**: Expose the `DbSet` and configure the FK + the two indexes the read and prune paths
need.

**Contract**: Add `public DbSet<LicenseVerificationEvent> LicenseVerificationEvents => Set<...>();`.
In `OnModelCreating`, inline (matching the existing style):
- FK `LicenseVerificationEvent.LicenseId` → `License`, required, `OnDelete` = `Cascade` (deleting
  a license — not currently possible in the app, but keep the schema honest — takes its events).
- Composite index `(LicenseId, OccurredAtUtc)` — the per-license newest-first read.
- Index `(OccurredAtUtc)` — the retention sweep's `WHERE OccurredAtUtc < cutoff`.

#### 3. Migration

**File**: `src/Migrations/<timestamp>_AddLicenseVerificationEvents.{cs,Designer.cs}` + `src/Migrations/LassieDbContextModelSnapshot.cs`

**Intent**: Create the `LicenseVerificationEvents` table and its indexes; `Down` drops it.

**Contract**: `dotnet ef migrations add AddLicenseVerificationEvents -p src/lassie.csproj`.
Table columns per the entity; `bigint` identity PK; `text` for the string columns; `timestamp
with time zone` for `OccurredAtUtc`; `integer` for `ObservedStatus`; FK constraint +
`IX_LicenseVerificationEvents_LicenseId_OccurredAtUtc` + `IX_LicenseVerificationEvents_OccurredAtUtc`.
Applied automatically at startup by the existing `context.Database.Migrate()`.

#### 4. PRD alignment

**File**: `context/foundation/prd.md`

**Intent**: S-07 has no backing FR in v1 (`roadmap.md` Open Roadmap Questions). Add one, plus an
NFR line for the retention/privacy posture, so the PRD and the build stay in sync.

**Contract**: Under `## Functional Requirements` → `### API weryfikacji`, add **FR-013**:
"System zapisuje wpis audytowy dla każdego rozpoznanego wywołania API weryfikacji (znacznik
czasu, licencja, adres IP wywołującego, User-Agent, zaobserwowany status licencji). Administrator
może przeglądać tę historię per licencja. Priority: nice-to-have (rozszerzenie poza pierwotny
zakres v1)." Under `## Non-Functional Requirements`, add: "Zapis audytu weryfikacji nie wpływa na
gwarancję < 500ms ani na rozróżnienie niedostępność/nieważność — zapis jest asynchroniczny, a
jego błąd nie zmienia odpowiedzi API. Adresy IP wywołujących są przechowywane maks. 90 dni,
dostępne wyłącznie dla administratora; podstawa: zapobieganie nadużyciom licencji."

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build src/lassie.csproj`
- Migration applies against a clean DB (covered by the integration-test fixture spinning up
  `postgres:17` and running `Migrate()`): `dotnet test --filter "Category=Integration&FullyQualifiedName~FixtureSmoke"`
- `dotnet format --verify-no-changes` (pre-commit hook parity)
- Model snapshot regenerated (no pending-model-changes warning at startup / in tests)

#### Manual Verification:

- `LicenseVerificationEvents` table + both indexes present in the DB after startup (`\d
  "LicenseVerificationEvents"` via psql).
- `prd.md` reads cleanly with FR-013 and the NFR line in place.

**Implementation Note**: After automated verification passes, pause for manual confirmation before
Phase 2.

---

## Phase 2: Capture pipeline

### Overview

Build the queue, the background writer, and the retention sweep; wire the verify handler to
enqueue on the resolved-license path only.

### Changes Required:

#### 1. Event queue

**File**: `src/Data/Verification/VerificationEventQueue.cs` (new folder `src/Data/Verification/`)

**Intent**: A singleton seam between the request thread and the writer. Producer side is
non-blocking and never throws; if the buffer is full the event is dropped and counted.

**Contract**: Define `IVerificationEventQueue { void Enqueue(LicenseVerificationEvent evt);
ChannelReader<LicenseVerificationEvent> Reader; }`. `VerificationEventQueue` implements it, wrapping
`Channel.CreateBounded<LicenseVerificationEvent>(new BoundedChannelOptions(capacity)
{ FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true })`. `Enqueue` calls
`Writer.TryWrite` and, on `false`, increments a dropped counter + logs Warning. `capacity` from
`Verification:QueueCapacity` (default 10000). The handler depends on `IVerificationEventQueue` so
the enqueue-seam test can substitute an inspectable fake (records enqueued events; `Reader` is an
empty completed channel). No production flush/idle hook — see Testing Strategy.

#### 2. Background writer

**File**: `src/Data/Verification/VerificationEventWriter.cs`

**Intent**: Drain the channel, batch, and insert on a fresh scope; never let a write error escape.

**Contract**: `class VerificationEventWriter(IServiceScopeFactory scopes, IVerificationEventQueue
queue, ILogger<VerificationEventWriter> log) : BackgroundService`. `ExecuteAsync` loop: `await
Reader.WaitToReadAsync`, then call `internal DrainOnceAsync()` — reads up to N (e.g. 200) or until
empty, `using var scope = scopes.CreateScope()`, resolve `LassieDbContext`, `AddRange`, `await
SaveChangesAsync`, all inside `try { } catch (Exception ex) { log.LogError(ex, ...); }` so a failed
batch is logged and the loop continues. `DrainOnceAsync` is `internal` +
`InternalsVisibleTo(Lassie.Tests)` so the writer-seam test drives one batch directly. `StopAsync`
override: drain remaining before returning.

#### 3. Retention sweep

**File**: `src/Data/Verification/VerificationEventRetentionService.cs`

**Intent**: Periodically remove rows past the retention window so the table and the stored IP data
stay bounded.

**Contract**: `class VerificationEventRetentionService(IServiceScopeFactory scopes, IConfiguration
config, ILogger<...> log) : BackgroundService`. Loop with a `PeriodicTimer` (interval config
`Verification:RetentionSweepInterval`, default 6h; run once on startup after a short delay).
Each tick: read `Verification:RetentionDays` (default 90); if the configured value is `< 1`, skip
the sweep and log a Warning — never let a `0`/negative value turn `cutoff` into "now or later"
and delete the whole table. Otherwise `cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(days)`,
then `await db.LicenseVerificationEvents.Where(e => e.OccurredAtUtc < cutoff).ExecuteDeleteAsync()`;
log the deleted count at Information. Wrapped in try/catch (a failed sweep must not crash the
host).

#### 4. Service registration

**File**: `src/Program.cs`

**Intent**: Register the queue as a singleton and both hosted services.

**Contract**: `builder.Services.AddSingleton<VerificationEventQueue>();`
`builder.Services.AddHostedService<VerificationEventWriter>();`
`builder.Services.AddHostedService<VerificationEventRetentionService>();` — placed with the other
`builder.Services.Add*` calls.

#### 5. Verify handler wiring

**File**: `src/Program.cs` (the `/api/license/verify` handler, currently `:143-161`)

**Intent**: After a successful license resolution, build the event from `HttpContext` and enqueue
it. Leave every other path — missing key, unknown key, unexpected error — untouched.

**Contract**: Add `HttpContext http` and `IVerificationEventQueue queue` to the handler's
parameter list. On the branch where `license is not null`: compute `valid` as today, then
construct `LicenseVerificationEvent { LicenseId = license.Id, OccurredAtUtc = DateTimeOffset.UtcNow,
ClientIp = http.Connection.RemoteIpAddress?.ToString(), UserAgent = <User-Agent header, trimmed to
512>, ForwardedForRaw = <X-Forwarded-For / X-Original-For, trimmed to 256>, ObservedStatus =
license.Status }` and call `queue.Enqueue(evt)` before `return Results.Ok(new { valid })`. The
whole build-event-and-enqueue block (event construction *and* the `Enqueue` call) is wrapped in
one narrow `try/catch` that logs at Warning and swallows — it must never rethrow, so a valid
license can never surface as a `5xx` because of audit code. The license *lookup* keeps its
existing no-try/catch stance (real outages still surface as `5xx`).

#### 6. Disable hosted services in the test factory

**File**: `src/Lassie.Tests/Infrastructure/LassieWebApplicationFactory.cs`

**Intent**: Keep the writer and retention `BackgroundService`s from starting under
`WebApplicationFactory`. They would resolve `LassieDbContext` on their own scope against the shared
transactional `NpgsqlConnection`, off the request pipeline and on a background thread — breaking
transaction enlistment and Npgsql's one-command-per-connection rule for every
`[Collection("Postgres")]` test, not just the new ones.

**Contract**: In `ConfigureServices`, after the existing registrations, call
`services.RemoveAll<IHostedService>()`
(`Microsoft.Extensions.DependencyInjection.Extensions`, already imported).

### Success Criteria:

#### Automated Verification:

- Build, `dotnet format --verify-no-changes`, and the full `dotnet test` suite green — the test
  factory calls `RemoveAll<IHostedService>()` so no `BackgroundService` runs under
  `WebApplicationFactory`.
- Enqueue seam (existing `IntegrationTestBase` HTTP fixture + a substituted inspectable
  `IVerificationEventQueue`): a resolved verify call enqueues exactly one event with the right
  `LicenseId` and `ObservedStatus`.
- Enqueue seam: `X-Forwarded-For: 203.0.113.7` → enqueued `ClientIp == "203.0.113.7"`; no header →
  `ClientIp` null.
- Enqueue seam: a missing-key call and an unknown-key call enqueue nothing.
- `VerifyEndpointKeySecrecyTests` still green (response body still exactly `{"valid":…}`).
- Writer seam (isolated: real `VerificationEventQueue` + a `LassieDbContext` on its own dedicated
  Testcontainer connection, no `WebApplicationFactory`, explicit row cleanup): draining N events
  via `DrainOnceAsync` persists N rows with fields intact, and a forced `SaveChangesAsync` failure
  is caught + logged while the loop survives to persist the next batch.
- Retention seam (isolated, own connection): an over-age row is deleted by one sweep; a fresh row
  is kept.
- Enqueue seam: when the substituted queue's `Enqueue` is configured to throw, the verify call
  still returns `200 {"valid":…}` — audit failure never breaks the response.

#### Manual Verification:

- Run the app, hit `/api/license/verify` a few times with `curl -H "X-Api-Key: …"` and varying
  `-H "X-Forwarded-For: …"`; confirm rows appear within a second or two and response time is
  unchanged (`curl -w %{time_total}`).
- Stop the app mid-load (Ctrl+C); confirm buffered rows are flushed on shutdown (row count
  matches requests sent, minus any logged drops).
- Set `Verification:RetentionDays=1`, seed one row dated 2+ days ago and one fresh row, restart;
  confirm the sweep deletes only the aged row and logs the count. Then set it to `0` and confirm
  the sweep is skipped with a Warning and nothing is deleted.

**Implementation Note**: After automated verification passes, pause for manual confirmation before
Phase 3.

---

## Phase 3: Panel history view

### Overview

A new authenticated page showing one license's verification history, server-side paged, linked
from the license list and the edit page.

### Changes Required:

#### 1. History page

**File**: `src/Components/Pages/LicenseVerifications.razor`

**Intent**: Let the admin page through a license's verification events, newest first, without
loading the whole table.

**Contract**: `@page "/licenses/{Id:long}/verifications"`, `@attribute [Authorize]`,
`@layout MainLayout`, `@rendermode InteractiveServer`, `@inject LassieDbContext DbContext`,
`<MudProviders />`. `[Parameter] public long Id { get; set; }`. On parameters-set, load the
license label (`.AsNoTracking()`); show the same "License not found" `MudAlert` + Back button as
`EditLicense.razor:19-23` when it is null. Otherwise render `<MudDataGrid T="LicenseVerificationEvent"
ServerData="LoadAsync">` with columns: Occurred (`OccurredAtUtc`, `yyyy-MM-dd HH:mm:ss 'UTC'`),
IP (`ClientIp` or "—"), Status (`<LicenseStatusBadge Status="context.Item.ObservedStatus" />`),
User-Agent (truncated with a tooltip, or "—"). `LoadAsync(GridState<...>)` →
`DbContext.LicenseVerificationEvents.AsNoTracking().Where(e => e.LicenseId == Id)
.OrderByDescending(e => e.OccurredAtUtc)` then `.Skip(state.Page * state.PageSize).Take(state.PageSize)`
+ a `CountAsync()` for the total. Page size options 25 / 50 / 100.

#### 2. Link from the license list

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: Add a history affordance next to the existing Edit button.

**Contract**: In the actions `TemplateColumn` (`:41-45`), add a second `MudIconButton`
(`Icons.Material.Filled.History`) with `Href="@($"licenses/{context.Item.Id}/verifications")"`.

#### 3. Link from the edit page

**File**: `src/Components/Pages/EditLicense.razor`

**Intent**: Give the admin a way to jump from editing a license to its verification history.

**Contract**: Add a `MudButton`/`MudLink` "Verification history" pointing at
`licenses/{Id}/verifications`, near the existing Cancel/Back controls (`:40`), shown only when
`license is not null`.

### Success Criteria:

#### Automated Verification:

- Build + `dotnet format --verify-no-changes`
- bUnit component test (`src/Lassie.Tests/Components/LicenseVerificationsTests.cs`, following
  `EditLicenseTests.cs` — `BunitContext`, `UseInMemoryDatabase`, `AddMudServices`):
  - Given a license with N seeded events, the grid renders the first page newest-first.
  - An unknown `Id` renders the "not found" alert.
- Full suite green: `dotnet test`

#### Manual Verification:

- From the license list, the history icon opens the new page for that license.
- The grid pages correctly (next/prev, page-size change) against a license with >100 seeded
  events; newest row is first.
- IP, status badge, and User-Agent columns render; empty values show "—".
- The edit page's "Verification history" link lands on the right license.
- Responsive check at narrow width (the grid is wrapped in an `overflow-x` container like
  `PanelHome`).

**Implementation Note**: After automated verification passes, pause for manual confirmation. This
is the last phase — on success, the change is ready for `/10x-impl-review` and `/10x-archive`.

---

## Testing Strategy

### Unit Tests:

- Truncation helper for `UserAgent` / `ForwardedForRaw` (length caps, null passthrough) — plain
  unit test, no DB.
- `ObservedStatus` mapping = `License.Status` for active / expired / deactivated inputs
  (`LicenseStatusTests.cs` already covers `License.Status`; add a case asserting the event copies
  it).

### Tests — three seams

The async `BackgroundService`s cannot run under the shared transactional `WebApplicationFactory`
fixture (see Critical Implementation Details); `LassieWebApplicationFactory` does
`RemoveAll<IHostedService>()`, and the pipeline is verified in three separate seams:

**Enqueue seam** — existing `IntegrationTestBase` + a substituted inspectable `IVerificationEventQueue`
(registered via `WithWebHostBuilder`/a factory subclass):
- Resolved call → exactly one enqueued event; correct `LicenseId` / `ObservedStatus` / `OccurredAtUtc`.
- `X-Forwarded-For` → enqueued `ClientIp`; no header → `ClientIp` null.
- Missing key → nothing enqueued; unknown key → nothing enqueued.
- `VerifyEndpointKeySecrecyTests` unchanged and green (regression guard on the response body).

**Writer seam** — isolated: real `VerificationEventQueue`, a `LassieDbContext` on its own dedicated
connection to the `postgres:17` Testcontainer, no host; teardown deletes seeded rows:
- `DrainOnceAsync` over N events → N rows persisted, fields intact.
- Forced `SaveChangesAsync` failure → caught + logged, loop survives, next batch persists.

**Retention seam** — isolated, own connection:
- Over-age row removed by one sweep; fresh row kept.

### Manual Testing Steps:

1. `curl` the endpoint with a valid key and a spoofed `X-Forwarded-For`; confirm a row within
   ~1s and unchanged `time_total`.
2. Hammer it briefly (e.g. `hey`/`ab` 500 requests); confirm row count ≈ requests, watch logs for
   any drop warnings.
3. Ctrl+C mid-load; confirm shutdown flush.
4. Set `Verification:RetentionDays=1` with a pre-aged row present; restart; confirm only the aged
   row is swept. Set it to `0`; confirm the sweep is skipped with a Warning.
5. In the panel, page through a busy license's history; check ordering, paging, empty-value
   rendering, and the two entry-point links.

## Performance Considerations

- Verify hot path gains only an in-memory `Channel.TryWrite` and a small object allocation — no
  I/O, no lock contention (single-reader bounded channel). The <500ms guardrail is unaffected.
- Writes are batched (`AddRange` + one `SaveChangesAsync` per drain), so sustained polling from
  many deployments collapses into few INSERT round-trips.
- The read page never does an unbounded query — `ServerData` caps every fetch at one page; the
  `(LicenseId, OccurredAtUtc)` index serves the `WHERE` + `ORDER BY` directly.
- Retention uses `ExecuteDeleteAsync` (set-based, no entity materialization), served by the
  `(OccurredAtUtc)` index.

## Migration Notes

- Single additive migration (`AddLicenseVerificationEvents`) — new table only, no change to
  existing tables, no data backfill. `Down` drops the table.
- Applied automatically at startup (`src/Program.cs:79`), consistent with every prior migration.
- No config is required for defaults to work; `Verification:*` keys are optional overrides.

## References

- Research: `context/changes/license-verification-audit-log/research.md`
- Roadmap slice: `context/foundation/roadmap.md` → S-07 (Stream F)
- Verify endpoint origin: `context/archive/2026-08-07-license-creation-and-verification/plan.md`
  (esp. `:101-106` header-not-query-string, `:316-317` no-broad-try/catch)
- Generic audit pattern this deviates from:
  `context/archive/2026-08-04-persistence-layer-foundation/plan.md:44,129-131`
- `[NotAudited]` / secret-leak precedent:
  `context/archive/2026-08-08-license-edit-with-audit-history/reviews/impl-review.md:23-40`
- Per-circuit `DbContext` rule: `context/foundation/lessons.md:35-43`
- Panel patterns: `src/Components/Pages/PanelHome.razor`, `src/Components/Pages/EditLicense.razor`

## Follow-ups (not this change)

- Give the client polling code a descriptive static `User-Agent`
  (`<App>/<version> (<runtime>; <os-arch>)`) so `UserAgent` becomes a real per-caller
  discriminator. Small, separate, needs a client release.
- The detection issue this feeds: distinct-origin / concurrency / impossible-travel analysis over
  these rows.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: Data model + migration

#### Automated

- [x] 1.1 Build passes: `dotnet build src/lassie.csproj` — 434b12f
- [x] 1.2 Migration applies against a clean DB (FixtureSmoke integration test) — 434b12f
- [x] 1.3 `dotnet format --verify-no-changes` — 434b12f
- [x] 1.4 Model snapshot regenerated — no pending-model-changes warning — 434b12f

#### Manual

- [x] 1.5 `LicenseVerificationEvents` table + both indexes present in the DB after startup — 434b12f
- [x] 1.6 `prd.md` reads cleanly with FR-013 and the NFR line — 434b12f

### Phase 2: Capture pipeline

#### Automated

- [x] 2.1 Build, `dotnet format --verify-no-changes`, full `dotnet test` green (test factory does `RemoveAll<IHostedService>()`) — dcc109f
- [x] 2.2 Enqueue seam: resolved call enqueues one event with correct `LicenseId` / `ObservedStatus` — dcc109f
- [x] 2.3 Enqueue seam: `X-Forwarded-For` → `ClientIp`; no header → `ClientIp` null — dcc109f
- [x] 2.4 Enqueue seam: missing key and unknown key enqueue nothing — dcc109f
- [x] 2.5 `VerifyEndpointKeySecrecyTests` still green — dcc109f
- [x] 2.6 Writer seam: draining N events persists N rows; a forced `SaveChangesAsync` failure is logged and the loop survives — dcc109f
- [x] 2.7 Retention seam: over-age row deleted by one sweep, fresh row kept — dcc109f
- [x] 2.8 Enqueue seam: when the substituted queue's `Enqueue` throws, the verify call still returns `200` — dcc109f

#### Manual

- [x] 2.9 `curl` with valid key + spoofed `X-Forwarded-For` → row within ~1s, unchanged `time_total` — dcc109f
- [x] 2.10 Brief load test → row count ≈ requests, drop warnings noted — dcc109f
- [x] 2.11 Ctrl+C mid-load → buffered rows flushed on shutdown — dcc109f
- [x] 2.12 `RetentionDays=1` → sweep deletes only the aged row; `RetentionDays=0` → sweep skipped with a Warning — dcc109f

### Phase 3: Panel history view

#### Automated

- [x] 3.1 Build + `dotnet format --verify-no-changes`
- [x] 3.2 bUnit: grid renders first page newest-first for a license with N seeded events
- [x] 3.3 bUnit: unknown `Id` renders the "not found" alert
- [x] 3.4 Full suite green: `dotnet test`

#### Manual

- [x] 3.5 History icon on the license list opens the new page for that license
- [x] 3.6 Grid paging (next/prev, page-size) works against >100 seeded events, newest first
- [x] 3.7 IP / status badge / User-Agent columns render; empty values show "—"
- [x] 3.8 Edit page "Verification history" link lands on the right license
- [x] 3.9 Responsive check at narrow width
