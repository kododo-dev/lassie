# Admin Panel UI Refresh Implementation Plan

## Overview

Replace the admin panel's current "stock Pico.css, zero custom styling" look with a
polished, elegant, consistent visual design built on the MudBlazor component library,
across every screen shipped so far: navigation/layout, license list, license
create/edit, and login.

## Current State Analysis

The panel is a Blazor Server app (Interactive Server render mode, .NET 10, Razor
Components hosting model — `src/Program.cs:19-20`, `src/Program.cs:111-112`). The only
stylesheet loaded is a vendored, unmodified `pico.min.css` v2.1.1
(`src/Components/App.razor:8`, `src/wwwroot/css/pico.min.css`) in its classless mode —
there is **zero custom CSS anywhere in the repo** (no `app.css`/`site.css`, no
`.razor.css` scoped files, no `<style>` blocks, one stray inline
`style="overflow-x: auto"` on `src/Components/Pages/PanelHome.razor:23`).

Five routed pages exist: `PanelHome.razor` (`/`, license list), `CreateLicense.razor`
(`/licenses/new`), `EditLicense.razor` (`/licenses/{Id}/edit`), `Login.razor`
(`/login`), `Logout.razor` (`/logout`, no UI — pure redirect). All but `Login.razor` use
`@layout MainLayout` + `@rendermode InteractiveServer`. `MainLayout.razor` is 8 lines: a
bare `<nav>` with two links, no branding, no active-link state, no user info.

Concrete visual gaps found:
- License status (`Active`/`Expired`) renders as raw enum text
  (`PanelHome.razor:38`) — no color, no badge.
- The only bespoke "visual" treatment anywhere is emoji-as-icons (🙈/👁/📋) on the
  reveal-key controls in `CreateLicense.razor:42-43`.
- `EditLicense.razor:24-31` renders error and success messages as two visually
  identical bare `<p>` tags — indistinguishable except by text content.
- `Login.razor` does not specify `@layout` or `@rendermode` at all — it renders outside
  `MainLayout` and outside the interactive circuit.

## Desired End State

Every screen shares a consistent MudBlazor-based visual language (light/dark theme with
a working toggle), reachable via a proper app-bar navigation instead of a bare `<nav>`.
License status renders as a colored chip. Forms use styled inputs, buttons, and
color-coded alerts for errors/success. The reveal-key controls use real icons instead of
emoji. The panel is manually verified usable at 375px and 768px viewport widths. The
login page is visually consistent with the rest of the app despite staying on a
different rendering path (see Critical Implementation Details).

Verify by: `dotnet build src/lassie.csproj` succeeds, `dotnet run --project
src/lassie.csproj` serves all five pages without console errors, and each phase's
Manual Verification steps pass.

### Key Discoveries:

- `Login.razor` has no `@rendermode` because `HttpContext.SignInAsync`
  (`Login.razor:76`) needs direct access to the HTTP response, which is only available
  during static server-side rendering of the initial request — not from inside an
  established Blazor Server circuit. `Logout.razor:12` follows the same pattern for
  `SignOutAsync`. This is deliberate, not an oversight.
- The panel's only architectural constraint on record is "no CDN-hosted CSS" — vendor
  everything locally — established when Pico.css was first adopted
  (`context/changes/module-catalog-management/plan.md:44`, carried forward through
  every subsequent panel slice). MudBlazor's core package ships its CSS/JS as bundled
  static web assets served from `_content/MudBlazor/...` with no CDN involved, so this
  constraint is satisfied by default — the one thing to actively avoid is adding
  MudBlazor's commonly-recommended Google Fonts (Roboto) `<link>`, which *is*
  CDN-hosted.
- MudBlazor's core package already ships `Icons.Material.Filled.*` (and other styles) as
  C#-constant SVG paths — no extra icon package or CDN needed. This satisfies the
  "self-hosted icon set" decision for free.
