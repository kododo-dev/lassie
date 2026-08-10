# License List View — Plan Brief

> Full plan: `context/changes/license-list-view/plan.md`

## What & Why

Admin lands on `/` and sees every license — label, current status, and expiry — with a link
into each license's edit page. This is PRD `FR-012` / roadmap slice `S-05`, and it's also the
in-panel entry point `S-03`'s plan explicitly deferred ("S-05 will supply the real entry point
later" for reaching `/licenses/{id}/edit`).

## Starting Point

`PanelHome.razor` (route `/`) is a literal placeholder today — "Logged in as X" + a "Log out"
link, no license data. `EditLicense.razor` is only reachable by typing its URL directly. No page
in the codebase renders a list/table yet, so this is new territory, not a copy-paste job.

## Desired End State

Navigating to `/` while authenticated shows every license, sorted by label, each tagged
Active/Expired with its expiry date, and a link to edit it. Zero licenses shows an empty-state
message with a link to create one. The verification API's behavior is unchanged.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Where the list lives | Replace `/` (PanelHome) | `/` is unused placeholder content today; the list is the most useful thing an admin could land on. |
| Status model | Shared `LicenseStatus` enum + `License.GetStatus()`, reused by the verify endpoint | Avoids a second hand-copied validity rule drifting from the one API clients actually depend on — that exact rule was already the subject of a critical plan-review fix once (day-boundary bug). |
| Sort order | Label, A-Z | Predictable, matches how an admin thinks ("which client's license"). |
| Row detail | Status badge + raw expiry date | Matches what Create/Edit already show; avoids an extra click just to see the date. |
| Mobile layout | Horizontally scrollable table (Pico's `<figure>` pattern) | Simplest correct option for the first list page in the codebase; meets the PRD's mobile-usability NFR without new CSS machinery. |
| Edit-link scope | Rows link to `/licenses/{id}/edit` | Closes the entry-point gap `S-03`'s plan explicitly deferred to this slice. |

## Scope

**In scope:**
- List of all licenses at `/`, sorted alphabetically by label
- Active/Expired status badge per license, computed by a rule shared with the verification API
- Expiry date shown per license ("No expiry" when null)
- Edit link per row to the existing `/licenses/{id}/edit` page
- Empty-state message + create-license link when there are no licenses yet
- Mobile-usable table layout

**Out of scope:**
- Search/filtering (explicit PRD non-goal)
- Pagination (small target scale)
- Deactivation/reactivation status (`S-04`, separate slice) — `LicenseStatus` is shaped as an enum so a `Deactivated` case can be added later without redesigning this page
- "Expiring soon" warning state (explicit PRD non-goal)
- Any change to `CreateLicense.razor`/`EditLicense.razor` internals, or to `MainLayout.razor`'s nav

## Architecture / Approach

Phase 1 extracts the verify endpoint's existing, already-reviewed validity expression into
`License.GetStatus()` (a `LicenseStatus` enum: `Active`/`Expired`), with zero behavior change to
`/api/license/verify`. Phase 2 rewrites `PanelHome.razor` to query all licenses and render them
in a table built on that same shared method, so the list can never show a status the verify API
would disagree with.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared LicenseStatus + verify-endpoint refactor | `LicenseStatus` enum + `License.GetStatus()`; verify endpoint calls it instead of inlining the rule | Silently reintroducing the day-boundary bug a prior plan-review already fixed once, if the comparison isn't reproduced exactly |
| 2. License list page | `PanelHome.razor` rewritten as a sorted, status-tagged license table with edit links and an empty state | First list/table page in the codebase — no existing responsive-table pattern to copy |

**Prerequisites:** `S-02` (done), `F-01`/`F-02` (done) — all satisfied.
**Estimated effort:** ~1 session across 2 phases.

## Open Risks & Assumptions

- Assumes Pico.css's `<figure>` wrapper gives adequate horizontal-scroll behavior for a 4-column
  table without additional custom CSS — first real test of this framework's table handling in
  the repo.
- Assumes "current status" only needing Active/Expired (no third state) holds until `S-04` ships;
  the enum shape is deliberately chosen to absorb that later without a redesign.

## Success Criteria (Summary)

- Admin sees an accurate, sorted list of all licenses with correct status/expiry at `/`.
- Every row's status agrees with what `/api/license/verify` reports for that license.
- Admin can reach any license's edit page from the list without knowing its URL.
- The page remains usable on a small/mobile screen.
