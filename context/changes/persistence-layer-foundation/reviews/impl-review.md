<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Persistence Layer Foundation

- **Plan**: context/changes/persistence-layer-foundation/plan.md
- **Scope**: Phase 1-3 of 3 (full plan)
- **Date**: 2026-08-05
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Missing index on AuditLog lookup columns

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Data/LassieDbContext.cs:14-16 (OnModelCreating); src/Migrations/20260804215449_InitialCreate.cs:15-30
- **Detail**: The generated migration creates only `PK_AuditLogs` on `Id`. There's no index on `(EntityName, EntityId)`, the natural lookup pattern for "show audit history for this license." As `AuditLogs` grows once `License` (S-03) starts writing to it, that query becomes a full table scan.
- **Fix A ⭐ Recommended**: Add `modelBuilder.Entity<AuditLog>().HasIndex(a => new { a.EntityName, a.EntityId })` now and cut a follow-up migration while the table is still empty.
  - Strength: The table currently has zero rows — this is the cheapest possible moment to add an index (no data cost, no downtime risk), and it captures the intent before it's forgotten.
  - Tradeoff: One more migration for infrastructure with no active read path yet.
  - Confidence: HIGH — indexing FK-like lookup columns before read paths land is a standard low-cost defensive pattern.
  - Blind spot: Haven't seen the actual query shape License history will use (could differ from `(EntityName, EntityId)`).
- **Fix B**: Defer — track as a follow-up to add once the first read path against `AuditLogs` is built (alongside S-03).
  - Strength: Avoids speculative indexing before real usage patterns are known; keeps this foundation change minimal.
  - Tradeoff: Risk of being forgotten, shipping a slow query silently.
  - Confidence: MEDIUM — depends on discipline to follow up.
  - Blind spot: No tracking mechanism currently ties this to S-03.
- **Decision**: FIXED (Fix A) — added `HasIndex(a => new { a.EntityName, a.EntityId })` in `LassieDbContext.cs:17-18`, migration `20260805062414_AddAuditLogEntityIndex` generated, build verified.

### F2 — Audit snapshot correctness depends on an unenforced "load before mutate" convention

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/Data/LassieDbContext.cs:33-52 (specifically line 40, `entry.OriginalValues.ToObject()`)
- **Detail**: `entry.OriginalValues` only reflects true pre-change state when EF materialized the entity from a query (or was `Attach`ed with original values explicitly set). If a future write path does an "attach-and-mark-modified" shortcut (e.g. `context.Attach(new License { Id = x, ... }); context.Entry(x).State = EntityState.Modified;`, or a bare `Remove(new License { Id = x })` without loading first) — a common minimal-API CRUD shortcut — `OriginalValues` will equal the new/default values, not the actual prior row. The resulting `AuditLog.Snapshot` would then be misleading rather than a real "before" record, directly undercutting PRD requirement FR-006 (audit history retained on edit) once `License` becomes the first `IAuditable` entity in S-03. Nothing is wrong in the code as written — this is a sharp edge every future `IAuditable` write path must respect, and nothing currently enforces or documents it.
- **Fix**: Add a doc comment on `IAuditable` (or alongside `AddAuditLogEntries` in `LassieDbContext.cs`) stating that entities must be loaded via query — never attach-and-mark-dirty — before being saved as `Modified`/`Deleted`, so `OriginalValues` reflects real prior state. Strong candidate for `/10x-lesson` since it's a recurring rule every future entity implementer needs to know, not a one-off code fix.
- **Decision**: FIXED + ACCEPTED-AS-RULE: "Audit snapshots require load-before-mutate" (context/foundation/lessons.md) — doc comment added to `IAuditable.cs` remarks; build verified.

### F3 — Local dev Postgres port drifted from plan (5433 vs. planned 5432)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: docker-compose.dev.yml:10-11; src/appsettings.Development.json:9
- **Detail**: Plan's Phase 1 contract for `docker-compose.dev.yml` specifies "port 5432 published to localhost." Implementation publishes `5433:5432` instead, and `appsettings.Development.json` is updated to match (`Port=5433`) — internally consistent and functionally fine, likely done to avoid clashing with a pre-existing local Postgres on 5432, but the plan text was never updated to reflect it.
- **Fix**: Update plan.md's Phase 1 item 5 contract text to say port 5433 (one-line addendum) so the plan stays accurate for future comparison.
- **Decision**: FIXED — plan.md Phase 1 item 5 contract updated to state port 5433 with rationale (port-conflict avoidance).

## Observations

### O1 — Composite primary keys not fully captured in AuditLog.EntityId

- **Location**: src/Data/LassieDbContext.cs:41 (`entry.Properties.First(p => p.Metadata.IsPrimaryKey())`)
- **Detail**: Only the first key property is used for `EntityId`. Fine for a single-column PK (License will presumably have one), but a future composite-keyed `IAuditable` entity would silently capture only part of its key. No action needed now — flag for whoever adds the first composite-keyed entity.

### O2 — Concurrent startup migration race under horizontal scaling

- **Location**: src/Program.cs:15-18
- **Detail**: The unguarded `Database.Migrate()` call itself is intentional (confirmed against plan) and not flagged. But if this app is ever deployed with >1 replica starting concurrently, simultaneous `Migrate()` calls aren't coordinated by a distributed lock and could race against `__EFMigrationsHistory`. Low likelihood/impact today (solo single-VPS deployment) — just don't carry this pattern forward unmodified if the topology ever changes.

### O3 — No null-check on connection string before UseNpgsql

- **Location**: src/Program.cs:10-11
- **Detail**: `builder.Configuration.GetConnectionString("DefaultConnection")` can return `null`; `UseNpgsql(null)` fails with a generic `ArgumentNullException` rather than a clear "connection string not configured" error. Low impact since the string is present in both dev config and the production env var today, but `?? throw new InvalidOperationException(...)` would fail faster and clearer if that env var is ever missing at deploy time.

### O4 — Dev Postgres port bound to all interfaces

- **Location**: docker-compose.dev.yml:10-11 (`"5433:5432"`)
- **Detail**: Binds to `0.0.0.0:5433` rather than `127.0.0.1:5433`. Dev-only/throwaway so not a real finding, but on a machine reachable from a shared network this exposes the DB port beyond localhost. Trivial fix if desired: `"127.0.0.1:5433:5432"`.

## Success Criteria Verification

**Automated** (re-run against current HEAD):
- `dotnet build src/lassie.csproj` → PASS (0 warnings, 0 errors)
- `src/Migrations/*_InitialCreate.cs` exists → PASS
- `.github/workflows/deploy.yml` unchanged, no migration step added → PASS (confirmed via diff)

**Manual** (per plan's Progress section, all checked `[x]` with commit SHAs — no rubber-stamping observed; each item ties to a real artifact in the diff):
- Phase 1: local Postgres container starts, app boots without throwing — 5d140a6
- Phase 2: migration applies cleanly, `AuditLogs`/`__EFMigrationsHistory` present locally — db83663
- Phase 3: deploy health check passed, production tables confirmed via `psql`, no migration errors in logs — 216f058

## Scope Discipline

`git diff --name-status 5d140a6^..e8dee38` matches the plan's file list exactly (context docs + `docker-compose.dev.yml` + 6 `src/` files). No unplanned files. "What We're NOT Doing" boundaries respected: no `License`/`Module`/admin-identity entities modeled, no test project stood up, no retry/backoff logic added, no changes to `deploy.yml`, `Added` state correctly excluded from auditing.