- No `LicenseStatus`-rendering component exists yet — `PanelHome.razor:38` interpolates
  the enum directly. A shared status-badge component is new, not a refactor of existing
  markup.

## What We're NOT Doing

- Building the audit-history UI (FR-006 has no screen yet — confirmed net-new, not a
  restyle). Explicitly deferred to a separate future change.
- Any backend, data model, EF migration, or verification-API change. `LicenseStatus`,
  `License`, and `/api/license/verify` are untouched.
- The license deactivate/reactivate feature itself (S-04) — the status badge component
  only needs to handle today's two `LicenseStatus` values (`Active`, `Expired`).
- License list search/filtering (parked in the roadmap).
- Removing the leftover `/weatherforecast` sample endpoint in `Program.cs` — unrelated
  pre-existing scaffold cruft.
- Automated UI/browser tests — no test project exists yet; mobile verification is
  manual, per the confirmed approach.
- Any change to authentication/authorization logic or the cookie/session model.
- Persisting the dark/light theme choice across browser restarts — in-session only for
  this slice (a simple bound bool, no localStorage/cookie).
- Designing a bespoke brand color — no logo or brand identity exists yet, so the theme
  adopts MudBlazor's default primary palette rather than inventing one.

## Implementation Approach

Swap the styling foundation from classless Pico.css to the MudBlazor component library
(per confirmed decision), phased from the ground up: wire the library and a light/dark
theme first, then redesign the shared layout/nav, then each content screen in isolation
(list → forms), and finally the one screen (`Login.razor`) that structurally cannot use
MudBlazor components because it must stay on the static-SSR rendering path.

## Critical Implementation Details

**Login page cannot use MudBlazor components.** Confirmed via research: MudBlazor is
not designed to work under static SSR — interactive elements (form inputs, buttons)
don't function without an active Blazor circuit, because MudBlazor's components rely on
JS interop that only exists once a circuit is connected. Since `Login.razor` must stay
non-interactive (see Key Discoveries), Phase 5 keeps its existing plain `InputText`
markup unchanged and instead adds hand-authored CSS (in the new `wwwroot/css/app.css`)
that mirrors the MudBlazor theme's colors/radius, so it *looks* consistent without
*being* a MudBlazor component. Do not attempt to drop `MudTextField`/`MudButton` onto
this page — build it will succeed but the components will render without their
interactive chrome and may behave unpredictably without a circuit.

**Script load order in `App.razor`.** `_content/MudBlazor/MudBlazor.min.js` must load
AFTER `_framework/blazor.web.js`, matching MudBlazor's own official template
(github.com/MudBlazor/Templates). Add the MudBlazor script tag below the existing
Blazor script tag, not above it.

**No Google Fonts link.** MudBlazor's own getting-started docs commonly show adding a
Google Fonts `<link>` for Roboto. Skip it — it's the one piece of the standard MudBlazor
setup that would violate the repo's established no-CDN constraint. The theme's
`Typography` falls back to the next font in the stack (Helvetica/Arial/system
sans-serif); this is a deliberate, accepted tradeoff, not a gap to fix later.

## Phase 1: MudBlazor Foundation

### Overview

Wire MudBlazor into the app (package, service registration, static asset links, root
theme providers) and define the light/dark theme, without touching any page's content
yet. This phase's own success criteria are purely "MudBlazor is present and the app
still builds and runs" — visual redesign starts in Phase 2.

### Changes Required:

#### 1. Package reference

**File**: `src/lassie.csproj`

**Intent**: Add the MudBlazor NuGet package so its components and services become
available.

**Contract**: One new `<PackageReference Include="MudBlazor" Version="9.8.0" />` entry
(current stable release), consistent with how every other package in this file is
pinned to a specific version.

#### 2. Service registration

**File**: `src/Program.cs`

**Intent**: Register MudBlazor's DI services so its components (popovers, theming) work.

