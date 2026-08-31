---
date: 2026-08-31T21:55:00+02:00
researcher: Jacek Łapiński
git_commit: e6cd7f97a4a574de521ca51ed836af0e48307fc6
branch: main
repository: lassie (kododo-dev/lassie)
topic: "license-verification-audit-log — which caller parameters to record per verification call"
tags: [research, codebase, verification-api, audit-log, license-sharing, privacy]
status: complete
last_updated: 2026-08-31
last_updated_by: Jacek Łapiński
---

# Research: license verification audit log — which caller parameters to collect

**Date**: 2026-08-31T21:55:00+02:00
**Researcher**: Jacek Łapiński
**Git Commit**: e6cd7f97a4a574de521ca51ed836af0e48307fc6 (local `main`, not yet pushed — GitHub permalinks omitted)
**Branch**: main
**Repository**: lassie (kododo-dev/lassie)

## Research Question

For the `license-verification-audit-log` feature (roadmap `S-07`): which caller parameters are
worth recording per verification-API call, beyond IP, so that a **later separate feature** can
detect whether one license is being used by more processes than it should be?

**Locked scope (this session, with the user):**
- Collection only — detection is a separate future issue.
- No client-contract change: the client keeps sending only the `X-Api-Key` header.
- Model is **1 license = 1 process** → the license key already identifies the one legitimate
  caller; no installation identifier.
- MAC address ruled out.

## Summary

The server can already see the **true external client IP** on every verify call (the forwarded-
headers middleware is wired and resolves it correctly through Caddy — no code change needed to
*obtain* it, only to *record* it). Beyond IP, the only other useful client-supplied signal is the
**User-Agent** — and only if the client SDK is given a meaningful value to send (a default .NET
`HttpClient` sends none). Everything else worth storing is server-derived: a **UTC timestamp**, the
**verification outcome** (valid / invalid / unknown key / missing key), and — defensively — the
**raw `X-Forwarded-For` chain** in case a CDN is ever put in front. TLS fingerprints, client
certificates and MAC addresses are **not available** (TLS terminates at Caddy; MAC is link-layer).

For "one process vs. several sharing the key", the discriminating power comes from the
**combination** of `{IP, User-Agent, timestamp-cadence}` over a trailing window — not any single
field. IP alone is unreliable (NAT collapses many callers to one; roaming/cloud splits one caller
across many). Storing timestamp and UA now is what lets the later detection issue disambiguate.

Storage must be a **dedicated table written outside the response path** — it cannot reuse the
existing `AuditLog` mechanism (that only fires on entity mutation), and the write must never add
latency to, or be able to fail, the verification response (the <500ms guardrail and the
"service-unavailable ≠ invalid" NFR both bind here).

## Detailed Findings

### 1. What the verification endpoint sees today

`src/Program.cs:143-161` — `app.MapGet("/api/license/verify", async (HttpRequest request, LassieDbContext context) => …)`.

- Injects only `HttpRequest` + `LassieDbContext`. **No `HttpContext`, no `IHttpContextAccessor`**
  (none is registered anywhere in the app).
- Reads exactly one thing: `request.Headers["X-Api-Key"]` (`src/Program.cs:145`).
- `ApiKeyHasher.Hash(apiKey)` (SHA-256 hex, `src/Data/Licenses/ApiKeyHasher.cs:15-16`) →
  `context.Licenses.SingleOrDefaultAsync(l => l.ApiKeyHash == hash)` (indexed unique column).
- Returns `200 {"valid": true|false}` on a known key (`valid = license.Status == LicenseStatus.Active`,
  `src/Data/Licenses/License.cs:22-27`); bare `401` (empty body) on missing/unknown key.
- **No broad `try/catch`** — DB/unexpected failures propagate as `5xx`, deliberately never
  `valid:false` (`src/Program.cs:141-142`; `context/archive/2026-08-07-license-creation-and-verification/plan.md:316-317`).
- No authentication scheme attached → `HttpContext.User` is anonymous for M2M callers; **audit
  cannot key off `HttpContext.User`** (`context/archive/2026-08-07-license-creation-and-verification/plan-brief.md:35`).
