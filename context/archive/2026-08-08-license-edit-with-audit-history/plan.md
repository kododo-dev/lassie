# License Edit With Audit History Implementation Plan

## Overview

Admin can edit an existing license's label and/or expiry date from the panel, and every prior
version is retained for audit via the already-existing generic `AuditLog` mechanism. This is
roadmap slice `S-03` (`context/foundation/roadmap.md`), implementing PRD `FR-006`.

## Current State Analysis

- Generic audit infrastructure already exists and is wired into `SaveChanges`/`SaveChangesAsync`
  (`src/Data/LassieDbContext.cs:38-71`): any entity implementing the marker interface
  `IAuditable` (`src/Data/Auditing/IAuditable.cs`) gets a row written to `AuditLog`
  (`src/Data/Auditing/AuditLog.cs`) whenever it's tracked as `Modified` or `Deleted`, capturing
  EF Core's `OriginalValues` as a JSON snapshot. No schema change is needed to opt an entity in —
  it's a pure C# interface addition.
- `License` (`src/Data/Licenses/License.cs`) does **not** yet implement `IAuditable`. It currently
  has `Id`, `Label`, `ExpiresOn`, `ApiKeyHash` — no status/active flag (that's a separate roadmap
  slice, `S-04`).
- No edit path exists yet for any entity in this codebase — `CreateLicense.razor`
  (`src/Components/Pages/CreateLicense.razor`) only ever `Add`s a new `License`. There is no
  precedent in-repo for the load-then-mutate flow this plan needs.
- `context/foundation/lessons.md` ("Audit snapshots require load-before-mutate") already flags the
  exact risk this plan must avoid: `OriginalValues` only reflects the true pre-change row when the
  entity was loaded via a query first. An attach-and-mark-modified shortcut would silently corrupt
  the audit snapshot.
- No license list view exists yet (`S-05` is `proposed`, not built) — there is currently no
  in-panel path to a specific license's edit page.
- No test project exists in the repo (per `CLAUDE.md`); F-01/F-02/S-02 were all verified manually.

### Key Discoveries:

- `src/Components/Pages/CreateLicense.razor:70-81` — the established pattern for surfacing the
  `Licenses.Label` unique-index conflict: catch `DbUpdateException` where
  `ex.InnerException is Npgsql.PostgresException { SqlState: "23505" }`, inspect
  `pgEx.ConstraintName`, detach the entity, show a generic message.
- `src/Components/Pages/CreateLicense.razor:85` — Blazor Server holds one long-lived `DbContext`
  per circuit, so entities must be explicitly `Detach`ed after use to avoid unbounded change
  tracker growth.
- `src/Components/Pages/PanelHome.razor:1-4` and `CreateLicense.razor:1-4` — the `[Authorize]` +
  `@layout MainLayout` + `@rendermode InteractiveServer` header is the standard shape for every
  panel page.

## Desired End State

Navigating to `/licenses/{id}/edit` while authenticated shows a form pre-filled with that
license's current label and expiry date. Submitting a change updates the `License` row and writes
exactly one new `AuditLog` row capturing the pre-edit values. Submitting with no actual changes
updates nothing and writes no audit row. Verification:

- `dotnet build src/lassie.csproj` succeeds.
- Manual walkthrough (Testing Strategy below) confirms the audit row's `Snapshot` JSON contains
  the license's values from *before* the edit, not after.

## What We're NOT Doing

- No in-panel link/navigation to reach the edit page — it's reachable by URL only in this slice.
  `S-05` (license list view) will supply the real entry point later.
- No UI to view a license's audit history — retention in `AuditLog` satisfies `FR-006`; a viewer
  is left for a future slice if the user asks for one.
- No optimistic-concurrency/RowVersion protection — last-write-wins is acceptable per the single
  flat-admin model in the PRD's Access Control section.
- No new test project — this slice stays consistent with F-01/F-02/S-02's manual-verification
  convention.
- No changes to license status/deactivation (`S-04`) or to the verification API.

## Implementation Approach

Add `IAuditable` to `License` (no migration needed — it's a marker interface, not a schema
change), then build a single new Razor page mirroring `CreateLicense.razor`'s shape but for
edit: load the license by route id via a query (never attach-and-mark-modified, per
`lessons.md`), bind an `EditForm` to its current values, and let the existing generic
`SaveChanges` override record the audit snapshot automatically on submit.

## Critical Implementation Details

### State sequencing: load-before-mutate

The edit page's `OnInitializedAsync` (or parameter-set handler) must fetch the `License` via
`DbContext.Licenses.SingleOrDefaultAsync(l => l.Id == Id)` and keep that *same tracked instance*
around for the circuit's lifetime, mutating its properties directly from the form's `OnValidSubmit`
handler before calling `SaveChangesAsync()`. Do not re-query or construct a new `License` and
`Attach`/`Update` it at submit time — that would make `OriginalValues` reflect the already-mutated
state instead of the true pre-edit row, silently corrupting the `AuditLog` snapshot (see
`context/foundation/lessons.md`).

## Phase 1: License edit with audit trail

### Overview

License opts into audit tracking, and a new panel page lets the admin edit an existing license's
label and expiry, exercising that audit path end-to-end.

