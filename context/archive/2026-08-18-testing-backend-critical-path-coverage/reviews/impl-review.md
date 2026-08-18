<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Backend Critical-Path Coverage Implementation Plan

- **Plan**: context/changes/testing-backend-critical-path-coverage/plan.md
- **Scope**: Phase 1-4 of 4 (full plan)
- **Date**: 2026-08-18
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 3 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Context: mid-Phase-1, the user directed the test project to be relocated from the
plan's original `tests/Lassie.Tests/` to `src/Lassie.Tests/`, with a new
`src/lassie.slnx` added. This is documented in commit messages and
`context/foundation/test-plan.md` §6.5, and is not treated as drift. Two
independent sub-agent reviews (plan-drift detection; safety/quality/pattern)
plus a re-run of all automated success criteria back this report.

Success criteria re-verified this pass: `dotnet build src/Lassie.Tests/Lassie.Tests.csproj`
succeeds; `dotnet test src/Lassie.Tests/Lassie.Tests.csproj` — 17/17 passed;
`context/foundation/test-plan.md` §6.1/§6.2 confirmed no longer "TBD". All
manual Progress checkboxes (1.4, 2.2, 3.2, 4.3) have observable evidence in
the conversation/diff, not rubber-stamped.

## Findings

### F1 — IntegrationTestBase.DisposeAsync has no null-guards against partial InitializeAsync failure

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Lassie.Tests/Infrastructure/IntegrationTestBase.cs:17-46
- **Detail**: `InitializeAsync` assigns `_connection`, `Factory`, `HttpClient`, `_transaction`, and `DbContext` sequentially across several fallible `await`s (`OpenAsync`, `Factory.CreateClient()` — which runs `Migrate()` + admin seed, `BeginTransactionAsync`, `UseTransactionAsync`). If any step throws before the last assignment, later fields stay `null!`. `DisposeAsync` unconditionally dereferences `DbContext`, `_transaction`, and `Factory` with no guards — if it still runs after a failed `InitializeAsync`, the first still-null field throws a `NullReferenceException` that masks the real failure and skips disposing whatever *was* acquired (leaked `NpgsqlConnection`/Testcontainers connection).
- **Fix**: Add a null-guard on each field in `DisposeAsync` (`if (DbContext is not null) await DbContext.DisposeAsync();`, same pattern for `_transaction`, `Factory`, `_connection`) so cleanup is safe regardless of how far `InitializeAsync` got.
- **Decision**: FIXED

### F2 — Migrate()+admin-seed commits outside the per-test transaction, leaking state across the run

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Lassie.Tests/Infrastructure/IntegrationTestBase.cs:23-30 (see also src/Program.cs:76-100)
- **Detail**: `Factory.CreateClient()` (line 27) runs `Migrate()` and the admin-user seed *before* the per-test transaction begins (line 29) — intentional, per the code's own comment, to avoid nesting migration transactions inside the test's. But that seed insert is a real, non-rolled-back commit against the collection-shared Postgres container: the first test in a run permanently seeds an admin `User` row that persists for every later test. Harmless today (no test currently asserts `Users` is empty), but it's an undocumented order-dependent side effect — a future test touching `Users` could see a confusing pre-existing row and not know why.
- **Fix**: Add a short caveat to `context/foundation/test-plan.md` §6.2 (or a code comment on `IntegrationTestBase`) noting that the seeded admin `User` persists across all tests in a run and isn't part of any single test's rollback.
- **Decision**: FIXED

### F3 — JetBrains Rider IDE files (`.idea/**`, `*.user`) committed with no `.gitignore` coverage

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: .gitignore; src/.idea/**, src/lassie.sln.DotSettings.user (committed in 30c4cfc)
- **Detail**: During Phase 1's commit, `src/.idea/.idea.lassie/.idea/*` and `src/lassie.sln.DotSettings.user` (IDE-generated in reaction to creating `src/lassie.slnx`) got staged via the "stage all" dirty-path choice. Content is benign (no secrets — encodings/vcs/layout config and a ReSharper test-session GUID cache), but `.gitignore` has no `.idea/` or `*.user` entry, so this machine/session-generated noise is now tracked and will churn on every IDE interaction going forward.
- **Fix**: Add `.idea/` and `*.user` to `.gitignore`, then `git rm -r --cached src/.idea src/lassie.sln.DotSettings.user` to untrack (files stay on disk, just stop being tracked).
- **Decision**: FIXED