- **No HTTP-logging / request-capture middleware exists anywhere** in the pipeline
  (`context/archive/2026-08-18-testing-backend-critical-path-coverage/research.md:69`).

### 2. Caller IP is already resolvable — correctly — with no client change

`src/Program.cs:54-60`:
```
ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
```
- `UseForwardedHeaders` runs **first** in the pipeline (`src/Program.cs:60`), before routing/auth.
- With **both** `KnownProxies` and `KnownIPNetworks` empty, ASP.NET Core's `ForwardedHeadersMiddleware`
  computes `checkKnownIps == false` → it performs **no trust check on the peer** and unconditionally
  consumes the right-most `X-Forwarded-For` entry (up to `ForwardLimit`, which defaults to **1**),
  assigns it to `HttpContext.Connection.RemoteIpAddress`, and stashes the original in `X-Original-For`.
- Deploy topology (`deploy/docker-compose.yml`, `deploy/README.md:43-71`,
  `context/deployment/deploy-plan.md:34`, `context/foundation/infrastructure.md`): external client
  → **single** shared Caddy hop (TLS termination) → `lassie:8080` plain HTTP on the internal Docker
  network. **No Cloudflare / CDN** anywhere. Kestrel publishes **no ports** — only reachable via Caddy.
- Net: inside the handler `HttpContext.Connection.RemoteIpAddress` = **the true external caller IP**
  (the deployment polling the API). Caller-spoofed `X-Forwarded-For` is **not** honoured — Caddy
  appends the real peer to the right, and `ForwardLimit=1` selects that — **as long as Kestrel stays
  unreachable except through Caddy** (currently true).
- `RemotePort` will be `0` (not carried in XFF) — no value.
- Prior confirmation the middleware is trusted app-wide:
  `context/archive/2026-08-07-license-creation-and-verification/research.md:82`
  ("all trust-network checks cleared since Kestrel is only ever reached via Caddy"),
  `context/archive/2026-08-05-admin-auth-foundation/plan.md:45`.
- **Fragility to record:** if a CDN/extra proxy is ever placed in front, the right-most XFF entry
  becomes the *edge* IP and `RemoteIpAddress` silently captures that instead. Mitigation: also store
  the raw forwarded chain (`X-Original-For` / residual `X-Forwarded-For`) verbatim.

### 3. Other request data available without a client change

Reachable via `HttpContext` / `HttpRequest` (needs `HttpContext` or `IHttpContextAccessor` injected
into the handler — currently neither is):

| Signal | Source | Notes |
| --- | --- | --- |
| Client IP | `Connection.RemoteIpAddress` | True caller IP (§2). Primary "where from" signal. |
| Timestamp | server: `DateTimeOffset.UtcNow` | Not a request field. Essential for cadence / concurrency analysis. |
| User-Agent | `request.Headers.UserAgent` | **Only if the client sets it.** .NET `HttpClient` sends none by default. |
| Raw XFF chain | `X-Original-For` / `X-Forwarded-For` residual | Defensive backup for a future CDN hop. |
| Verification outcome | derived in handler | `valid` bool + auth result (known/unknown/missing key). |
| Correlation id | `HttpContext.TraceIdentifier` | Cheap; only useful to line a row up with app logs. |

**Not available (do not plan around these):**
- **TLS / JA3 fingerprint, cipher, TLS version** — TLS terminates at Caddy; the Caddy↔Kestrel hop
  is plain HTTP (`src/Program.cs:49-53`). `ITlsConnectionFeature` / `ITlsHandshakeFeature` are null.
  Would require Caddy to forward custom headers — brittle, out of scope.
- **Client certificate** — same reason (`Connection.ClientCertificate` is null).
- **MAC address** — link-layer only, never crosses a router; would require the client to read its
  own NIC and send it. Randomised on modern OSes, synthetic in containers/VMs, strong PII. **Ruled
  out permanently.**
- **`Host` / `X-Forwarded-Host`** — Caddy always sets these to `kododo.dev`; no discriminating value.
- **`RemotePort`** — `0` after the forwarded-headers rewrite.

