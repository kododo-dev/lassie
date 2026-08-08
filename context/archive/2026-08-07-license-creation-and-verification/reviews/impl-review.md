<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: License Creation and Verification Implementation Plan

- **Plan**: context/changes/license-creation-and-verification/plan.md
- **Scope**: Phase 3 of 3 (full plan review)
- **Date**: 2026-08-08
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Grounding

All 3 phases' file lists cross-checked against `git diff --name-only b6123a2..HEAD -- src/` — exact match, no unplanned files under `src/`. Automated checks re-run clean at review time: `dotnet build src/lassie.csproj` (0 errors), `dotnet ef migrations has-pending-model-changes` ("No changes have been made to the model since the last migration."). All 17 Progress rows across 3 phases carry commit SHAs and were independently re-verified during implementation against the deployed app (production create/reveal/copy, duplicate-label, path-base, valid/expired/missing/bogus-key verify calls, response time, log absence).

## Findings

### F1 — Inconsistent language in user-facing error messages

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/Components/Pages/CreateLicense.razor:78-79`
- **Detail**: The duplicate-label and generic-save-error messages are in Polish ("Ta etykieta już istnieje.", "Coś poszło nie tak przy zapisie. Spróbuj ponownie.") while every other user-facing string in the app is English (`Login.razor`: "Invalid email or password."). No feature-specific reason for the switch — this was an unplanned choice made during implementation, not called for by the plan.
- **Fix**: Translate both messages to English to match the rest of the panel's established convention, unless the project has since decided to go bilingual/Polish-first (in which case flag the other pages for translation too, in a separate change).
- **Decision**: FIXED — translated to "This label already exists." / "Something went wrong while saving. Please try again."

### F2 — Unbounded change-tracker growth on repeated creates within one circuit

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Performance)
- **Location**: `src/Components/Pages/CreateLicense.razor:68-85`
- **Detail**: On the success path, the newly-created `License` stays tracked (`Unchanged`) in the circuit-scoped `LassieDbContext` — unlike the failure path, which explicitly detaches (`:76`). Since Blazor Server injects one `DbContext` per circuit (long-lived, not per-request), repeatedly using the "Create another license" button in one session grows the change tracker without bound. Low severity for a single-admin panel with infrequent license creation, and it mirrors a pre-existing pattern already present elsewhere (`Login.razor`'s untracked query) rather than being a new regression — but worth a follow-up before write-heavy admin flows are added.
- **Fix**: Detach the entity after a successful save too (mirroring the failure path), or switch to a short-lived scope per operation.
- **Decision**: FIXED — added `DbContext.Entry(license).State = EntityState.Detached;` on the success path, mirroring the existing failure-path detach.

### F3 — Unplanned "Create another license" reset flow

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `src/Components/Pages/CreateLicense.razor:96-104`
- **Detail**: The plan's Phase 2 contract describes create → reveal-once only; the implementation adds a `ResetForm()` method and a "Create another license" button so the admin can start a fresh form without a page reload (necessary because Blazor's router doesn't reliably remount on same-URI navigation — a real UX gap the plan didn't anticipate). Benign, doesn't touch the security-critical constraints (raw key still never navigated/logged), but is scope added during implementation rather than planned.
- **Fix**: No action needed — accept as a reasonable in-flight addition. Worth a one-line note in the plan's Phase 2 section for future readers, but not worth reverting.
- **Decision**: ACCEPTED — kept as-is, no code or plan change.
