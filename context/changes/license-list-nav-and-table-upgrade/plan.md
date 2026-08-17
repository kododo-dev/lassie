# Top Nav Rework and License Table Upgrade Implementation Plan

## Overview

Second UI polish pass on the admin panel (first was S-06 `admin-panel-ui-refresh`). Reworks the
top app bar's navigation/actions and upgrades the license list from a bare `MudTable` to a
sortable, filterable `MudDataGrid`.

## Current State Analysis

- `src/Components/Layout/MainLayout.razor:5-17` — `MudAppBar` shows static "Lassie" text (not a
  link), a "New License" button, the logged-in user's email, a dark-mode toggle, and a "Log out"
  button. No user dropdown menu exists anywhere in the codebase yet.
- `src/Components/Pages/PanelHome.razor:24-39` — plain `MudTable` over an in-memory
  `List<License>` loaded once in `OnInitializedAsync` (`DbContext.Licenses.AsNoTracking().OrderBy(l
  => l.Label).ToListAsync()`). No sorting or filtering. Empty state
  (`PanelHome.razor:17-21`) shows its own "Create a license" button separate from the (currently
  app-bar-hosted) "New License" action.
- MudBlazor **9.8.0** (`src/lassie.csproj:18`) — supports `MudDataGrid<T>` with `SortBy` per
  column (custom `Func<T, object>` or `IComparer`) and a built-in filter row (`Filterable="true"`
  + `FilterTemplate`/quick operators).
- `src/Components/Shared/LicenseStatusBadge.razor` — existing `LicenseStatus` enum rendering as a
  `MudChip`; the enum values are `Active`, `Expired`, `Deactivated` (declaration order — not the
  display-priority order needed for sorting).
- Routing runs behind a `PathBase` (`kododo.dev/lassie` in production) — all internal
  `Href`/`NavigateTo` calls in this codebase use relative paths (no leading `/`), e.g.
  `Href="licenses/new"` (`PanelHome.razor:20`), `Navigation.NavigateTo("login", ...)`
  (`Logout.razor:15`). Follow the same convention for the new home link and any new hrefs.
- `MudProviders.razor` must be rendered once per interactive page (see
  `context/foundation/lessons.md` — MudBlazor providers must live in the same render scope as
  per-page `@rendermode` consumers). `PanelHome.razor:13` and `CreateLicense.razor:16` already do
  this; `MainLayout.razor` does not need its own copy since it has no popover-consuming components
  today — a `MudMenu` on the app bar changes that (see Critical Implementation Details).

## Desired End State

- Clicking "Lassie" in the app bar navigates to the home page (`/`) from anywhere in the panel.
- The app bar has no "New License" button. On the right side there is a dark-mode toggle and an
  account-icon button that opens a menu with a single "Log out" item. No user email/name is shown.
- The home page has a toolbar above the license grid with a single "New License" button
  (right-aligned), used in both the empty and populated states — the empty-state alert no longer
  has its own separate button.
- The license grid (`MudDataGrid`) supports sorting on Label, Status, and Expires, and per-column
  filtering on all three:
  - Status sorts by logical priority: Active → Expired → Deactivated (not alphabetical/enum
    declaration order).
  - Expires sorts with "no expiry" (null) licenses always last, in both ascending and descending
    order.
  - Label and Expires use MudDataGrid's default text/date filter operators; Status filters by
    exact match against the enum's display values.
- On narrow (smartphone) viewports, the toolbar stacks to full-width elements and the grid scrolls
  horizontally within its container — no column is hidden, satisfying the panel's responsive NFR.

### Key Discoveries

- No existing `MudMenu` usage in the codebase — this is a net-new UI pattern, not a refactor of an
  existing one.
- No existing `MudDataGrid` usage — the codebase's only prior list rendering is `PanelHome.razor`'s
  `MudTable`, which this plan replaces outright rather than extends.
- `LicenseStatus` enum (used by `LicenseStatusBadge.razor`) has no built-in ordinal that matches
  the desired sort priority — a small rank-mapping function is needed in `PanelHome.razor`.

## What We're NOT Doing

- No backend/API changes — filtering and sorting are client-side (in-memory) over the license list
  already loaded in `OnInitializedAsync`; no query pushdown to the database.
- No pagination — out of scope; the grid still loads and displays the full license list at once
  (matches current behavior, no stated performance problem to solve).
- No changes to `CreateLicense.razor`, `EditLicense.razor`, `Login.razor`, or audit-history pages —
  only `MainLayout.razor` and `PanelHome.razor` are touched.
- No display of the logged-in user's identity anywhere in the app bar (explicitly dropped per the
  user's request).
- No global full-text search box — filtering is per-column only (per the "Per-column filter row"
  decision), not a combined search-everything box.

