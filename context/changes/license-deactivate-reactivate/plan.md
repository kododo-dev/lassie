# License Deactivate/Reactivate Implementation Plan

## Overview

Admin gets the ability to deactivate a license and later reactivate it (FR-007), from the license edit page. Deactivation is a persisted, reversible administrative flag — independent of expiry — that immediately makes the license invalid for the verification API once set.

## Current State Analysis

`License` (`src/Data/Licenses/License.cs`) has no persisted status concept at all. `LicenseStatus` is a two-case enum (`Active`/`Expired`) computed purely from `ExpiresOn` vs. today's UTC date, via a get-only, unmapped `Status` property. This computed property is the single source of truth consumed by both the verification endpoint (`src/Program.cs:158`, `license.Status == LicenseStatus.Active`) and the admin panel's `LicenseStatusBadge` component (`src/Components/Shared/LicenseStatusBadge.razor`) — a deliberate refactor from S-05 specifically so the two surfaces can't disagree.

`EditLicense.razor` (`src/Components/Pages/EditLicense.razor`) already establishes the pattern this feature must follow: load the tracked `License` via `SingleOrDefaultAsync` in `OnParametersSetAsync`, mutate that same instance, call `DbContext.SaveChangesAsync()`. `License` implements `IAuditable`, so `LassieDbContext`'s `SaveChanges`/`SaveChangesAsync` overrides (`src/Data/LassieDbContext.cs:39-49`) automatically snapshot the entity's pre-change `OriginalValues` into an `AuditLog` row — this only works correctly because the entity was loaded via query first (see `context/foundation/lessons.md`, "Audit snapshots require load-before-mutate"). No new audit code is needed; a new persisted field on `License` gets audited for free as long as the existing load-then-mutate-then-save shape is preserved.

No `IDialogService`/`MudDialogProvider` is wired up anywhere in the app yet (`MudProviders.razor` only has `MudThemeProvider` + `MudPopoverProvider`) — the Deactivate confirmation dialog is the first consumer.

## Desired End State

- `License` has a persisted `IsActive` bool (default `true`).
- `LicenseStatus` has three cases: `Active`, `Expired`, `Deactivated`. When a license is both deactivated and expired, `Status` reports `Deactivated`.
- `/api/license/verify` returns `valid: false` for a deactivated license, with no code change beyond the `Status` computation itself (it already only checks `== Active`).
- `LicenseStatusBadge` renders a distinct color for `Deactivated`.
- `EditLicense.razor`'s form gains an Active/Inactive switch, bound alongside `Label`/`ExpiresOn` in the same `EditLicenseFormModel`. Flipping it from Active to Inactive immediately opens a confirmation dialog naming the license; canceling reverts the switch to Active, confirming leaves it Inactive (pending — not yet persisted). Flipping it from Inactive back to Active needs no dialog. The pending value is only written to the database when the admin clicks the existing **Save** button, together with any other field edits, in one `SaveChangesAsync()` call and one audit entry — there is no separate Deactivate/Reactivate action divorced from Save.
- Every save that changes `IsActive` produces an `AuditLog` row via the existing generic mechanism (verified manually via a DB query, no new audit code written).

**Verification**: `dotnet build src/lassie.csproj` succeeds; a manual pass through both phases' Manual Verification steps below.

### Key Discoveries:

