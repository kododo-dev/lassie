---
date: 2026-08-18T20:17:12+02:00
researcher: Jacek Łapiński
git_commit: 40566b1cf44f16bc47ffad7658adb05645c2bb33
branch: main
repository: kododo-dev/lassie
topic: "Ground rollout Phase 1 of test-plan.md: status precedence (Risk #1), API key secrecy (Risk #2), audit load-before-mutate (Risk #5)"
tags: [research, codebase, testing, license-status, api-key-secrecy, audit-trail, test-bootstrap]
status: complete
last_updated: 2026-08-18
last_updated_by: Jacek Łapiński
---

# Research: Ground rollout Phase 1 of test-plan.md

**Date**: 2026-08-18T20:17:12+02:00
**Researcher**: Jacek Łapiński
**Git Commit**: 40566b1cf44f16bc47ffad7658adb05645c2bb33
**Branch**: main
**Repository**: kododo-dev/lassie

## Research Question

Ground rollout Phase 1 of `context/foundation/test-plan.md` (Risks #1, #2, #5) in the current codebase: find the exact code paths, verify or correct the Risk Response Guidance, identify existing tests (expected: none), and assess feasibility of bootstrapping the project's first test project (xUnit).

## Summary

**None of the three risks describe a currently-present bug.** All three code paths already do the right thing:

- **Risk #1** (status precedence) — the precedence logic (`Deactivated > Expired > Active`) is correctly implemented in exactly one place ([`License.cs:22-27`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/Licenses/License.cs#L22-L27)), consumed identically by both the verify API and the panel list — no divergent logic exists. **Correction to the risk's framing**: the original wording ("boundary e.g. expiry exactly 'now'") implied an instant-level comparison. The actual comparison is **date-only** (`DateOnly` vs. `DateOnly.FromDateTime(DateTime.UtcNow)`), so the real edge case is **UTC-calendar-day rollover**, not a sub-day instant. See "Corrections to test-plan.md §2" below.
- **Risk #2** (key secrecy) — the raw key is CSPRNG-generated, only its SHA-256 hash is ever persisted, and no code path (panel, API response, or logging middleware) ever re-exposes the raw value after the reveal-once display. No logging middleware exists at all.
- **Risk #5** (audit load-before-mutate) — the only two License write paths (create = `Added` state, not audited; edit/deactivate = loaded via `SingleOrDefaultAsync` then mutated in place) both already comply with the load-before-mutate rule. No attach-and-mark-dirty shortcut exists anywhere in the repo.

**All three are therefore regression-guard risks, not bug-fixes** — tests should pin current-correct behavior against the PRD/lesson rule (not against "what the code currently does," which is the same thing here but must stay that way as an independent check), so a future change that breaks them gets caught.

**Test-project bootstrap is straightforward but has three concrete blockers to resolve in Phase 1's plan**:
1. `Program.cs` uses top-level statements with **no `public partial class Program {}` marker** — required for `WebApplicationFactory<Program>` and currently absent.
2. App startup **eagerly calls `context.Database.Migrate()`** and throws if `ADMIN_EMAIL`/`ADMIN_PASSWORD` aren't set — any `WebApplicationFactory`-based integration test must handle both.
3. **No test-DB strategy is chosen.** A disposable local dev Postgres already exists (`docker-compose.dev.yml`, `postgres:17`, port 5433, db `lassie_dev`) — the natural candidate for integration tests, given `lessons.md`'s explicit warning that EF Core's InMemory provider/mocked `ChangeTracker` don't reproduce real change-tracking semantics. Testcontainers.PostgreSql is the alternative if per-test isolation from the shared dev DB is wanted. Neither is wired up yet.

No test files, no test project, no test-related package references exist anywhere in the repo — confirmed by all four agents independently.

## Detailed Findings

### Risk #1 — License status precedence

- Single source of truth: [`src/Data/Licenses/License.cs:22-27`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/Licenses/License.cs#L22-L27) — the `Status` computed property:
  ```csharp
  public LicenseStatus Status =>
      !IsActive
          ? LicenseStatus.Deactivated
          : ExpiresOn is null || ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)
              ? LicenseStatus.Active
              : LicenseStatus.Expired;
  ```
- Precedence matches PRD exactly: `!IsActive` short-circuits to `Deactivated` before the expiry check ever runs — Deactivated > Expired > Active, no deviation.
- Expiry comparison is **date-only** (`DateOnly?` column, `date` type in Postgres per [migration `20260807203601_AddLicenses.cs:22`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Migrations/20260807203601_AddLicenses.cs#L22)), compared against `DateOnly.FromDateTime(DateTime.UtcNow)` with `>=`. A license with `ExpiresOn == today (UTC)` is still `Active` for the entire UTC day; it flips to `Expired` only at UTC midnight rollover.
- Both consumers read the same property, no duplication:
  - Verify API: [`src/Program.cs:158`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Program.cs#L158) — `var valid = license.Status == LicenseStatus.Active;`
  - Panel list: [`src/Components/Pages/PanelHome.razor:31-40`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Pages/PanelHome.razor#L31-L40) renders `x.Status` directly; [`LicenseStatusBadge.razor:7-13`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Shared/LicenseStatusBadge.razor#L7-L13) only maps it to a display color, doesn't re-derive it.
- No existing tests anywhere (confirmed via `Glob **/*Test*.cs`, `**/*.Tests*/**` — zero matches; only `src/lassie.csproj` exists as a project file).

### Risk #2 — API key secrecy

- Generation: [`src/Data/Licenses/ApiKeyHasher.cs:9-13`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/Licenses/ApiKeyHasher.cs#L9-L13) — `RandomNumberGenerator.GetBytes(32)` (CSPRNG, 256 bits), base64url-encoded.
- Persistence: only the SHA-256 hash is stored — `License.ApiKeyHash` ([`License.cs:19-20`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/Licenses/License.cs#L19-L20)), marked `[NotAudited]`. No raw-key column exists on the entity. [`CreateLicense.razor:67-79`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Pages/CreateLicense.razor#L67-L79) only saves `hash` to the DB.
- Reveal-once: raw key lives only in `CreateLicense.razor`'s component-local state ([line 96](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Pages/CreateLicense.razor#L96)), masked by default, cleared on `ResetForm()`. Neither `EditLicense.razor` nor `PanelHome.razor` reference the raw key or hash in any rendered output — no detail/view page exists at all.
- No API/DTO ever returns the key: the verify endpoint returns `Results.Ok(new { valid })` only ([`Program.cs:159`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Program.cs#L159)); there is no separate JSON API layer for the panel (Blazor Server queries `DbContext` directly), so no serialization boundary exists that could leak it.
- No logging middleware exists at all — no `UseHttpLogging`, no custom request/response-body logging, no exception-handling middleware. `Program.cs`'s pipeline is `MapOpenApi` (dev) → `UseHttpsRedirection` → `UseStaticFiles` → `UseAuthentication` → `UseAuthorization` → `UseAntiforgery`.

### Risk #5 — Audit load-before-mutate

- `SaveChanges`/`SaveChangesAsync` overrides at [`LassieDbContext.cs:39-49`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/LassieDbContext.cs#L39-L49) call `AddAuditLogEntries()` ([lines 53-85](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/LassieDbContext.cs#L53-L85)) before the base save, which reads `entry.OriginalValues` per property (excluding `[NotAudited]`-marked ones) into a JSON snapshot.
- `IAuditable` ([`src/Data/Auditing/IAuditable.cs`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/Auditing/IAuditable.cs)) is an empty marker interface; `License` is its only implementer.
- `AuditLog` entity ([`src/Data/Auditing/AuditLog.cs`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Data/Auditing/AuditLog.cs)) stores `EntityName`, `EntityId`, `ChangeType`, `ChangedAtUtc`, `Snapshot` (Postgres `jsonb`, indexed on `(EntityName, EntityId)`).
- Every current write path:
  - **Create** — [`CreateLicense.razor:75`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Pages/CreateLicense.razor#L75) — `DbContext.Licenses.Add(license)`, state `Added`, never enters the audit filter (`Modified`/`Deleted` only).
  - **Edit / Deactivate-Reactivate** (same handler for both) — [`EditLicense.razor:86`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Pages/EditLicense.razor#L86) loads via `SingleOrDefaultAsync`; [`HandleSaveAsync`, lines 146-159](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Components/Pages/EditLicense.razor#L146-L159) mutates the same tracked instance and calls `SaveChangesAsync()` — genuine load-before-mutate, and the file has an inline comment (lines 51-54) documenting this as intentional.
- No `Attach(`, `EntityState.Modified`, or `Update(` calls exist anywhere in `src/` outside these reviewed files — confirmed via repo-wide grep.
- No existing test-DB strategy: `docker-compose.dev.yml` (repo root) provisions a disposable `postgres:17` container (`lassie-postgres-dev`, host port 5433, db `lassie_dev`), matched by [`appsettings.Development.json:9`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/appsettings.Development.json#L9). It's a shared, stateful dev instance, not currently wired for per-test isolation.

### Test-project bootstrap feasibility (cross-cutting)

- `src/lassie.csproj`: `net10.0`, `Microsoft.NET.Sdk.Web`, zero test-related package references. It is the only `.csproj` in the repo.
- `Program.cs` composes both surfaces in one app: minimal-API verify endpoint ([`Program.cs:143-161`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Program.cs#L143-L161)) and Blazor Server interactive components ([`Program.cs:20-21`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Program.cs#L20-L21), [115-116](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Program.cs#L115-L116)).
- **No `public partial class Program {}` marker exists** — top-level statements only. `WebApplicationFactory<Program>` needs this marker to bootstrap; it must be added (a small, low-risk addition) as part of Phase 1.
- Startup eagerly calls `context.Database.Migrate()` and requires `ADMIN_EMAIL`/`ADMIN_PASSWORD` env vars or throws ([`Program.cs:76-100`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/src/Program.cs#L76-L100)) — any `WebApplicationFactory`-based test must supply these or override the DbContext registration before host startup.
- No CI test step exists today. [`.github/workflows/deploy.yml`](https://github.com/kododo-dev/lassie/blob/40566b1cf44f16bc47ffad7658adb05645c2bb33/.github/workflows/deploy.yml) has two jobs (`build-and-push`, `deploy`), no `dotnet test` anywhere. Natural insertion point for the test-plan's Phase 4: a new step before the Docker login/build steps in `build-and-push`, using GitHub Actions' `services:` block for a `postgres:17` container if integration tests need one (mirrors `docker-compose.dev.yml`).

## Code References

- `src/Data/Licenses/License.cs:22-27` — `Status` computed property (Risk #1 anchor)
- `src/Program.cs:158` — verify endpoint's status check (Risk #1)
- `src/Components/Pages/PanelHome.razor:31-40`, `src/Components/Shared/LicenseStatusBadge.razor:7-13` — panel display of status (Risk #1)
- `src/Migrations/20260807203601_AddLicenses.cs:22` — `ExpiresOn` column type (`date`, confirms date-only granularity)
- `src/Data/Licenses/ApiKeyHasher.cs:9-16` — key generation + hashing (Risk #2 anchor)
- `src/Data/Licenses/License.cs:19-20` — `ApiKeyHash` field, `[NotAudited]` (Risk #2)
- `src/Components/Pages/CreateLicense.razor:65-97,107-117` — reveal-once flow (Risk #2)
- `src/Data/LassieDbContext.cs:39-85` — `SaveChanges` overrides + `AddAuditLogEntries` (Risk #5 anchor)
- `src/Data/Auditing/IAuditable.cs`, `src/Data/Auditing/AuditLog.cs`, `src/Data/Auditing/NotAuditedAttribute.cs` — audit infrastructure (Risk #5)
- `src/Components/Pages/EditLicense.razor:86,146-159` — load-then-mutate edit/deactivate path (Risk #5)
- `src/lassie.csproj` — no test packages, `net10.0`
- `src/Program.cs` (whole file, esp. lines 17-21, 76-100, 102-116, 143-161) — app composition, no `Program` marker class, eager migrate + admin-seed requirement
- `docker-compose.dev.yml`, `src/appsettings.Development.json:9` — local dev Postgres (candidate test-DB)
- `.github/workflows/deploy.yml` — no test step; insertion point for Phase 4

## Architecture Insights

- The codebase already has a strict, consistently-applied "single source of truth" discipline for status computation and audit correctness — both risks investigated as potential live bugs turned out to be already-correct-by-convention, with the convention even documented inline (`EditLicense.razor:51-54`) and in `lessons.md`. This is unusual for a solo/short-timeline project and suggests the tests for Phase 1 should be framed explicitly as **regression locks on documented conventions**, not bug hunts.
- The single-project structure (webapi + Blazor Server in one host, no separate API/DTO layer for panel-facing data) means there is no natural JSON-serialization boundary to test against for the panel — Blazor Server pages query `DbContext` directly. Only the verify endpoint has a true request/response API contract worth an integration test in the `WebApplicationFactory` sense.
- `[NotAudited]` (property-level attribute) is the existing mechanism for excluding sensitive/noisy fields from audit snapshots — already used correctly for `ApiKeyHash`. Any future auditable entity should follow the same pattern.

## Historical Context (from prior changes)

- `context/foundation/lessons.md` ("Audit snapshots require load-before-mutate") is the direct source of Risk #5 and explicitly names the exact hazard (attach-and-mark-modified corrupting `OriginalValues`) that this research confirms is *not* currently present in the codebase.
- `context/archive/2026-08-08-license-edit-with-audit-history/plan.md` is where the load-before-mutate pattern was originally established for `EditLicense.razor` — consistent with what's in the code today.
- `context/archive/2026-08-07-license-creation-and-verification/plan.md` and `context/archive/2026-08-10-license-list-view/plan.md` cover the original implementation of key generation/hashing and the shared `LicenseStatus`/verify-endpoint refactor referenced in `context/foundation/test-plan.md` §2 Risk #1's source citation — both are corroborated as still-consistent by this research.
- No archived plan mentions a test-DB strategy (Testcontainers/SQLite/dedicated test Postgres) — confirmed absent across all 8 archived `plan.md` files.

## Related Research

- `context/foundation/test-plan.md` — the phased rollout strategy this research grounds (§2 Risk Map, §2 Risk Response Guidance, §3 Phase 1, §4 Stack).

## Corrections to test-plan.md §2 (for backport consideration)

Per `/10x-test-plan`'s post-research backport check — one finding materially affects risk wording, the other two are confirmations that don't require a wording change but are worth the plan-writer's awareness:

1. **Risk #1 wording correction (recommended backport)**: the boundary condition is **UTC-calendar-day rollover** (a `DateOnly` comparison), not an instant-level "expiry exactly now" edge. Suggested reword: *"Verify API or panel computes the wrong license validity at the UTC-day boundary — a license expiring 'today' in the admin's local timezone may flip Expired up to ~24h earlier/later than the admin expects, since `ExpiresOn` is compared as a UTC calendar date."* This also surfaces an **open question** worth flagging to the user: does the admin-facing date picker in `CreateLicense.razor`/`EditLicense.razor` communicate that "expiry date" is interpreted in UTC, or could an admin reasonably expect local-time semantics? Not investigated in this pass — out of scope for Risk #1's original ask (precedence-order correctness), but adjacent enough to flag.
2. **Risk #2 and #5 — no wording correction needed**, but both are confirmed *already protected by current code*, not currently vulnerable. The Risk Response Guidance's framing ("prove protection," "must challenge") is still valid and correctly anticipates future regressions — no change needed to those cells.

## Open Questions

- Test-DB strategy for Risk #5's integration test: reuse the shared `docker-compose.dev.yml` Postgres (simpler, but needs per-test transaction rollback or schema isolation to avoid cross-test pollution) vs. Testcontainers.PostgreSql (per-test ephemeral isolation, one more package dependency). Not resolved by this research — a decision for `/10x-plan`.
- Whether the admin-facing date picker for `ExpiresOn` should clarify UTC semantics (surfaced above) — outside Phase 1's stated risk scope but worth a note for whoever picks this up.
- Exact xUnit / `Microsoft.AspNetCore.Mvc.Testing` package versions for `net10.0` were not verified against live docs (no Context7/Exa MCP available this session, per `test-plan.md` §4) — `/10x-plan` or the implementation step should confirm current package versions before locking `lassie.Tests.csproj`.
