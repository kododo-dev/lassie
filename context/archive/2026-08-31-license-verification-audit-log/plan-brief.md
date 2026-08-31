# License Verification Audit Log — Plan Brief

> Full plan: `context/changes/license-verification-audit-log/plan.md`
> Research: `context/changes/license-verification-audit-log/research.md`

## What & Why

Record one row per **resolved** call to the license verification API — timestamp, license, caller
IP, User-Agent, observed license status — and let the admin page through that history per license
in the panel. This is the collection half of roadmap slice S-07; detecting that a license is used
by more than the one process it's meant for is a **separate future issue** that will read these
rows.

## Starting Point

`GET /api/license/verify` (`src/Program.cs:143-161`) today reads only the `X-Api-Key` header and
returns `{"valid": <bool>}` — no per-call observability anywhere in the pipeline. The forwarded-
headers middleware is already wired, so the true caller IP is resolvable the moment the handler
takes `HttpContext`. The existing `AuditLog` mechanism is mutation-gated and cannot record reads.
The panel has no pagination primitive and no license detail page — only an edit page.

## Desired End State

Every resolved verify call writes a `LicenseVerificationEvent` row asynchronously, with no change
to response latency (still ~110-140ms, well under the 500ms guardrail) and no way for an audit-
write failure to affect the response. Missing/unknown-key calls write nothing. Rows older than 90
days are pruned by a background sweep. From the panel, `/licenses/{id}/verifications` shows that
license's history, newest first, server-side paged, reachable from the list and the edit page.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Write mechanism | In-process bounded `Channel<T>` + `BackgroundService` batch-writer | Keeps the verify hot path I/O-free and isolates write failures by construction | Plan |
| Failed-auth calls | Not logged — only calls resolving to a license | Non-null FK, simplest schema and UI; probing detection deferred with the rest of detection | Plan |
| Retention | Rolling prune, keep 90 days (configurable) | Bounds table growth and IP-data exposure while keeping a useful trailing window | Plan |
| IP at rest | Raw address, aged out by retention | Preserves geo / ASN / impossible-travel utility the later detection issue needs | Plan |
| History view | New route `/licenses/{id}/verifications`, `MudDataGrid` `ServerData` | Scales to any row count; keeps the edit page focused; matches the existing route shape | Plan |
| Client SDK | Record `User-Agent` verbatim, no client change | Keeps scope on the server; column is ready if a UA appears later | Plan |
| Observed status | Store full `LicenseStatus` (not just a bool) | Free richness, already computed, no analytics/Non-Goal concern | Plan |
| Audit atomicity | Deliberately dropped (unlike S-03's same-transaction audit) | Verify latency + "unavailable ≠ invalid" outrank never-losing-a-row | Research |

## Scope

**In scope:**
- `LicenseVerificationEvent` entity + `DbSet` + indexes + one additive migration
- Queue, background batch-writer, 90-day retention sweep
- Verify handler enqueues on the resolved-license path only
- New paginated panel page + entry-point links from the list and edit page
- FR-013 + a retention/privacy NFR line in `prd.md`

**Out of scope:**
- Any detection / alerting / thresholds / seat limit on `License`
- Logging unauthenticated (missing/unknown-key) calls
- Client-side changes (a UA follow-up is noted)
- Changes to the verify response contract or to `AuditLog` / `AddAuditLogEntries()`
- IP hashing, geolocation, ASN lookup, cold archive of pruned rows

## Architecture / Approach

Verify handler → builds a `LicenseVerificationEvent` from `HttpContext` → `VerificationEventQueue.Enqueue`
(bounded channel, non-blocking, `DropWrite` + counter when full). `VerificationEventWriter :
BackgroundService` drains, batches, and inserts on its own DI scope inside a swallowed try/catch;
flushes on graceful stop. `VerificationEventRetentionService : BackgroundService` bulk-deletes
expired rows on a `PeriodicTimer` via `ExecuteDeleteAsync`. The panel page uses `MudDataGrid`
`ServerData` against the `(LicenseId, OccurredAtUtc)` index with `.AsNoTracking()`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Data model + migration | Entity, DbContext config, migration; PRD FR-013 + NFR line | Getting indexes right up front so Phase 3's paging is index-served |
| 2. Capture pipeline | Queue + batch-writer + retention sweep + handler wiring; three-seam tests | Async `BackgroundService`s can't run under the transactional test fixture — hosted services disabled in the factory, pipeline verified as enqueue / writer / retention seams |
| 3. Panel history view | Paged `/licenses/{id}/verifications` page + two entry-point links; bUnit test | First server-side paging in the panel (`MudDataGrid ServerData` is a new pattern here) |

**Prerequisites:** none — S-02, F-01, F-02, S-05 are all done and archived.
**Estimated effort:** ~2-3 sessions across 3 phases.

## Open Risks & Assumptions

- **Buffered events are lost on a hard crash.** Accepted: this is an audit trail, not a ledger;
  graceful shutdown flushes.
- **No single end-to-end test of the pipeline.** The async `BackgroundService`s are incompatible
  with the shared transactional test fixture, so the enqueue, the writer, and the retention sweep
  are each verified as separate seams with hosted services disabled in the factory (F1 in the
  plan review).
- **`RemoteIpAddress` correctness depends on staying single-hop behind Caddy.** If a CDN is ever
  put in front, `ClientIp` silently captures the edge IP — `ForwardedForRaw` is stored as a
  hedge, and the assumption is documented.
- **`User-Agent` is usually empty today** (bare `HttpClient` sends none), so near-term the row's
  discriminating power is IP + timestamp; the UA follow-up would fix that.
- **S-07 has no FR in PRD v1** — Phase 1 adds FR-013 + an NFR line to close that gap.

## Success Criteria (Summary)

- A resolved verify call produces exactly one row (IP, UA, status, timestamp); a missing/unknown
  key produces none — and verify response latency is visibly unchanged.
- An injected audit-write failure never turns a valid verify call into an error.
- Rows past the retention window are gone after a sweep; fresh rows survive.
- The admin can open a license's verification history from the panel and page through it
  newest-first.
