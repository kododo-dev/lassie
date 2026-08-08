<!-- PLAN-REVIEW-REPORT -->
# Plan Review: License Creation and Verification Implementation Plan

- **Plan**: context/changes/license-creation-and-verification/plan.md
- **Mode**: Deep
- **Date**: 2026-08-07
- **Verdict**: REVISE (pre-triage) → SOUND (post-triage, all findings fixed)
- **Findings**: 1 critical, 0 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | FAIL (pre-fix) |
| Plan Completeness | WARNING (pre-fix) |

## Grounding

8/8 paths ✓, 4/4 symbols ✓ (`dotnet ef migrations has-pending-model-changes` ran clean against the
current model; `Npgsql.PostgresException.ConstraintName`/`SqlState` confirmed present in
`Npgsql.dll` 10.0.3; `System.Buffers.Text.Base64Url` and `RandomNumberGenerator.GetBytes` confirmed
present in the .NET 10.0.2 runtime), brief↔plan ✓. Cross-checked the actual reverted
`LicenseFields.razor` implementation (commit `7b5a680`) for the `DbUpdateException`-narrowing and
`[Required]`-validation precedents cited in F2/F3.

## Findings

### F1 — Expiry-date day-boundary semantics never decided

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 1 (`License.ExpiresAtUtc`) / Phase 2 (date input) / Phase 3 (validity check)
- **Detail**: `ExpiresAtUtc` was typed `DateTimeOffset?` with no decided convention for what a
  picked calendar date maps to as a stored instant. If an admin picks "2026-12-31" meaning "valid
  through the end of that day" and the date picker binds directly to midnight UTC, Phase 3's
  `ExpiresAtUtc > now` check makes the license read as expired from 00:00 UTC on Dec 31 — a silent,
  customer-facing one-day-early cutoff for a licensing product.
- **Fix A ⭐ Recommended (chosen)**: Model expiry as `DateOnly? ExpiresOn`, compare with `>=`
  against today's UTC date.
  - Strength: Eliminates the ambiguity at the storage layer, matches the admin's mental model
    exactly, cleanest foundation for S-03/S-04.
  - Tradeoff: `DateOnly` → Postgres `date` is a new column-type precedent in this codebase.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Fix B**: Keep `DateTimeOffset?`, store `pickedDate.AddDays(1)` at midnight UTC, keep `>`.
  - Strength: No new column type.
  - Tradeoff: Silent +1-day transform is a gotcha for the S-03 edit form.
  - Confidence: MEDIUM.
- **Decision**: FIXED (Fix A) — `License.ExpiresAtUtc: DateTimeOffset?` → `License.ExpiresOn: DateOnly?`
  throughout the plan (entity, migration, form input, verification comparison); Phase 3 now uses
  `ExpiresOn is null || ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)`.

### F2 — Missing `using Lassie.Data.Licenses;` in Phase 1's DbContext contract

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1, item 3 (`LassieDbContext` registration)
- **Detail**: The plan's Key Discoveries explicitly reuses the "namespace import gotcha" lesson
  from `module-catalog-management`, but its own Phase 1 contract for wiring up `License` didn't
  mention the matching `using Lassie.Data.Licenses;`.
- **Fix**: Add the `using` to Phase 1 item 3's Contract.
- **Decision**: FIXED — Phase 1 item 3 now explicitly calls out
  `using Lassie.Data.Licenses;` alongside the `DbSet` addition.

### F3 — No mention of basic required-field validation on the label

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2, item 1 (License creation page)
- **Detail**: The reverted `LicenseFields.razor` (commit `7b5a680`) used `[Required]` on its form
  model's `Name`. This plan's Phase 2 contract didn't mention required-field validation on `Label`,
  risking a confusing "duplicate label" error on a second blank submission instead of a clear
  "required" message on the first.
- **Fix**: Add `[Required]` to the form model's `Label` + `<DataAnnotationsValidator/>`, matching
  the reverted `LicenseFields.razor` precedent.
- **Decision**: FIXED — Phase 2 item 1's Contract now specifies `[Required]` +
  `<DataAnnotationsValidator/>`.
