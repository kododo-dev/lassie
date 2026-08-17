# Top Nav Rework and License Table Upgrade — Plan Brief

> Full plan: `context/changes/license-list-nav-and-table-upgrade/plan.md`

## What & Why

Second UI polish pass on the admin panel, requested directly by the user. The top app bar's
navigation is cluttered (a page action mixed in with branding/identity/logout), and the license
list table looks bare and can't be sorted or filtered — both make daily license management feel
less professional than it should.

## Starting Point

`MainLayout.razor`'s app bar today: static "Lassie" text (not clickable), a "New License" button,
the logged-in user's email, a dark-mode toggle, and a plain "Log out" button. `PanelHome.razor`
renders licenses in a plain `MudTable` with no sort/filter. MudBlazor 9.8.0 is already the panel's
component library.

## Desired End State

"Lassie" is a home link. The app bar's right side is just a dark-mode toggle and an account-icon
menu with "Log out" — no user email shown. The home page has a "New License" button in a toolbar
above the license grid (used for both empty and populated states). The grid sorts and filters on
every column, with two deliberate custom orderings: Status sorts by meaning (Active → Expired →
Deactivated), and licenses with no expiry always sort last regardless of direction. On narrow
screens the toolbar stacks and the grid scrolls horizontally rather than losing columns.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Table component | `MudDataGrid` (replaces `MudTable`) | Built-in per-column sort/filter satisfies "every column" with far less custom code than hand-rolling it on `MudTable` | Plan |
| Filter UX | Per-column filter row | Directly matches "ideally every column," one consistent mechanism | Plan |
| New License button | Single toolbar button, reused for empty + populated states | Avoids duplicate CTAs; empty-state alert keeps its text, loses its own button | Plan |
| User menu trigger | Plain account-icon button | Matches the existing icon-button style already next to it (dark-mode toggle) | Plan |
| Status sort order | Logical priority (Active → Expired → Deactivated), not alphabetical/enum order | Admin sorting by status wants active licenses grouped first | Plan |
| Null expiry sort | Always last, both directions | A no-expiry license isn't a date — pinning it avoids nulls flipping position on direction change | Plan |
| Mobile layout | Stack toolbar, horizontal-scroll the grid (no column hiding) | Keeps every column reachable, satisfying the panel's "no loss of functionality" responsive NFR | Plan |

## Scope

**In scope:**
- `MainLayout.razor` app bar rework (home link, remove New License button, remove user email,
  account-icon `MudMenu` with Log out)
- `PanelHome.razor` toolbar + `MudTable` → `MudDataGrid` conversion with sort/filter on all columns

**Out of scope:**
- Backend/API changes, pagination, server-side sort/filter pushdown
- Any change to `CreateLicense.razor`, `EditLicense.razor`, `Login.razor`, audit-history pages
- Global full-text search box (per-column filtering only)
- Displaying user identity anywhere in the app bar

## Architecture / Approach

Two independent phases touching one file each — `MainLayout.razor` (Phase 1) and `PanelHome.razor`
(Phase 2) — with no shared dependency beyond both rendering under the same layout. All
sorting/filtering stays client-side over the license list already loaded once in
`OnInitializedAsync`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. App Bar Rework | Home link, removed New License button/user email, account-icon logout menu | `MudMenu` popover render-scope correctness across every page using `MainLayout` (see plan's Critical Implementation Details) |
| 2. License List Grid Upgrade | `MudDataGrid` with toolbar, per-column sort/filter, Status/Expires custom ordering, responsive layout | Getting the two custom sort comparators (Status priority, Expires nulls-last) correct in both directions |

**Prerequisites:** None — both files already exist and compile today.
**Estimated effort:** ~1 session, 2 phases.

## Open Risks & Assumptions

- Assumes `MudDataGrid`'s built-in filter operators (text/date/enum) are sufficient without custom
  `FilterTemplate`s — if the built-in Status enum filter UX turns out to be awkward in MudBlazor
  9.8.0 at implementation time, a small custom filter template may be needed (still within Phase
  2's scope, not a plan change).
- `MainLayout.razor` gets no new `<MudProviders />` of its own — relies on each page's existing
  per-page `<MudProviders />` to supply the `MudMenu`'s popover, consistent with the codebase's
  established pattern (see lesson in `context/foundation/lessons.md`). Flagged for explicit manual
  verification in Phase 1 rather than assumed correct.

## Success Criteria (Summary)

- Admin can navigate home from anywhere via the "Lassie" link, and log out via a clean account menu
  with no identity clutter in the app bar.
- Admin can sort and filter the license list on every column, with Status and Expires ordering
  matching real-world meaning rather than raw data order.
- Panel remains fully usable (no lost functionality) on a smartphone-width screen.