**Contract**: `using MudBlazor.Services;` at the top; `builder.Services.AddMudServices();`
added alongside the existing `AddRazorComponents().AddInteractiveServerComponents()`
call (around line 19-20).

#### 3. Host document wiring

**File**: `src/Components/App.razor`

**Intent**: Swap the stylesheet from Pico to MudBlazor and load MudBlazor's JS, without
a Google Fonts CDN reference (see Critical Implementation Details).

**Contract**: Replace the `<link rel="stylesheet" href="css/pico.min.css" />` line
(line 8) with two links — `_content/MudBlazor/MudBlazor.min.css` and a new
`css/app.css` (see change 4) — and add
`<script src="_content/MudBlazor/MudBlazor.min.js"></script>` immediately after the
existing `<script src="_framework/blazor.web.js"></script>` line.

#### 4. Custom override stylesheet (new file)

**File**: `src/wwwroot/css/app.css`

**Intent**: Small home for the handful of hand-authored CSS rules this refresh needs
that MudBlazor doesn't provide out of the box (populated further in Phase 5 for the
login card). Empty/minimal in this phase beyond a file-level comment.

**Contract**: New static file, linked from `App.razor` after the MudBlazor stylesheet so
its rules can override MudBlazor defaults where needed.

#### 5. Remove Pico

**File**: `src/wwwroot/css/pico.min.css`

**Intent**: Delete the now-unused vendored stylesheet — nothing references it after
change 3.

**Contract**: File deletion.

#### 6. Theme definition and root providers

**File**: `src/Components/Layout/MainLayout.razor`

**Intent**: Define a light/dark `MudTheme` (MudBlazor's default primary palette — no
custom brand color, per `## What We're NOT Doing`) and mount the two providers every
interactive page needs. The existing bare `<nav>` stays as-is in this phase; Phase 2
replaces it.

**Contract**: Add `<MudThemeProvider Theme="_theme" @bind-IsDarkMode="_isDarkMode" />`
and `<MudPopoverProvider />` at the top of the component (before the existing `<nav>`),
plus an `@code` block holding `private bool _isDarkMode;` and
`private static readonly MudTheme _theme = new();` (default palette — no custom colors
assigned). `MudDialogProvider`/`MudSnackbarProvider` are intentionally omitted — nothing
in this plan uses dialogs or snackbars.

#### 7. Imports

**File**: `src/Components/_Imports.razor`

**Intent**: Make MudBlazor's components available without a per-file `@using`.

**Contract**: Add `@using MudBlazor`.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `dotnet list src/lassie.csproj package` shows `MudBlazor` as a direct reference

#### Manual Verification:

- `dotnet run --project src/lassie.csproj`, load `/login` then log in: no MudBlazor-related JS errors in the browser console on any page
- Network tab shows `MudBlazor.min.css`/`MudBlazor.min.js` loading from `_content/MudBlazor/...` (not a CDN) and `pico.min.css` no longer requested
- Existing pages still render and function (unstyled-looking is expected — content redesign hasn't happened yet)

**Implementation Note**: Pause here for manual confirmation before proceeding to Phase 2.

---

## Phase 2: Navigation & Layout

### Overview

Replace the bare `<nav>` with a proper MudBlazor app bar: branding, active-section
indication, the logged-in user's email and a logout action (moved here from
`PanelHome.razor`, so it's consistent across every authenticated page instead of
appearing once), and the light/dark theme toggle.

### Changes Required:

#### 1. App bar

**File**: `src/Components/Layout/MainLayout.razor`

**Intent**: Give every authenticated page a consistent top bar instead of two bare
links, and surface user identity/logout/theme-toggle once, centrally, instead of ad hoc
per page.