## Implementation Approach

Two independent phases, no shared dependency beyond both files rendering under the same
`MainLayout`. Phase 1 (app bar) can ship and be manually verified on its own before Phase 2 (data
grid) starts.

## Critical Implementation Details

**MudMenu render scope**: `MainLayout.razor` currently has no `<MudProviders />` (no popover
consumers there today). Adding a `MudMenu` to the app bar means `MainLayout` now needs its own
`<MudPopoverProvider />` in scope — but per the lesson in `context/foundation/lessons.md`,
providers must share the render-mode boundary of their consumer. `MainLayout` is not itself given
an explicit `@rendermode` (it's the shared layout for pages that each set their own), so the
existing per-page `<MudProviders />` (already present on every routable page — `PanelHome.razor`,
`CreateLicense.razor`, etc.) is what actually supplies the `MudPopoverProvider` `MudMenu` needs at
runtime, the same way it already supplies popovers for e.g. `MudDatePicker` on `CreateLicense`.
No new provider placement is needed — just confirm (Phase 1 manual verification) that the menu
opens correctly on every page that uses `MainLayout`, including `PanelHome` and `EditLicense`,
since each supplies its own `<MudProviders />` independently.

## Phase 1: App Bar Rework

### Overview

Rework `MainLayout.razor`'s app bar: brand becomes a home link, "New License" button removed, user
email removed, "Log out" button replaced by an account-icon `MudMenu`.

### Changes Required

#### 1. App bar navigation and user menu

**File**: `src/Components/Layout/MainLayout.razor`

**Intent**: Make the "Lassie" brand text a clickable link to the home page. Remove the "New
License" button entirely (it moves to `PanelHome.razor` in Phase 2). Remove the `_userEmail`
display and its `OnInitializedAsync` lookup — no user identity is shown in the app bar anymore.
Replace the standalone "Log out" `MudButton` with a `MudMenu` triggered by an account-style
`MudIconButton` (e.g. `Icons.Material.Filled.AccountCircle`), containing a single `MudMenuItem`
that navigates to `logout` (same relative-path convention as the current button's `Href="logout"`)
Keep the dark-mode toggle `MudIconButton` as-is, positioned to the left of the new user menu.

**Contract**: The brand `MudText` becomes (or is wrapped by) a link to `href="."` or equivalent
relative home path — use the same relative-path convention as `Href="licenses/new"` elsewhere
(no leading `/`, so the `PathBase` prefix is preserved in production). Remove the
`[CascadingParameter] Task<AuthenticationState> AuthState` and `_userEmail` field/lookup entirely
since nothing in the app bar needs the authenticated user's identity anymore.

### Success Criteria

#### Automated Verification

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification

- Clicking "Lassie" in the app bar from any panel page (home, create license, edit license)
  navigates to the home page, and the production `PathBase` prefix (`/lassie`) is preserved (spot
  check locally is fine — full prefix behavior is already covered by existing infra, just confirm
  the link is relative, not absolute-rooted).
- The app bar no longer shows a "New License" button or the user's email.
- Clicking the account icon opens a menu with exactly one item, "Log out"; clicking it signs out
  and redirects to the login page, same as the previous button did.
- The menu opens correctly on every page using `MainLayout` (home, create license, edit license) —
  confirms the per-page `<MudProviders />` placement covers the new `MudMenu` popover on each.

---

## Phase 2: License List Grid Upgrade

### Overview

Replace `PanelHome.razor`'s `MudTable` with a `MudDataGrid`, add a toolbar with the relocated "New
License" button, and implement sorting/filtering across all three columns with the two custom sort
behaviors (Status priority order, Expires nulls-last) settled during planning.

### Changes Required

#### 1. Toolbar and empty-state consolidation

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: Add a toolbar row above the grid/empty-state area containing a single, right-aligned
"New License" `MudButton` (`Href="licenses/new"`, same as today), shown in both the empty and
populated states. Remove the separate "Create a license" button currently inside the empty-state
`MudAlert` block — the alert keeps its informational text ("No licenses yet.") but no longer has
its own action button, since the toolbar button now covers that case too.

**Contract**: Toolbar renders unconditionally above the `@if (licenses.Count == 0)` branch; the
empty-state branch keeps only the `MudAlert`, its button is deleted.

#### 2. MudTable → MudDataGrid conversion with sort/filter

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: Replace the `MudTable`/`HeaderContent`/`RowTemplate` block with a `MudDataGrid<License>`
bound to the same `licenses` list. Each of the three data columns (Label, Status, Expires) gets
`Sortable="true"` and `Filterable="true"`; the fourth (edit-icon) column stays non-sortable,
non-filterable. Add a `PropertyColumn` (or `TemplateColumn` where a custom cell renderer is
needed, e.g. the `LicenseStatusBadge` chip and the edit icon button) per existing column.

**Contract**:
- Label column: default string `SortBy`/filter behavior (MudDataGrid's built-in text filter
  operators) — satisfies the "at minimum sortable/filterable by name" baseline requirement.
- Status column: custom `SortBy` using a rank function `Active` → 0, `Expired` → 1, `Deactivated`
  → 2 (do not rely on enum declaration order, which is `Active, Expired, Deactivated` today but is
  not a contract to depend on for sort meaning — write the mapping explicitly so it can't silently
  drift if the enum is reordered later). Filter uses MudDataGrid's built-in enum/select filter
  operator (exact match against status).
- Expires column: custom `SortBy`/comparer where `null` (no expiry) always sorts after every
  non-null date, in both ascending and descending grid sort direction — MudDataGrid's default null
  handling flips null position by direction, which this explicitly overrides. Filter uses the
  built-in date filter operators; a null "Expires" value should not match any date-based filter
  operator (excluded from filtered results when a date filter is active, same as it has no date to
  compare).
- Responsive behavior: no MudDataGrid column-hiding/breakpoint props — the grid's outer container
  gets horizontal-scroll styling (e.g. wrap in a `div` with `overflow-x: auto` or the equivalent
  MudBlazor utility class) so all columns remain reachable on narrow viewports without being
  dropped. The toolbar row (button + any grid-level controls) uses a responsive flex layout that
  stacks to full-width on narrow viewports instead of the current single-row layout.

### Success Criteria

#### Automated Verification

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification

- Grid displays all existing licenses with the same data as before (Label, status badge, formatted
  expiry or "No expiry").
- Clicking each column header sorts ascending/descending; Status sorts Active → Expired →
  Deactivated (not alphabetically); Expires keeps "No expiry" rows last regardless of sort
  direction.
- Typing into each column's filter row narrows the grid to matching rows; clearing the filter
  restores the full list. Label filter matches substrings of the license name.
- "New License" button appears above the grid in both the empty-list state and the populated
  state, navigates to `licenses/new`, and the empty-state alert no longer shows its own separate
  button.
- Edit icon button still navigates to `licenses/{id}/edit` for each row.
- Resizing the browser to a smartphone-width viewport: the toolbar stacks to full-width elements,
  the grid becomes horizontally scrollable within its own container (page itself does not scroll
  horizontally), and no column or its filter/sort control is inaccessible.

---

## Testing Strategy

### Manual Testing Steps

1. Log in, land on the home page — confirm toolbar + grid render with existing seed/test license
   data.
2. Click "Lassie" from the create-license and edit-license pages — confirm it returns to the home
   page from anywhere in the panel.
3. Open the account menu from multiple pages, confirm "Log out" works identically to before.
4. Sort each column ascending and descending; verify Status and Expires special-case ordering.
5. Filter each column individually and in combination; verify results and confirm clearing filters
   restores the full list.
6. Create a license with no expiry date and one with a far-future expiry; confirm the no-expiry
   license sorts last in both sort directions on the Expires column.
7. Resize to a narrow viewport and repeat steps 4–5 to confirm no functionality is lost.

## Performance Considerations

None — sorting/filtering stays client-side over the already-loaded in-memory list, same data
volume as today's `MudTable`. No new queries introduced.

## Migration Notes

None — no data model or persisted-schema changes.

## References

- Prior UI polish pass: `context/archive/2026-08-11-admin-panel-ui-refresh/`
- Lesson on MudBlazor provider render scope: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles.

### Phase 1: App Bar Rework

#### Automated

- [x] 1.1 Build succeeds: `dotnet build src/lassie.csproj`

#### Manual

- [x] 1.2 Brand link navigates home from every panel page, relative path preserves `PathBase`
- [x] 1.3 App bar no longer shows "New License" button or user email
- [x] 1.4 Account menu opens with one "Log out" item; logout works
- [x] 1.5 Menu opens correctly on every page using `MainLayout`

### Phase 2: License List Grid Upgrade

#### Automated

- [ ] 2.1 Build succeeds: `dotnet build src/lassie.csproj`

#### Manual

- [ ] 2.2 Grid displays all licenses with same data as before
- [ ] 2.3 Column sorting works; Status and Expires special-case ordering verified
- [ ] 2.4 Per-column filtering works and clears correctly
- [ ] 2.5 "New License" toolbar button present in both empty/populated states; empty-state's own
  button removed
- [ ] 2.6 Edit icon button still navigates correctly
- [ ] 2.7 Narrow-viewport layout: toolbar stacks, grid scrolls horizontally, no lost functionality