Repo-wide grep: **no code anywhere currently reads** `RemoteIpAddress`, `User-Agent`, or
`X-Forwarded-For` (only the `ForwardedHeadersOptions` config block in `src/Program.cs:52-60`).

### 4. Recommended fields to store (ranked)

**Tier 1 — core, always store:**

1. **`LicenseId`** (`long`, FK → `Licenses`) — resolved from the key hash in the handler.
   **Never store the raw key or the hash** (`context/foundation/prd.md:98` — key never in operator-
   readable logs; the `[NotAudited]` mechanism at `src/Data/Auditing/NotAuditedAttribute.cs` +
   `src/Data/LassieDbContext.cs` exists precisely because `ApiKeyHash` once leaked into `AuditLog` —
   `context/archive/2026-08-08-license-edit-with-audit-history/reviews/impl-review.md:23-40`).
2. **`OccurredAtUtc`** (`timestamp with time zone`) — server clock, `DateTimeOffset.UtcNow`.
3. **`ClientIp`** (`text`, nullable) — `RemoteIpAddress?.ToString()`. Nullable: `null` under
   `TestServer` and on any forwarded-headers misconfig.
4. **`Outcome`** (`int` enum) — `Valid` / `Invalid` (key known, licence not active) / `UnknownKey` /
   `MissingKey`. Folds in the roadmap's "validity result + auth outcome"
   (`context/foundation/roadmap.md:181,190`). Lets the admin see probing with bad/revoked keys.

**Tier 2 — cheap, materially improves later disambiguation:**

5. **`UserAgent`** (`text`, nullable, cap ~512 chars) — as sent. **Pair with a client-SDK change to
   send a descriptive static UA** (`<App>/<version> (<runtime>; <os-arch>)`). This is *not* a
   contract change — it is a header the client already controls and currently leaves empty — but it
   hands you app version + OS/arch for free and a stable discriminator between two callers.
6. **`ForwardedForRaw`** (`text`, nullable) — the raw `X-Forwarded-For` / `X-Original-For` string
   Caddy passed, verbatim. Small. Survives a future CDN hop and lets the true IP be re-derived if
   the proxy topology changes.

**Tier 3 — optional, low value:**

7. **`TraceId`** (`text`, nullable) — `HttpContext.TraceIdentifier`, for app-log correlation only.

**Explicitly NOT stored:** raw API key, API-key hash, MAC, TLS fingerprint, remote port, and
geo/ASN (derive geo/ASN *at analysis time* from `ClientIp` in the detection issue — don't bake a
lookup into the write path).

### 5. How the later detection issue would use these (motivation only — NOT built here)

With **1 license = 1 process**, the expected steady state is a single stable `ClientIp` (or a
slowly-changing one) polling on a roughly regular `OccurredAtUtc` interval. Signals the detection
issue can compute purely from the columns above:

- **Distinct `ClientIp` values whose activity windows overlap in time** (concurrent, not merely
  sequential — sequential can be one process whose egress IP changed).
- **Interleaved cadence** — two IPs each polling on their own regular schedule, interleaved → two
  independent schedulers → two processes.
- **`UserAgent` divergence** on the same license within the same window (two different OS/arch).
- **Impossible travel** — two `ClientIp` in a short window geolocating far apart (geo derived then).