**Contract**: Replace the `<nav>` block with a `MudAppBar` containing: an app title
("Lassie"), a "New License" nav action, a spacer, the logged-in user's email, a
dark/light `MudIconButton` toggle (bound to the same `_isDarkMode` field from Phase 1,
swapping between `Icons.Material.Filled.DarkMode`/`LightMode`), and a logout action.
Wrap `@Body` in a `MudContainer` for consistent page margins/max-width. Getting the
user's email requires the same `[CascadingParameter] Task<AuthenticationState> AuthState`
+ `OnInitializedAsync` pattern `PanelHome.razor` already uses (see change 2) — move it
here.

#### 2. Remove duplicated user-info UI

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: The layout now owns "who's logged in" and "log out" — remove the duplicate.

**Contract**: Delete the `<p>Logged in as @userEmail</p>` and `<a href="logout">Log
out</a>` lines and the now-unused `userEmail`/`AuthState` fields and their population in
`OnInitializedAsync` (the license-loading part of `OnInitializedAsync` stays).

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- App bar renders identically across `/`, `/licenses/new`, and `/licenses/{id}/edit`
- User email and logout appear once (in the app bar), not duplicated on `PanelHome`
- Dark/light toggle switches the whole app's theme live, immediately
- At 375px viewport width, the app bar doesn't overflow horizontally or clip content

**Implementation Note**: Pause here for manual confirmation before proceeding to Phase 3.

---

## Phase 3: License List

### Overview

Redesign `PanelHome.razor`'s license table and introduce a shared status-badge
component, replacing the raw-enum-text status column.

### Changes Required:

#### 1. Status badge component (new file)

**File**: `src/Components/Shared/LicenseStatusBadge.razor`

**Intent**: One shared place that maps `LicenseStatus` to a visual treatment, so the
list page (and any future consumer) doesn't re-implement the color mapping.

**Contract**: `[Parameter] public LicenseStatus Status { get; set; }`, rendering a
`MudChip` sized `Size.Small` whose `Color` is `Color.Success` for `Active` and
`Color.Default` for `Expired` (a plain switch expression over today's two enum values —
not a forward-compatible open-ended mapper; extending it for a future `Deactivated`
value is that future change's job, not this one's).

#### 2. List page redesign

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: Replace the bare `<table>` (and its manual `overflow-x: auto` scroll
wrapper) with a `MudTable`, and the raw status text with the new badge component.

**Contract**: `MudTable<License> Items="licenses"` with columns for Label, a
`<LicenseStatusBadge Status="context.Status" />` cell, formatted Expires date (same
`ToString("yyyy-MM-dd")` / "No expiry" logic as today), and an edit action rendered as a
`MudIconButton` (`Icons.Material.Filled.Edit`) linking to the existing edit route. Empty
state (`licenses.Count == 0`) becomes a `MudAlert Severity="Severity.Info"` plus a
`MudButton` linking to `/licenses/new`, replacing the two bare `<p>`/`<a>` lines.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- License list shows colored status chips (Active vs Expired visually distinct)
- Empty state (no licenses) looks intentional, not like unstyled placeholder text
- Edit action still navigates to the correct license's edit page
- At 375px viewport width, the table is usable (no unreadable overflow/clipping)

**Implementation Note**: Pause here for manual confirmation before proceeding to Phase 4.

---

## Phase 4: Create/Edit License Forms

### Overview

Redesign both license forms: styled inputs, styled buttons, color-coded alerts for
errors/success (currently indistinguishable bare `<p>` tags), and real icons on the
reveal-key controls instead of emoji.

### Changes Required:

#### 1. Create-license form

**File**: `src/Components/Pages/CreateLicense.razor`

**Intent**: Replace bare inputs/button/messages with MudBlazor equivalents; replace
emoji-as-icons with real icons. Form behavior (validation, submit handler, key-reveal
logic, clipboard copy) is unchanged — only the rendered markup changes.