### Changes Required:

#### 1. License entity opts into audit tracking

**File**: `src/Data/Licenses/License.cs`

**Intent**: Mark `License` as audited so edits/deactivations are automatically snapshotted by the
existing generic mechanism — no other change needed on the data-model side.

**Contract**: `License` implements `Lassie.Data.Auditing.IAuditable`.

#### 2. License edit page

**File**: `src/Components/Pages/EditLicense.razor` (new)

**Intent**: Let an authenticated admin change an existing license's label and/or expiry date, with
the edit going through the load-before-mutate path so the audit snapshot is correct, and with the
same label-uniqueness conflict handling `CreateLicense.razor` already established.

**Contract**:
- Route `/licenses/{Id:long}/edit`, `[Authorize]`, `@layout MainLayout`,
  `@rendermode InteractiveServer` — same shape as `CreateLicense.razor`.
- On load: query the `License` by `Id` (`SingleOrDefaultAsync`, not `Find`-then-detach — must stay
  the tracked instance used for the later mutate+save). If not found, render a "License not found"
  message instead of the form (no exception, no redirect loop).
- Form fields: `Label` (required, matches `CreateLicense`'s validation) and `ExpiresOn`
  (`DateOnly?`, clearable back to `null`).
- On valid submit: assign the form values directly onto the already-loaded tracked `License`
  instance, then `SaveChangesAsync()`. If EF finds no property actually changed, the entity never
  becomes `Modified` and no audit row is written — this is the desired no-op behavior, and needs
  no explicit "nothing changed" detection code.
- Label conflict: catch `DbUpdateException` with `SqlState: "23505"` on `IX_Licenses_Label`
  exactly as `CreateLicense.razor:74-80` does; show the same generic conflict message; do not
  leave the entity in a corrupted tracked state (reload or reset properties from DB values so a
  retry is possible).
- A "Cancel" link back to `/` (`PanelHome`) that performs no save.
- After a successful save, show a confirmation (updated label/expiry) — no redirect required.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification:

- Create a license via `/licenses/new`, then navigate directly to `/licenses/{id}/edit` — form is
  pre-filled with the current label and (if set) expiry date.
- Change the label and/or expiry, submit — the `License` row reflects the new values, and exactly
  one new `AuditLog` row appears with `EntityName = "License"`, `EntityId` matching, `ChangeType =
  Modified`, and `Snapshot` containing the *pre-edit* label/expiry/API-key-hash values.
- Clear an existing expiry date back to empty, submit — `ExpiresOn` becomes `null` in the DB.
- Edit a license's label to match another existing license's label — submit shows the generic
  conflict message, no partial save, no unhandled exception; a retry with a unique label then
  succeeds.
- Submit the form with no actual changes — save completes without error and the `AuditLog` row
  count for that license is unchanged (verify via `psql`/`SELECT count(*)`).
- Navigate to `/licenses/999999/edit` (non-existent id) — a "not found" message renders, no
  exception.
- Click "Cancel" — navigates back to `/` without touching the DB.
- Visit `/licenses/{id}/edit` while logged out — redirected to `/login` (same as other
  `[Authorize]` panel pages).

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding further.

---

## Testing Strategy

### Manual Testing Steps:

1. Log in, create a license, edit its label — confirm DB row + audit snapshot as described above.
2. Edit the same license's expiry (set, then clear) — confirm both directions work.
3. Trigger the label-uniqueness conflict and confirm recovery on retry.
4. Confirm the no-op-submit and not-found-id cases.
5. Confirm the Cancel link and the logged-out redirect.

## Performance Considerations

None beyond what already applies to `CreateLicense.razor` — single-row read/write, no additional
queries introduced.

## Migration Notes

None — `IAuditable` is a marker interface with no schema impact; no EF Core migration is generated
or required for this phase.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `S-03`
- PRD requirement: `context/foundation/prd.md` → `FR-006`
- Lesson driving the critical implementation detail: `context/foundation/lessons.md` →
  "Audit snapshots require load-before-mutate"
- Pattern followed: `src/Components/Pages/CreateLicense.razor`
- Generic audit mechanism: `src/Data/LassieDbContext.cs:38-71`, `src/Data/Auditing/AuditLog.cs`,
  `src/Data/Auditing/IAuditable.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: License edit with audit trail

#### Automated

- [x] 1.1 Build succeeds: `dotnet build src/lassie.csproj` — 4cd1c12

#### Manual

- [x] 1.2 Edit page pre-fills current label/expiry from an existing license — 4cd1c12
- [x] 1.3 Successful edit updates the License row and writes exactly one correct AuditLog row — 4cd1c12
- [x] 1.4 Clearing expiry back to null works — 4cd1c12
- [x] 1.5 Label-uniqueness conflict shows generic message, retry succeeds — 4cd1c12
- [x] 1.6 No-op submit writes no new AuditLog row — 4cd1c12
- [x] 1.7 Non-existent id renders "not found", no exception — 4cd1c12
- [x] 1.8 Cancel link returns without saving — 4cd1c12
- [x] 1.9 Logged-out access redirects to /login — 4cd1c12
