# Panel UI Regression Guard — Plan Brief

> Full plan: `context/changes/panel-ui-regression-guard/plan.md`

## What & Why

Closes Phase 3 of `context/foundation/test-plan.md`: protect risk #6 (a pending license deactivate/reactivate confirmation applied to the wrong license after navigation) with a new bUnit component-test layer, and formalize risk #7's already-implemented Playwright regression test (theme toggle silently no-oping across a MudBlazor render-mode boundary) into this change's history and the project's test documentation.

## Starting Point

Risk #6's fix (a guard in `EditLicense.razor`'s `HandleIsActiveChanged`, commit `3f625ac`) already exists in production code but has zero automated coverage — a future refactor could drop it silently. Risk #7's fix and Playwright test already exist too, verified with a deliberate-break check, but from a prior standalone `/10x-e2e` run that predates this change folder — the work is real but uncommitted and unrecorded in `test-plan.md`. No bUnit layer exists anywhere in this repo yet.

## Desired End State

`dotnet test` includes two passing bUnit tests that fail if risk #6's guard is removed. `e2e/theme-toggle.spec.ts` and its supporting infrastructure are committed, plus a new `e2e/seed.spec.ts` filling the one missing E2E quality lever. `test-plan.md`'s Stack and Cookbook sections describe both layers as they now actually exist instead of "none yet."

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| bUnit project location | Existing `Lassie.Tests` project | Matches existing convention, avoids a second `.sln` entry | Plan (user-confirmed) |
| `IDialogService` test double | Hand-written `FakeDialogService` with a `TaskCompletionSource` | Full control over "navigation happens mid-await" timing; no new mocking-library dependency | Plan (user-confirmed) |
| bUnit scope for #6 | Race guard test + one sanity confirm/cancel test | Cheap extra coverage of the dialog flow itself, same shared setup | Plan (user-confirmed) |
| Risk #7 formalization depth | Book existing artifacts + add `seed.spec.ts` | User chose to invest in the seed lever now rather than defer | Plan (user-confirmed) |
| E2E rules lever | Reuse existing root `CLAUDE.md` rules, no new file | A second rules file would duplicate an existing source of truth | Plan (research finding) |
| `test-plan.md` sync timing | Same change, dedicated Phase 3 | Mirrors Phase 1's precedent of updating the cookbook after implementation | Plan (user-confirmed) |
| Manual verification for #6 | None — automated-only | A hand-reproduced async race isn't a reliable signal; `dotnet test` (plus a deliberate-break check) is | Plan (user-confirmed) |
| `LassieDbContext` in bUnit tests | EF Core InMemory provider | Real `ChangeTracker`, not a mock; risk #6's test never touches `SaveChangesAsync`/audit, so the "never mock `LassieDbContext`" rule (which protects risk #5) doesn't apply here | Plan (research finding) |

## Scope

**In scope:**
- bUnit test infrastructure + two tests protecting risk #6
- Committing risk #7's already-done E2E work + a new seed test
- `test-plan.md` documentation sync

**Out of scope:**
- CI wiring for `dotnet test` / `npx playwright test` (test-plan.md Phase 4)
- Re-verifying risk #7's fix itself (already deliberate-break checked)
- A mocking library, a second E2E rules file, manual browser reproduction of the race

## Architecture / Approach

Two independent test-layer deliverables land as separate phases (each with its own commit + manual checkpoint), followed by a documentation-only phase syncing `test-plan.md` to the now-real stack.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. bUnit + race regression | Two bUnit tests protecting risk #6, `FakeDialogService`, EF Core InMemory setup | Getting the async-timing simulation (dialog await + mid-flight `SetParametersAndRender`) right — an incorrect sequence would make the test pass without actually reproducing the race |
| 2. E2E formalization | Commits risk #7's existing artifacts, adds `e2e/seed.spec.ts` | None significant — work is already verified; this is bookkeeping plus one new, low-risk test |
| 3. test-plan.md sync | Stack/Cookbook/Freshness-ledger updates | Drift if written before Phases 1-2's exact package versions/paths are confirmed |

**Prerequisites:** A running app instance (`dotnet run --project src/lassie.csproj`) for Phase 2; local dev Postgres not required for Phase 1 (EF Core InMemory).
**Estimated effort:** ~1 session across 3 phases — Phase 1 is the only one with real new code; Phases 2-3 are largely bookkeeping.

## Open Risks & Assumptions

- Assumes bUnit 2.9.0 (latest stable on NuGet as of 2026-08-19) has no net10.0/Blazor-10 compatibility surprises — not independently verified beyond the package existing; first `dotnet build` after adding it is the real check.
- Assumes `MudSwitch`'s underlying DOM (an `<input type="checkbox">`) is stable enough to drive via bUnit's `Find(...).Change(...)` — if MudBlazor's internal markup differs from expectation, the implementer adjusts the query; this doesn't change the test's intent.

## Success Criteria (Summary)

- A future accidental removal of `EditLicense.razor`'s cross-license guard makes a test fail, not a production incident.
- `e2e/theme-toggle.spec.ts` and its infrastructure are committed and green, protecting risk #7 going forward.
- `test-plan.md` accurately describes the project's real test stack — no more "none yet" placeholders for layers that exist.