**Contract**: `InputText`/`InputDate` → `MudTextField`/`MudDatePicker` bound via the
same `@bind-Value="Model.Label"` / `@bind-Value="Model.ExpiresOn"` pattern (MudBlazor's
input components implement `InputBase<T>` and work inside the existing
`EditForm`/`DataAnnotationsValidator` unchanged — no changes to
`CreateLicenseFormModel` or its `[Required]` attribute). The error `<p>` becomes
`MudAlert Severity="Severity.Error"`. The submit `<button>` becomes `MudButton
Variant="Variant.Filled" Color="Color.Primary" ButtonType="ButtonType.Submit"`. The
reveal-key panel becomes a `MudPaper`/`MudCard` wrapping the existing `<code>` key
display; the 🙈/👁 toggle button becomes a `MudIconButton` swapping
`Icons.Material.Filled.Visibility`/`VisibilityOff`; the 📋 button becomes a
`MudIconButton` with `Icons.Material.Filled.ContentCopy` — both keep their existing
`@onclick` handlers (`keyRevealed = !keyRevealed`, `CopyKeyAsync`) unchanged.

#### 2. Edit-license form

**File**: `src/Components/Pages/EditLicense.razor`

**Intent**: Same input/button treatment as Create; additionally make error and success
messages visually distinguishable by color (the concrete gap found in research), not
just by text content.

**Contract**: Same `MudTextField`/`MudDatePicker`/`MudButton` swap as change 1. Error
`<p>` → `MudAlert Severity="Severity.Error"`; success `<p>` → `MudAlert
Severity="Severity.Success"`. The "Cancel" `<a>` becomes a `MudButton Variant="Variant.Text"`
linking to `/`. The "License not found" state becomes `MudAlert Severity="Severity.Warning"`
plus a `MudButton` back-link, replacing the bare `<p>`/`<a>`.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- Submitting the create form with an empty Label still shows a validation error (no
  regression in `DataAnnotationsValidator` behavior)

#### Manual Verification:

- Create and edit forms are visually consistent with the rest of the redesigned app
- Error alert (red/error-colored) and success alert (green/success-colored) are clearly
  distinguishable at a glance, not just by reading the text
- Reveal-key eye/copy icons behave identically to before (mask toggle, clipboard copy)
- Both forms are usable at 375px viewport width

**Implementation Note**: Pause here for manual confirmation before proceeding to Phase 5.

---

## Phase 5: Login Page

### Overview

Make the login page look consistent with the rest of the now-redesigned app, without
using actual MudBlazor components (see Critical Implementation Details) since the page
must stay on the static-SSR rendering path for `HttpContext.SignInAsync` to work.

### Changes Required:

#### 1. Hand-authored auth-card styles

**File**: `src/wwwroot/css/app.css`

**Intent**: Visually match the MudBlazor theme (colors, spacing, corner radius) using
plain CSS, since MudBlazor components themselves can't be used here.

**Contract**: New rules for a centered auth-page container and a card (max-width, centered,
padding, border-radius, subtle shadow) plus a small "error text" style for the login
failure message, using color values matching the `_theme`'s default MudBlazor palette
defined in Phase 1.

#### 2. Login page markup

**File**: `src/Components/Pages/Login.razor`

**Intent**: Apply the new classes; keep every existing input/form/code-behind
line unchanged (no `@rendermode`, no MudBlazor components — this page's interaction
model is untouched).

**Contract**: Wrap the existing `<EditForm>` in a `<div class="auth-page"><div
class="auth-card">...</div></div>` structure using the classes from change 1; replace
the bare `<h1>Log in</h1>` with the same heading styled via the new CSS; give the error
`<p>` the new error-text class. No changes inside `@code`.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- Login page renders as a centered, styled card that looks consistent with the rest of
  the (now MudBlazor-themed) app, despite not using MudBlazor components
- Logging in with valid/invalid credentials still works exactly as before (cookie
  sign-in unchanged)
- Page renders and the form is submittable even before any Blazor JS has finished
  loading (confirms it's still genuinely static-SSR-safe, not silently depending on an
  interactive circuit)
- Usable at 375px viewport width

**Implementation Note**: This is the final phase — after manual confirmation, the change
is ready for `/10x-impl-review`.

