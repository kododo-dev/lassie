---
change_id: license-list-nav-and-table-upgrade
title: Top nav rework and license table upgrade (sorting/filtering)
status: implementing
created: 2026-08-17
updated: 2026-08-17
archived_at: null
---

## Notes

Another round of UI polish, requested directly by the user (not on `roadmap.md` — S-06 admin
panel UI refresh is already `done`/archived). Two parts:

1. **Top app bar (`MainLayout.razor`)**: "Lassie" brand text should link to the home page.
   Remove the "New License" button from the app bar — move it to the home page, above the
   license table. Add a right-aligned user dropdown menu containing just "Log out" (no need to
   display the current user's email/name in the bar).
2. **Home page (`PanelHome.razor`)**: the license table currently looks bare (plain `MudTable`).
   Wants a more professional look, plus sorting and filtering — at minimum sortable/filterable by
   name (Label), ideally on every column (Label, Status, Expires).

Codebase already uses MudBlazor (`MudProviders.razor`, `MudTable` in `PanelHome.razor`) — likely
upgrade path is `MudTable` sortable columns + a filter/search box, or `MudDataGrid` which has
built-in per-column sort/filter. Worth comparing at plan time.
