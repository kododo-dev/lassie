<!-- PLAN-REVIEW-REPORT -->
# Plan Review: License Deactivate/Reactivate Implementation Plan

- **Plan**: context/changes/license-deactivate-reactivate/plan.md
- **Mode**: Deep
- **Date**: 2026-08-12
- **Verdict**: REVISE (fixed to SOUND after triage — see Decisions below)
- **Findings**: 1 critical, 2 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | FAIL |

## Grounding

5/5 paths ✓ (`src/Data/Licenses/License.cs`, `src/Components/Shared/LicenseStatusBadge.razor`, `src/Components/Pages/EditLicense.razor`, `src/Components/Shared/MudProviders.razor`, `src/Data/LassieDbContext.cs`), 3/3 symbols ✓ (`IAuditable`, `NotAuditedAttribute`, `LicenseStatus`), brief↔plan ✓

## Findings

### F1 — Wrong MudBlazor dialog API name

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2, Change #3 (EditLicense.razor Contract)
- **Detail**: Plan's Contract called `IDialogService.ShowMessageBox(...)`. Checked against the installed package (`~/.nuget/packages/mudblazor/9.8.0/lib/net10.0/MudBlazor.xml:34253`) — MudBlazor 9.8.0's `IDialogService` only exposes async `ShowMessageBoxAsync(...)` returning `Task<bool?>`. As written, this would not compile.
- **Fix**: Renamed to `ShowMessageBoxAsync`, awaited, branching on `result == true`.
- **Decision**: FIXED (applied directly to plan.md)

### F2 — Deactivate/Reactivate silently discards unsaved Label/ExpiresOn edits

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2, Change #3 (EditLicense.razor Contract)
- **Detail**: The original Deactivate/Reactivate button design mutated `license.IsActive` directly and saved immediately, without ever copying `Model.Label`/`Model.ExpiresOn` onto the tracked entity. An admin who edited the Label then clicked Deactivate (instead of Save) would silently lose that edit, with no indication anything was dropped.
- **Fix A**: Bundle pending form edits into the same save (copy `Model` → `license` before setting `IsActive`).
- **Fix B**: Disable Deactivate/Reactivate while the form is dirty.
- **Decision**: FIXED — applied differently. User chose a third approach: replace the separate Deactivate/Reactivate buttons with an Active/Inactive switch inside the existing form (`EditLicenseFormModel.IsActive`), persisted only via the existing Save button alongside Label/ExpiresOn — eliminating the failure mode structurally rather than patching around it. Confirmation dialog now fires at the moment the switch is toggled to Inactive (before the pending value enters `Model`), not at Save time; switching to Active needs no dialog. Plan and plan-brief rewritten accordingly (Phase 2 change #3, Desired End State, What We're NOT Doing, Success Criteria, Progress, Testing Strategy, Key Decisions table).

### F3 — Migration Notes omit the app's auto-migrate-on-startup behavior

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Migration Notes / Phase 1 Automated Verification
- **Detail**: `Program.cs:79` calls `context.Database.Migrate()` unconditionally on every app startup, dev and production alike. The plan's Migration Notes doesn't mention that the new column will auto-apply against live production data on the next deploy with no manual step — the more directly relevant fact behind why `defaultValue: true` matters.
- **Fix**: Add a sentence to Migration Notes clarifying the auto-migrate-on-startup behavior and that `dotnet ef database update` is a local pre-push check, not the production apply mechanism.
- **Decision**: SKIPPED — user chose not to address now.