---

## Testing Strategy

### Unit Tests:

Not applicable — no test project exists in this repo, and this change is purely
presentational (no new business logic to unit-test).

### Integration Tests:

Not applicable, same reason.

### Manual Testing Steps:

1. Log in, walk through all four authenticated-flow screens plus login/logout, at
   desktop width — confirm the MudBlazor-based redesign renders on every screen and
   nothing regressed functionally (create a license, edit it, log out, log back in).
2. Resize the browser (DevTools responsive mode) to 375px and to 768px on each screen —
   confirm no horizontal overflow, no clipped/unreadable content, app bar and forms
   remain usable.
3. Toggle dark/light mode from the app bar on each screen — confirm the whole app
   (including any modal-like surfaces such as the reveal-key panel) follows the theme
   consistently.
4. Compare the login page side-by-side with an authenticated page — confirm the visual
   language (colors, corner radius, spacing) reads as the same app despite the
   different implementation underneath.

## Performance Considerations

None beyond normal static-asset weight: MudBlazor's CSS/JS bundle is larger than the
Pico stylesheet it replaces, but this is a low-traffic single-tenant admin panel (per
the PRD's scale) — not a budget worth engineering around here.

## Migration Notes

None — no data model, schema, or stored data is touched by this change.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `S-06: Admin panel UI refresh`
- Prior styling decision (Pico.css adoption, no-CDN constraint):
  `context/changes/module-catalog-management/plan.md:44,161`
- Panel-shell carry-forward note: `context/archive/2026-08-07-license-creation-and-verification/plan.md:34`
- Audit-history load-before-mutate rule (unrelated to this plan's scope, but touches the
  same `License` entity): `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: MudBlazor Foundation

#### Automated

- [x] 1.1 dotnet build src/lassie.csproj succeeds — a6737ba
- [x] 1.2 dotnet list src/lassie.csproj package shows MudBlazor as a direct reference — a6737ba

#### Manual

- [x] 1.3 No MudBlazor-related JS console errors on any page — a6737ba
- [x] 1.4 MudBlazor CSS/JS load from _content/MudBlazor/..., pico.min.css no longer requested — a6737ba
- [x] 1.5 Existing pages still render and function — a6737ba

### Phase 2: Navigation & Layout

#### Automated

- [x] 2.1 dotnet build src/lassie.csproj succeeds

#### Manual

- [x] 2.2 App bar renders identically across all authenticated pages
- [x] 2.3 User email and logout appear once, not duplicated
- [x] 2.4 Dark/light toggle switches the whole app's theme live
- [x] 2.5 App bar usable at 375px viewport width

### Phase 3: License List

#### Automated

- [ ] 3.1 dotnet build src/lassie.csproj succeeds

#### Manual

- [ ] 3.2 License list shows colored status chips
- [ ] 3.3 Empty state looks intentional
- [ ] 3.4 Edit action still navigates correctly
- [ ] 3.5 Table usable at 375px viewport width

### Phase 4: Create/Edit License Forms

#### Automated

- [ ] 4.1 dotnet build src/lassie.csproj succeeds
- [ ] 4.2 Empty-Label validation error still triggers on create form

#### Manual

- [ ] 4.3 Create and edit forms visually consistent with redesigned app
- [ ] 4.4 Error and success alerts clearly distinguishable by color
- [ ] 4.5 Reveal-key eye/copy icons behave identically to before
- [ ] 4.6 Both forms usable at 375px viewport width

### Phase 5: Login Page

#### Automated

- [ ] 5.1 dotnet build src/lassie.csproj succeeds

#### Manual

- [ ] 5.2 Login page renders as a centered, styled card consistent with the rest of the app
- [ ] 5.3 Login still works with valid/invalid credentials
- [ ] 5.4 Page renders and form is submittable before Blazor JS finishes loading
- [ ] 5.5 Usable at 375px viewport width
