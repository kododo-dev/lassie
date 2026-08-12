# License Deactivate/Reactivate — Plan Brief

> Full plan: `context/changes/license-deactivate-reactivate/plan.md`
> Plan review: `context/changes/license-deactivate-reactivate/reviews/plan-review.md`

## What & Why

Admin needs to be able to deactivate a license and later reactivate it (PRD FR-007) — a reversible administrative state, not a permanent kill. This is the last unblocked, un-shipped slice on the roadmap (S-04); every prerequisite (F-01, F-02, S-02) is already `done`.

## Starting Point

`License` has no persisted status today — `LicenseStatus` (`Active`/`Expired`) is computed purely from `ExpiresOn` vs. today's date, and that single computed property already feeds both the verify API and the admin panel's status badge (a deliberate S-05 refactor). The edit page (`EditLicense.razor`) has only Save/Cancel; no deactivation UI, button, or backing field exists yet anywhere in the codebase.

## Desired End State

From a license's edit page, the admin sees an Active/Inactive switch alongside Label and Expiry date. Switching to Inactive asks for confirmation first (it immediately cuts off client-app verification once saved); switching to Active needs no confirmation. Either way, the change only takes effect when the admin clicks the existing Save button — same as any other field edit — and shows up in the audit log like any other license edit.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Data model | Persisted `bool IsActive` (default `true`) | User chose the simple boolean over a `DeactivatedAtUtc` timestamp | Plan |
| Status precedence | `Deactivated` beats `Expired` when both apply | Reflects the deliberate admin action rather than hiding it behind a date-based fact | Plan |
| UI placement | Edit page only, no list-row quick action | Matches the existing single "editing surface" pattern; avoids duplicating the mutate/audit code path | Plan |
| UI shape | Active/Inactive switch in the form, not separate Deactivate/Reactivate buttons | Plan-review found the original button design would silently discard any unsaved Label/ExpiresOn edit when clicked; folding `IsActive` into the same `Model`/Save path removes that failure mode entirely | Plan review (F2) |
| Confirmation | Dialog only when switching to Inactive, fired at the moment of toggling (before Save) | Risk asymmetry — deactivating cuts off a live client app immediately once saved, reactivating is purely restorative; confirming at toggle-time (not Save-time) means the risky action is confirmed right where it's taken | Plan review (F2) |
| Persistence timing | Toggling the switch only changes pending form state; nothing writes to the DB until Save | Matches how every other field on this form already behaves — no special-cased "acts immediately" path to reason about | Plan review (F2) |
| Automated tests | None — build + manual only | Consistent with every prior slice; repo has no test project yet | Plan |

## Scope

**In scope:**
- Persisted `IsActive` bool + migration (with `defaultValue: true` backfill for existing production licenses)
- Three-case `LicenseStatus` (`Active`/`Expired`/`Deactivated`) with `Deactivated` precedence
- Verify API automatically deactivation-aware (no endpoint code change needed)
- `LicenseStatusBadge` gets a `Deactivated` color
- Edit-page Active/Inactive switch, bound into the existing form model, with a confirmation dialog when switching to Inactive
- Wiring up `MudDialogProvider` (first consumer of `IDialogService` in this app)

**Out of scope:**
- List-page quick-action toggle
- Confirmation when switching to Active
- A separate Deactivate/Reactivate action distinct from Save
- Automated test project
- API response distinguishing "expired" from "deactivated" (stays a single `valid` bool, per FR-010)
- Bulk deactivate, scheduled deactivation, deactivation reason/note field

## Architecture / Approach

Extends the existing computed-status architecture instead of replacing it: one new persisted bool folds into the `Status` property that every consumer (verify endpoint, badge) already reads — the same "single source of truth" design S-05 established. On the edit page, `IsActive` is just one more field in the existing form model, saved through the page's own already-proven load-tracked-instance → mutate → `SaveChangesAsync()` shape, so the existing generic `IAuditable`/`AuditLog` mechanism captures the change with zero new audit code. The only new interaction is a confirmation dialog interposed at the moment the switch is toggled to Inactive, before the pending value even enters the form model.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Data model & status precedence | `IsActive` column + migration, 3-case `LicenseStatus`, verify API deactivation-aware | Migration must backfill existing production rows to `true`, not break the NOT NULL constraint |
| 2. Edit page switch & badge | Active/Inactive switch in the edit form + confirm dialog on toggle-to-Inactive + badge color, `MudDialogProvider` wired up | First use of `IDialogService` in this app — provider must be added to the per-page `MudProviders.razor`, not `MainLayout`, per the existing render-scope lesson |

**Prerequisites:** None beyond what's already `done` (F-01, F-02, S-02).
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- The app auto-applies pending migrations on every startup (`context.Database.Migrate()`, `Program.cs:79`) in both dev and production — the `IsActive` migration ships automatically on next deploy, not via a separate manual step.
- Assumes `IDialogService.ShowMessageBoxAsync` (MudBlazor 9.8.0 — confirmed against the installed package docs) is sufficient for the confirmation UX without a custom dialog component.

## Success Criteria (Summary)

- Admin can switch an active license to Inactive from its edit page (with confirmation at toggle-time) and, after Save, see the change reflected — in the badge, and end-to-end through the verify API.
- Admin can switch a deactivated license back to Active with no confirmation, and Save persists it immediately.
- A deactivated license's API key fails `/api/license/verify` (`valid: false`) once saved, and reactivating restores `valid: true` — verified end-to-end, not just via direct DB edit.