**Why IP alone is not enough (write this down for the detection issue's author):** NAT/CGNAT
collapses many processes onto one IP (false negative); dynamic / roaming / cloud egress IPs split
one process across many IPs (false positive). The `{cadence + UA + time-overlap}` combination is
what disambiguates — which is the whole reason timestamp and UA are worth capturing now.

**No seat/limit field exists on `License`** (`src/Data/Licenses/License.cs:12-28` — only `Id`,
`Label`, `ExpiresOn`, `IsActive`, `ApiKeyHash`, computed `Status`; grep for
seat/device/installation/capacity/limit across `src/` finds nothing). With the 1:1 model the
threshold is implicitly 1, so the detection issue does not strictly need a new column. If a license
ever legitimately needs N processes, that is a `License` schema change owned by that later issue.

### 6. Storage & write-path design (for `/10x-plan` — flagged, not decided)

- **Dedicated entity, not `AuditLog`.** `src/Data/LassieDbContext.cs` `AddAuditLogEntries()` is
  hard-gated on `EntityState.Modified or Deleted` (`:55-56`) and builds its snapshot from
  `entry.OriginalValues` of a *mutated tracked entity*. A verification is a pure read — it never
  calls `SaveChanges`, never trips that path. `ChangeType` is a closed 2-value enum with no
  "accessed" member. → new `LicenseVerificationEvent` entity + its own `DbSet` + migration; rows
  written **explicitly in the verify handler**, not via the `SaveChanges` override. (The roadmap
  already presumes a purpose-shaped record — `context/foundation/roadmap.md:181,195` — but left the
  table decision to `/10x-plan`.)
- **Two NFRs bind the write path** (`context/foundation/prd.md:99,102`;
  `context/foundation/roadmap.md:192`):
  - **<500ms guardrail** — current verify is ~110-140ms steady-state
    (`context/archive/2026-08-07-license-creation-and-verification/plan.md:426`). The audit insert
    must be **off the response path**: enqueue to a `Channel<T>` drained by a `BackgroundService`,
    or a best-effort `Task.Run`. Mechanism is a `/10x-plan` decision.
  - **"service-unavailable ≠ invalid"** — an audit-write failure (DB slow/down) must **never**
    turn a valid response into a `5xx` or `valid:false`. The lookup keeps its "no broad try/catch"
    stance; the audit write gets its **own** swallow-and-log guard, isolated from the response.
  - This is a deliberate **inversion** of the S-03 audit pattern, which puts audit rows in the
    *same transaction* as the change for atomicity
    (`context/archive/2026-08-04-persistence-layer-foundation/plan.md:44`). Here atomicity is
    traded away for latency + reliability. Say so explicitly in the plan.
- **Indexing:** `(LicenseId, OccurredAtUtc DESC)` for the per-license history view; consider a
  plain `OccurredAtUtc` index for retention pruning. (`AuditLog` only has `(EntityName, EntityId)`
  and *no* time index — `src/Migrations/20260805062414_AddAuditLogEntityIndex.cs` — don't copy that
  gap.)
- **Volume / retention** (roadmap Unknown, still open — `context/foundation/roadmap.md:189`): every
  deployment polls periodically → the table grows unbounded. PRD `target_scale` is low-QPS / small
  (`context/foundation/prd.md:9-12`), so keep-everything is a viable MVP default, but a rolling
  prune (keep last N days) is cheap insurance and also bounds the privacy exposure. Pruning is a
  background mechanism, **never an admin action** — append-only from the UI's perspective
  (`context/foundation/roadmap.md:194`).
- **Panel history view:** no pagination helper exists — `src/Components/Pages/PanelHome.razor:57`
  loads *all* licenses into a `MudDataGrid` with client-side sort, no paging anywhere. Verification
  rows will dwarf edit-audit rows, so this view needs real server-side paging or a hard cap
  ("last 100 / last 30 days"). Query with `.AsNoTracking()` per the per-circuit `DbContext` lesson
  (`context/foundation/lessons.md:35-43`). There is currently **no UI at all** for the existing
  edit-audit history (`context/archive/2026-08-08-license-edit-with-audit-history/plan.md:59`), so
  this feature builds the first audit-viewing surface in the panel.
- **Conventions** (`src/Data/*`, recent migration `src/Migrations/20260812200540_AddLicenseIsActive.cs`):
  `long Id` identity (`UseIdentityByDefaultColumns`), `text` columns, `timestamptz`, `boolean`;
  Npgsql / `postgres:17`; fluent config inline in `LassieDbContext.OnModelCreating` (no
  `IEntityTypeConfiguration` classes); hand-written `Up`/`Down` migration pair + `.Designer.cs` +
  single model snapshot; `context.Database.Migrate()` at startup (`src/Program.cs:79`).
- **Tests:** `src/Lassie.Tests/Infrastructure/IntegrationTestBase.cs` + `[Collection("Postgres")]`
  + `[Trait("Category","Integration")]`; `src/Lassie.Tests/Licenses/VerifyEndpointKeySecrecyTests.cs`
  is the reference pattern for hitting `/api/license/verify` (seed via `DbContext`, act via
  `HttpClient`, assert response shape). **Under `TestServer` there is no socket** →
  `RemoteIpAddress` is `null` and `UseForwardedHeaders` is a no-op unless the test sets
  `X-Forwarded-For` explicitly on the request. New shared-fixture teardown should follow the
  null-guard / documented-out-of-transaction-commit lesson (`context/foundation/lessons.md:45-53`).

### 7. Privacy / RODO (proportionate)

No `context/**` artifact has ever discussed GDPR/RODO, privacy, or "dane osobowe" (full-text
search: zero hits). Caller IP — and a User-Agent or hostname that identifies a person/host — are
**personal data under RODO even in a B2B context**.

Collection-time decisions this forces (name them at `/10x-plan`, don't silently pick a default):

- **Retention window** — doubles as the privacy control.
- **Raw IP vs. keyed hash at rest** — hashing kills the geo / ASN / impossible-travel utility the
  detection issue needs, so *raw + time-limited* is the likely call, but it is a conscious trade.
- **Processing purpose** — licence compliance / anti-abuse (legitimate interest); one line in a
  processing note.

Keep this light: solo operator, one company, small data volume. A "write it down" item, not a
workstream.

### 8. Non-Goal boundary

Stay a **raw, append-only, per-call trail viewed per license**. Not dashboards, not aggregated
"usage trends", not module-usage analytics — `context/foundation/prd.md:140` ("Zaawansowana
telemetria i analityka wykorzystania licencji przez klientów"), `context/foundation/roadmap.md:188`.
A single derived count on one license's own rows ("distinct IPs in the last N days" on that
license's page) is borderline-acceptable as an audit view; anything cross-license or trend-shaped
crosses the line.

## Code References

- `src/Program.cs:143-161` — the verify endpoint; injects `HttpRequest` + `LassieDbContext` only,
  reads `X-Api-Key`, returns `{valid}`, no broad try/catch.
- `src/Program.cs:54-60` — `UseForwardedHeaders` config; both known-proxy lists cleared → real
  client IP resolves into `RemoteIpAddress`.
- `src/Program.cs:79` — `context.Database.Migrate()` at startup.
- `src/Data/Licenses/License.cs:12-28` — `License` entity; **no seat/limit field**; `ApiKeyHash`
  is `[NotAudited]`.
- `src/Data/Auditing/AuditLog.cs:9-17` — generic audit row shape (mutation-only).
- `src/Data/LassieDbContext.cs` — `AddAuditLogEntries()` gated on `Modified|Deleted`, snapshot from
  `OriginalValues`; `Snapshot` mapped `jsonb`; index `(EntityName, EntityId)` only.
- `src/Data/Licenses/ApiKeyHasher.cs:15-16` — SHA-256 hex; only the hash is ever persisted.
- `src/Migrations/20260805062414_AddAuditLogEntityIndex.cs` — the one AuditLog index; no time index.
- `src/Migrations/20260812200540_AddLicenseIsActive.cs` — recent single-column migration, the
  reference for adding a column.
- `src/Components/Pages/PanelHome.razor:57` — `AsNoTracking()` list load, no pagination (the
  pattern a history view must improve on).
- `src/Lassie.Tests/Licenses/VerifyEndpointKeySecrecyTests.cs` — reference test for the verify
  endpoint; asserts body is exactly `{"valid":…}`.
- `src/Lassie.Tests/Infrastructure/IntegrationTestBase.cs` — per-test transaction-rollback fixture.

## Architecture Insights

- **The verify endpoint is deliberately minimal and deliberately un-instrumented.** Manual header
  validation, no auth scheme, no logging middleware, no rate limiting — all recorded as conscious
  MVP-scale accepted risk (`context/archive/2026-08-07-license-creation-and-verification/plan.md:77`,
  `plan-brief.md:35,52`). S-07 is the first thing to add any per-call observability.
- **Audit in this codebase means "before-image of a mutated row, same transaction".** A per-read
  event log is a different animal and must not be bent onto the `AuditLog` mechanism — the write
  path, the trigger, the transaction semantics, and the query pattern all differ.
- **The <500ms guardrail and "service-unavailable ≠ invalid" NFR are treated as sacred** across
  every prior slice that touched the verify path. Any S-07 design that puts a DB write on the
  response path violates both. Fire-and-forget is not an optimisation here — it is a correctness
  requirement.
- **Secret-leak-into-audit has already bitten this project once** (`ApiKeyHash` into
  `AuditLog.Snapshot`, fixed via `[NotAudited]`). The new table must reference `LicenseId`, never
  the key or hash.

## Historical Context (from prior changes)

- `context/archive/2026-08-07-license-creation-and-verification/` — built the verify endpoint. Key
  priors: raw key must be read from a **header**, never a query string / log line (`plan.md:101-106`);
  production-verified the key is absent from Caddy + app logs (`plan.md:426-427`); no rate limiting,
  no auth scheme, no broad try/catch (`plan.md:77,316-317`); forwarded-headers middleware trusted
  app-wide because Kestrel is only reachable via Caddy (`research.md:82`).
- `context/archive/2026-08-08-license-edit-with-audit-history/` — the append-no-overwrite audit
  pattern. Priors: **no viewer UI** for audit history — retention alone satisfies FR-006
  (`plan.md:59`, `plan-brief.md:31`); `[NotAudited]` added here after `ApiKeyHash` leaked into the
  snapshot (`reviews/impl-review.md:23-40`).
- `context/archive/2026-08-04-persistence-layer-foundation/plan.md:44,121,129-131` — the "one
  generic table, marker interface, audit rows in the same `SaveChanges` transaction" design that
  S-07 must **deviate** from.
- `context/archive/2026-08-18-testing-backend-critical-path-coverage/` — "no logging middleware
  exists at all" (`research.md:69`); `VerifyEndpointKeySecrecyTests` is the sanctioned pattern for
  verify-endpoint integration tests (`plan.md:305-318`). Risk #3 (outage vs. invalid) and Risk #4
  (verify-API IDOR) are **planned but not yet built** (`context/foundation/test-plan.md:48-49,85`).
- FR-009 Socrates note (`context/foundation/prd.md:85`) and `shape-notes.md:45-46,181` — installation
  identifier / heartbeat as the foundation for sharing detection was **explicitly deferred past
  MVP**. This feature does **not** add those (1 license = 1 process), so it is consistent with the
  deferral while still being "the first foundation stone" toward the parked sharing-detection goal
  (`context/foundation/roadmap.md:195,231`).

## Related Research

- `context/archive/2026-08-07-license-creation-and-verification/research.md` — verify endpoint
  internals, forwarded-headers trust model.
- `context/archive/2026-08-18-testing-backend-critical-path-coverage/research.md` — pipeline
  inventory, verify-endpoint test pattern, absence of logging middleware.

## Open Questions

1. **Retention** — keep-everything vs. rolling prune, and the window. (Roadmap Unknown, still open.)
2. **Client User-Agent** — will the client SDK be given a descriptive static UA to send? A yes makes
   `UserAgent` a Tier-1 signal; a no leaves it usually-empty and near-worthless.
3. **`ForwardedForRaw`** — store it defensively, or rely on `RemoteIpAddress` plus a documented
   "no CDN in front" assumption?
4. **Raw IP vs. keyed hash** at rest (privacy vs. detection utility).
5. **Panel history view** — real server-side pagination, or a capped window ("last 100 / 30 days")
   for MVP?
6. **PRD alignment** — add a matching FR to `prd.md` for S-07, or an explicit "extends beyond v1"
   note, before/at `/10x-plan` (`context/foundation/roadmap.md:216`).
7. **Write mechanism** — `Channel<T>` + `BackgroundService` vs. best-effort `Task.Run` for the
   off-response-path insert (deferred to `/10x-plan`).
