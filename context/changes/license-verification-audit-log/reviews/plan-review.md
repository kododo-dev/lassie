<!-- PLAN-REVIEW-REPORT -->
# Plan Review: License Verification Audit Log

- **Plan**: `context/changes/license-verification-audit-log/plan.md`
- **Mode**: Deep
- **Date**: 2026-08-31
- **Verdict**: REVISE → SOUND after fixes
- **Findings**: 2 critical · 1 warning · 1 observation (all resolved)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | FAIL → addressed (F1, F2, F3 fixed) |
| Plan Completeness | PASS |

## Grounding

9/9 paths ✓ · symbols ✓ (`LicenseStatusBadge.Status : LicenseStatus`; no existing `MudDataGrid`
`ServerData` or `BackgroundService` in `src/` — confirms the "new pattern" claims) ·
`docs/reference/contract-surfaces.md` absent (check skipped) · brief↔plan ✓

## Findings

### F1 — Background writers collide with the transaction-rollback test fixture

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Phase 2 — capture pipeline + Success Criteria
- **Detail**: `LassieWebApplicationFactory` registers `LassieDbContext` on one shared
  `NpgsqlConnection` (`LassieWebApplicationFactory.cs:32`), enlisted in the test transaction
  *per HTTP request via middleware* (`:41-58`). `IntegrationTestBase.CreateClient()` (`:30`) starts
  the host — and both new `BackgroundService`s — before the test transaction exists (`:32`). The
  services resolve `LassieDbContext` on their own scopes (same connection, no enlistment) and run
  DB work on a background thread → `BeginTransaction` failures and
  `NpgsqlOperationInProgressException`, threatening the new Phase 2 tests *and* every existing
  `[Collection("Postgres")]` test. The plan's "seed → act → WaitForIdleAsync → assert via the
  transactional DbContext" flow can't observe the writer; "inject a failure" and `WaitForIdleAsync`
  were undesigned.
- **Fix A ⭐ Recommended**: Seam the tests + disable hosted services in the factory
  - Strength: keeps the shared transactional fixture and the existing suite stable; matches the
    natural producer/consumer seam.
  - Tradeoff: no single end-to-end "endpoint → writer → row" assertion.
  - Confidence: HIGH — fixture mechanics read directly from the three infra files.
  - Blind spot: the isolated writer test needs its own row cleanup (runs outside any rollback).
- **Fix B**: Dedicated non-transactional fixture for capture-pipeline E2E tests + disable hosted
  services in the shared fixture
  - Strength: keeps one true end-to-end test through the real pipeline.
  - Tradeoff: a second integration-test harness; slower; truncation-vs-sweep ordering hazards.
  - Confidence: MED — workable, more moving parts.
  - Blind spot: retention-service vs per-test truncation interaction not fully worked through.
- **Decision**: FIXED via Fix A — added `IVerificationEventQueue` + inspectable fake for the
  enqueue seam; isolated writer/retention seams on a dedicated Testcontainer connection with
  `internal DrainOnceAsync` + `InternalsVisibleTo`; new Phase 2 change #6 adds
  `RemoveAll<IHostedService>()` to `LassieWebApplicationFactory`; Critical Implementation Details,
  Testing Strategy, Success Criteria, Progress, and `plan-brief.md` updated.

### F2 — Happy-path event construction is unguarded — can 5xx a valid license

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 — change #5 + Critical Implementation Details
- **Detail**: The plan said "No try/catch added anywhere in the handler" and only guaranteed the
  *enqueue* can't throw. The new success-path block (read `RemoteIpAddress`, read + trim
  `User-Agent` / `X-Forwarded-For`, read `license.Status`, allocate the event) can throw, which
  would return 500 for a *valid* license — violating the plan's own invariant and the
  "service-unavailable ≠ license-invalid" NFR. The existing no-try/catch rule was scoped to the
  *lookup*.
- **Fix**: Wrap only the build-event-and-enqueue block in a narrow try/catch that logs at Warning
  and swallows — never rethrows. Leave the lookup path untouched.
  - Strength: restores the plan's stated guarantee for one-line cost.
  - Tradeoff: none significant.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED — change #5 contract + Critical Implementation Details "Enqueue placement"
  updated; added Success Criteria / Progress item 2.8 (queue `Enqueue` throws → verify still `200`).

### F3 — Retention window has no lower bound; a config typo wipes the log

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 — change #3; manual test 2.12
- **Detail**: `Verification:RetentionDays` (default 90) → `cutoff = UtcNow - days`. A `0` or
  negative value makes `cutoff` now-or-future and deletes every row. Manual test 2.12 used
  `RetentionDays=0` to "clear the table," normalising a destructive value.
- **Fix**: Clamp effective retention to `>= 1` day (skip the sweep + log a Warning when configured
  `< 1`). Rework test 2.12 to a small positive window with a pre-aged seed row.
  - Strength: removes a silent data-loss footgun.
  - Tradeoff: none.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED — change #3 contract + Success Criteria + Manual Testing Steps + Progress
  2.12 updated.

### F4 — `ForwardedForRaw` column is speculative

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Lean Execution
- **Location**: Phase 1 — change #1
- **Detail**: No CDN / extra proxy hop exists or is planned; the column is added "in case." One
  nullable `text` column; it does hedge a real silent-failure mode (edge IP masquerading as
  client IP).
- **Fix**: Keep it; add a one-line code comment tying it to the "single Caddy hop" assumption in
  Current State Analysis.
- **Decision**: FIXED (kept) — Phase 1 change #1 now instructs the tie-back comment.
