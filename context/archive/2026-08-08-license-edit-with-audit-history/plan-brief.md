# License Edit With Audit History — Plan Brief

> Full plan: `context/changes/license-edit-with-audit-history/plan.md`

## What & Why

Admin needs to edit a license's label and/or expiry date after creating it, without losing the
ability to audit what it used to be. This is roadmap slice `S-03`, implementing PRD `FR-006`
("Administrator can edit a license (name, expiry date), retaining history of prior versions for
audit").

## Starting Point

A generic audit mechanism already exists and works today (`IAuditable` marker interface +
`LassieDbContext.SaveChanges` override that snapshots `OriginalValues` into `AuditLog`), but
`License` doesn't implement it yet and no edit UI exists anywhere in the app — only
`CreateLicense.razor` (create-only) and a placeholder `PanelHome`.

## Desired End State

Visiting `/licenses/{id}/edit` shows the license's current label/expiry in a form. Saving a real
change updates the license and creates exactly one `AuditLog` row with the pre-edit values.
Saving with no actual change touches nothing. A license list to browse into this page (`S-05`) is
explicitly deferred — this slice is reachable by URL only.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Entry point to edit page | URL only (`/licenses/{id}/edit`), no in-panel link | S-05 (license list) will supply the real entry point soon — building a throwaway nav path now is wasted scope. |
| Audit history visibility | DB retention only, no viewer UI | FR-006 requires retention, not a viewer; keeps this slice narrow. |
| Concurrency control | Last-write-wins, no RowVersion | Single flat-admin model per PRD Access Control — no concurrent-edit scenario to guard against. |
| Clearing expiry | Allowed, symmetric with create | `ExpiresOn` is nullable/optional at creation; edit should support the same shape. |
| Label conflict handling | Reuse `CreateLicense.razor`'s `DbUpdateException` (23505) catch pattern | Consistency with the only existing precedent in the codebase. |
| No-op edit | No explicit "nothing changed" detection | EF Core already skips marking an entity Modified (and thus skips the audit row) when no property actually changed. |
| Test coverage | Manual verification, no new test project | Matches F-01/F-02/S-02 convention; introducing test infra is out of scope for this slice. |
| Cancel affordance | Simple link back to `/` | Minimal-cost standard form UX. |

## Scope

**In scope:**
- `License` implements `IAuditable`
- New `/licenses/{id}/edit` page: load-before-mutate, edit label/expiry, label-conflict handling,
  clear-expiry support, cancel link, not-found handling

**Out of scope:**
- License list view / in-panel navigation to the edit page (`S-05`)
- Audit history viewer UI
- Optimistic concurrency
- New test project
- License deactivation/reactivation (`S-04`)

## Architecture / Approach

One new Razor page mirrors `CreateLicense.razor`'s shape. The only genuinely new pattern is
load-before-mutate: the page must query the `License` (not construct-and-attach) and hold that
tracked instance through to `SaveChangesAsync`, so EF's `OriginalValues` — which the existing
generic audit mechanism reads — reflects the true pre-edit row. This exact risk is already called
out in `context/foundation/lessons.md`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. License edit with audit trail | `IAuditable` opt-in + working edit page with a correct audit trail | Getting load-before-mutate wrong would silently corrupt audit snapshots — mitigated by an explicit Critical Implementation Details note in the full plan. |

**Prerequisites:** `S-02` (done) — `License` entity and panel auth already exist.
**Estimated effort:** ~1 session, single phase.

## Open Risks & Assumptions

- Reaching the edit page by URL-only (no nav link) is accepted as fine until `S-05` ships; if that
  slice is delayed, this may feel awkward in practice.
- Last-write-wins is accepted risk under the single-admin MVP model; revisit if the panel ever
  gains multiple concurrent admins.

## Success Criteria (Summary)

- Editing a license's label/expiry persists correctly and is reachable via direct URL.
- Every edit produces exactly one accurate audit-history row capturing the prior state; a no-op
  save produces none.
- Existing conventions (auth, label-uniqueness conflict UX, Blazor Server detach discipline) are
  followed without introducing new patterns beyond what's necessary.