- `src/Data/Licenses/License.cs:5-9,20-23` — the enum and computed `Status` property to extend.
- `src/Program.cs:158` — verify endpoint already status-agnostic; needs zero changes once `Status` accounts for `IsActive`.
- `src/Components/Shared/LicenseStatusBadge.razor:9-13` — `switch` expression to extend with the `Deactivated` case.
- `src/Components/Pages/EditLicense.razor:70-95,97-126` — the load-then-mutate-then-save shape to replicate for the new action; `errorMessage`/`successMessage` fields are the existing feedback convention to reuse rather than introducing a snackbar.
- `src/Data/LassieDbContext.cs:30-36` (`OnModelCreating`) — where `License` index/property configuration for EF lives, in case the new `IsActive` column needs an explicit default (it does, for the migration's `AddColumn` call, since existing rows must backfill to `true`).
- `src/Components/Shared/MudProviders.razor` — needs `<MudDialogProvider />` added; per `context/foundation/lessons.md` ("MudBlazor providers must live in the same render scope as per-page @rendermode consumers"), this is a per-page-instantiated component, so adding the provider here (not in `MainLayout`) is required for `EditLicense.razor`'s confirmation dialog to resolve popovers correctly.

## What We're NOT Doing

- No quick-action toggle on the license list page (`PanelHome.razor`) — deactivate/reactivate lives only on the edit page.
- No confirmation dialog when switching Inactive → Active — only Active → Inactive.
- No separate Deactivate/Reactivate button distinct from Save — the state lives in the form and persists through the existing Save flow (revised during plan review — see `context/changes/license-deactivate-reactivate/reviews/plan-review.md`, F2).
- No automated test project — matches every prior slice's precedent (none exists yet); verification is `dotnet build` + manual steps below.
- No API-visible distinction between "expired" and "deactivated" in the verify response — `valid` stays a single bool, per FR-010's existing scope decision.
- No bulk deactivate, no scheduled/timed deactivation, no deactivation reason/note field — none of these are in FR-007.

## Implementation Approach

Extend the existing computed-status architecture rather than replacing it: add one persisted bool to `License`, fold it into the existing `Status` switch with `Deactivated` taking precedence over `Expired`, and let every existing consumer (verify endpoint, badge) pick up the new case through that single computed property — exactly the "single source of truth" reason `LicenseStatus` was extracted as a shared type in S-05. The edit page reuses its own already-established load/mutate/save/audit pattern for the new action instead of introducing a parallel code path.

## Critical Implementation Details

**MudDialogProvider is not wired up yet.** `MudProviders.razor` currently renders `MudThemeProvider` + `MudPopoverProvider` only. `IDialogService.ShowMessageBoxAsync(...)` (used for the deactivation confirmation) requires a `<MudDialogProvider />` in scope. Add it to `MudProviders.razor` (Phase 2) — per the existing per-page-render-mode lesson, this makes it available to every page that already renders `<MudProviders />`, not just `EditLicense.razor`.

**The Active/Inactive switch cannot use plain `@bind-Value`.** Turning it off must intercept the change to show a confirmation dialog before the new value is accepted (and revert it on Cancel) — a two-way `@bind-Value` binding would commit the value immediately without a hook to interpose the dialog. Use an explicit `Value`/`ValueChanged` pair on `MudSwitch<bool>` instead, so the `ValueChanged` handler can `await` the dialog before deciding whether to update `Model.IsActive`.

**Migration must backfill existing rows.** The `Licenses` table already has rows in production (license-creation-and-verification, license-edit-with-audit-history, license-list-view were all shipped and used). The new `IsActive` column must be added as `nullable: false` with `defaultValue: true` in the `AddColumn` migration call, so pre-existing licenses come back as active rather than failing the NOT NULL constraint or silently deactivating everything.

## Phase 1: Data model & status precedence

### Overview

Add the persisted `IsActive` flag, extend `LicenseStatus` to three cases with `Deactivated` taking precedence, and ship the migration. This phase alone makes the verify endpoint deactivation-aware — no endpoint code changes required.

### Changes Required:

#### 1. License entity and status computation

**File**: `src/Data/Licenses/License.cs`

**Intent**: Add a persisted, auditable `IsActive` flag and extend `LicenseStatus`/`Status` so deactivation is reflected everywhere `Status` is already consumed, with `Deactivated` taking precedence over `Expired` when both apply.

**Contract**: `LicenseStatus` enum gains a third case, `Deactivated`. `License` gains `public bool IsActive { get; set; } = true;` (no `[NotAudited]` — deactivation changes must appear in the audit trail). `Status` becomes:
```csharp
public LicenseStatus Status =>
    !IsActive
        ? LicenseStatus.Deactivated
        : ExpiresOn is null || ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)
            ? LicenseStatus.Active
            : LicenseStatus.Expired;
```

#### 2. EF Core migration

**File**: `src/Migrations/<timestamp>_AddLicenseIsActive.cs` (generated)

**Intent**: Persist the new column against the already-populated `Licenses` table without breaking existing rows.

**Contract**: `AddColumn<bool>("IsActive", "Licenses", nullable: false, defaultValue: true)` in `Up`; corresponding `DropColumn` in `Down`. Generate via `dotnet ef migrations add AddLicenseIsActive --project src/lassie.csproj`, then apply with `dotnet ef database update --project src/lassie.csproj` (or however the project's existing deploy/dev workflow applies migrations — check `deploy/` for the production path; this plan doesn't change that mechanism).

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `dotnet ef migrations add AddLicenseIsActive --project src/lassie.csproj` generates a migration with no manual edits needed to compile
- `dotnet ef database update --project src/lassie.csproj` applies cleanly against the dev database

#### Manual Verification:

- Existing licenses in the dev DB show `IsActive = true` after the migration applies (spot-check via `psql` or the license list page — nothing changes visually yet since the badge isn't updated until Phase 2)
- Manually flip `IsActive` to `false` for one license directly in the DB, then call `GET /api/license/verify` with that license's API key (see `src/lassie.http`) — confirm the response is `{ "valid": false }`
- Flip it back to `true` and confirm the same call returns `{ "valid": true }` (assuming not expired)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Edit page switch & badge

### Overview

Surface deactivation as an Active/Inactive switch inside `EditLicense.razor`'s existing form, confirmed only when switching to Inactive and persisted only via the existing Save button, plus a visually distinct badge color for `Deactivated`.

### Changes Required:

#### 1. Wire up MudDialogProvider

**File**: `src/Components/Shared/MudProviders.razor`

**Intent**: Make `IDialogService` resolvable from pages rendering `<MudProviders />`, needed for the Deactivate confirmation dialog.

**Contract**: Add `<MudDialogProvider />` alongside the existing `<MudThemeProvider />` and `<MudPopoverProvider />`.

#### 2. Badge color for Deactivated

**File**: `src/Components/Shared/LicenseStatusBadge.razor`

**Intent**: Give `Deactivated` a visually distinct chip color so it doesn't read as "just another Expired" in the list/edit views.

**Contract**: Extend the `ChipColor` switch expression with `LicenseStatus.Deactivated => Color.Error`. The chip's text label already renders `@Status` (the enum's `ToString()`), so no separate label mapping is needed.

#### 3. Active/Inactive switch on the edit form

**File**: `src/Components/Pages/EditLicense.razor`

**Intent**: Let the admin change `IsActive` as an ordinary field of the edit form, alongside `Label`/`ExpiresOn`, so it saves and audits through the exact same `HandleSaveAsync` → `SaveChangesAsync()` path — no separate action, no risk of a pending field edit being silently dropped by an action that bypasses `Model` (see plan-review F2). Switching to Inactive is the one risky direction (it immediately affects a live client app's verification once saved) and gets a confirmation dialog at the moment of the toggle, before the pending value is even accepted into `Model`; switching to Active needs no dialog.

**Contract**:
- `EditLicenseFormModel` gains `public bool IsActive { get; set; }`, initialized from `license.IsActive` in `OnParametersSetAsync` (same place `Label`/`ExpiresOn` are already seeded from the loaded entity).
- Render `<MudSwitch T="bool" Value="Model.IsActive" ValueChanged="HandleIsActiveChanged" Label="@(Model.IsActive ? "Active" : "Inactive")" Color="Color.Success" />` — an explicit `Value`/`ValueChanged` pair, not `@bind-Value`, so the confirmation dialog can run before the new value is accepted.
- `HandleIsActiveChanged(bool newValue)`: if `newValue == false` (switching off), `await`s `DialogService.ShowMessageBoxAsync("Deactivate license?", $"Client apps using \"{license!.Label}\"'s API key will immediately stop verifying as valid once you save.", yesText: "Deactivate", cancelText: "Cancel")`; only sets `Model.IsActive = false` if the result is `true` (leaves it `true` — i.e. the switch visually stays on — on cancel/dismiss). If `newValue == true` (switching on), sets `Model.IsActive = true` directly, no dialog.
- `HandleSaveAsync` gains one more line copying the pending value onto the tracked entity, alongside the existing `license.Label = Model.Label;` / `license.ExpiresOn = Model.ExpiresOn;`: `license.IsActive = Model.IsActive;`. No other change to `HandleSaveAsync`'s shape — the existing `try`/`catch (DbUpdateException ...)` and `successMessage` construction already cover this new field for free.
- Inject `IDialogService` alongside the existing `LassieDbContext` injection.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- On an active license's edit page, the switch shows "Active" and is on; toggling it off opens a confirmation dialog naming the license before the switch visually commits
- Canceling the dialog leaves the switch back on "Active" and doesn't touch the database (no Save happened)
- Confirming the dialog flips the switch to "Inactive" but does **not** yet persist anything — reloading the page (or checking the DB) at this point still shows the license `IsActive = true`, since only Save writes it
- Clicking Save with the switch on "Inactive" persists `IsActive = false` in one save, stays on the page, and the badge (on next list visit or page reload) shows "Deactivated" in its distinct color
- Toggling the switch back to "Active" on a deactivated license requires no dialog; clicking Save persists `IsActive = true` again
- Editing the Label *and* toggling the switch to Inactive in the same visit, then clicking Save once, persists both changes together and produces a single `AuditLog` row capturing both
- Deactivating (via switch + Save) an already-expired license shows the badge as "Deactivated", not "Expired" (precedence check)
- After a deactivate-then-reactivate round trip (two separate Saves), querying `AuditLog` for that `EntityId`/`EntityName = "License"` shows two new `Modified` rows, each with a `Snapshot` reflecting the correct pre-change `IsActive` value
- Re-run the `/api/license/verify` manual check from Phase 1 through the UI (switch + Save), not a direct DB edit: deactivate via UI → verify returns `valid: false`; reactivate via UI → verify returns `valid: true`
- Responsive/legible check on a small viewport (panel-wide NFR) — the new switch doesn't break the edit page layout on mobile width

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

None — no test project exists in this repo yet (consistent with every prior slice); this plan does not introduce one.

### Integration Tests:

None automated — see Manual Testing Steps below, which cover the full deactivate → verify → reactivate → verify round trip.

### Manual Testing Steps:

1. On an active, non-expired license's edit page, toggle the switch off; confirm the dialog text names the correct license, and that canceling leaves the switch on "Active" with the DB untouched.
2. Toggle off and confirm the dialog, then reload the page *before* clicking Save — confirm the license is still `IsActive = true` in the DB (the pending toggle wasn't persisted).
3. Toggle off, confirm the dialog, then click Save; confirm `/api/license/verify` returns `valid: false` immediately after.
4. Toggle back on (no dialog) and click Save; confirm `/api/license/verify` returns `valid: true` again.
5. Edit the Label and toggle the switch off (confirming the dialog) in the same visit, then click Save once; confirm both the new label and `IsActive = false` persisted, and exactly one new `AuditLog` row was written.
6. Toggle off a license whose `ExpiresOn` is already in the past, then Save; confirm the badge shows "Deactivated" (not "Expired").
7. Toggle that same expired-and-deactivated license back on and Save; confirm the badge now shows "Expired" (deactivation cleared, expiry still in effect).
8. Inspect `AuditLog` rows for a deactivate-then-reactivate round trip (two separate Saves) to confirm both transitions were captured with correct before/after `IsActive` snapshots.
9. Resize the browser to a phone-width viewport on the edit page and confirm the new switch doesn't overflow or hide.

## Performance Considerations

None — this is a single boolean column read/write on an already-indexed-by-PK row; no impact on the verify endpoint's <500ms guardrail.

## Migration Notes

The `AddColumn` migration must specify `defaultValue: true` so existing production licenses (already live at `kododo.dev/lassie`) come back as active rather than violating the new `NOT NULL` constraint or defaulting to deactivated. No data backfill script needed beyond the column default itself.

## References

- Prior audit-history mechanism: `context/archive/2026-08-08-license-edit-with-audit-history/plan.md`
- Status computation refactor origin: `context/archive/2026-08-10-license-list-view/plan.md` (explicitly anticipated a future `Deactivated` case)
- Lesson: `context/foundation/lessons.md` — "Audit snapshots require load-before-mutate", "MudBlazor providers must live in the same render scope as per-page @rendermode consumers"

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Data model & status precedence

#### Automated

- [x] 1.1 `dotnet build src/lassie.csproj` succeeds — c83e830
- [x] 1.2 `dotnet ef migrations add AddLicenseIsActive --project src/lassie.csproj` generates a migration with no manual edits needed to compile — c83e830
- [x] 1.3 `dotnet ef database update --project src/lassie.csproj` applies cleanly against the dev database — c83e830

#### Manual

- [x] 1.4 Existing licenses show `IsActive = true` after migration — c83e830
- [x] 1.5 Manually flipping `IsActive` to false makes `/api/license/verify` return `valid: false` — c83e830
- [x] 1.6 Flipping it back to true restores `valid: true` — c83e830

### Phase 2: Edit page switch & badge

#### Automated

- [x] 2.1 `dotnet build src/lassie.csproj` succeeds

#### Manual

- [x] 2.2 Toggling the switch off opens a confirmation dialog naming the license before committing
- [x] 2.3 Canceling the dialog leaves the switch on "Active" and the DB untouched
- [x] 2.4 Confirming the dialog flips the switch to "Inactive" without persisting until Save
- [x] 2.5 Clicking Save with the switch "Inactive" persists `IsActive = false` in one save; badge shows "Deactivated"
- [x] 2.6 Toggling back to "Active" needs no dialog; Save persists `IsActive = true` again
- [x] 2.7 Editing Label + toggling the switch in one visit, then one Save, persists both and produces a single AuditLog row
- [x] 2.8 Deactivated + expired license shows "Deactivated" badge (precedence)
- [x] 2.9 AuditLog shows correct before/after snapshots for a deactivate-then-reactivate round trip (two Saves)
- [x] 2.10 End-to-end UI-driven deactivate/reactivate round trip confirmed via `/api/license/verify`
- [x] 2.11 Edit page with new switch remains usable at phone-width viewport
