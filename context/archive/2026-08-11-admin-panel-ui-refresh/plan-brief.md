# Admin Panel UI Refresh — Plan Brief

> Full plan: `context/changes/admin-panel-ui-refresh/plan.md`

## What & Why

The admin panel currently has zero custom styling — stock, classless Pico CSS on every
screen, license status shown as raw enum text, emoji used as icons, and error/success
messages that look identical. This change gives the panel a real, elegant visual design
by switching to the MudBlazor component library, so it looks like a finished product
instead of a scaffold.

## Starting Point

Blazor Server app (Interactive Server render mode, .NET 10). Five pages exist: license
list (`/`), create (`/licenses/new`), edit (`/licenses/{id}/edit`), login, logout
(no UI). Only `pico.min.css` is loaded, unmodified, in classless mode — no custom CSS
file exists anywhere in the repo.

## Desired End State

Every panel screen shares a consistent MudBlazor-based look with a working light/dark
toggle: a proper app-bar nav instead of two bare links, license status as a colored
chip, styled forms with color-coded error/success alerts, real icons instead of emoji.
The login page looks consistent with the rest of the app despite staying on a different
rendering path for a technical reason (see below).

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Styling architecture | MudBlazor component library | User's explicit choice — full component library over hand-rolled CSS or Pico theming. |
| Theme | Light + dark, with a toggle | User's explicit choice; default MudBlazor primary palette (no brand identity exists to draw a custom color from). |
| Icons | MudBlazor's built-in Material icon set | Self-hosted, bundled in the core package — satisfies the "SVG icons, no CDN" decision without an extra package. |
| Fonts | No Google Fonts CDN link; fall back to system sans-serif | Preserves the repo's established "no CDN-hosted CSS" constraint (first set when Pico was adopted). |
| Audit-history screen | Out of scope for this change | Confirmed via research it doesn't exist yet at all (backend-only) — building it is net-new scope, not a restyle; deferred to a future change. |
| Login page | Stays plain HTML, hand-styled with CSS to *look* consistent — no MudBlazor components | MudBlazor requires an interactive circuit; `Login.razor` must stay static-SSR because `HttpContext.SignInAsync` needs direct response access unavailable inside a circuit. |
| Mobile verification | Manual, DevTools responsive mode at 375px/768px | User's explicit choice over automated screenshot tooling. |
| Scope trimming | None — everything in this plan is must-have | User's explicit choice. |

## Scope

**In scope:** MudBlazor integration + theme, app-bar navigation redesign, license list
redesign (status chips, table), create/edit form redesign (styled inputs, alerts,
icons), login page visual consistency (hand-styled, not MudBlazor).

**Out of scope:** audit-history UI (doesn't exist yet — separate future change), any
backend/data-model/API change, the deactivate/reactivate feature itself (S-04), list
search/filtering, automated UI tests, persisting the theme choice across browser
restarts, custom brand color.

## Architecture / Approach

Swap the styling foundation (Pico → MudBlazor) first, then work outward: shared
layout/nav, then each content screen. The one exception is the login page, which keeps
its current plain-HTML, non-interactive implementation and gets matching hand-authored
CSS instead of real MudBlazor components, because it structurally cannot run them.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. MudBlazor Foundation | Package, theme, root providers wired; app still builds/runs | Getting the no-CDN font constraint right from the start |
| 2. Navigation & Layout | App bar with branding, user info, logout, theme toggle | None significant — additive, low-risk |
| 3. License List | MudTable + status-chip component | None significant |
| 4. Create/Edit Forms | Styled inputs, color-coded alerts, real icons | None significant — 1:1 component swap |
| 5. Login Page | Hand-styled card matching the new theme | Must stay non-interactive; easy to accidentally break sign-in if scope creeps into "add real MudBlazor here" |

**Prerequisites:** None beyond what's already `done` on the roadmap (F-02, S-02, S-03,
S-05 — all shipped).
**Estimated effort:** ~5 phases, roughly one focused session each — mostly mechanical
component swaps once Phase 1's foundation is in place.

## Open Risks & Assumptions

- MudBlazor 9.x's compatibility with `net10.0` hasn't been verified beyond "targets
  .NET 8/9, no reason to expect breakage" from research — worth confirming at the start
  of Phase 1 (`dotnet build` will surface any real incompatibility immediately).
- The login page's hand-styled CSS approach is a deliberate, researched decision, not a
  fallback to test — no MudBlazor-on-static-SSR spike is needed.

## Success Criteria (Summary)

- Every panel screen looks and feels like one consistent, designed product — not a bare
  scaffold — when viewed at desktop and at 375px/768px widths.
- License status, form errors, and form success are all visually distinguishable at a
  glance (color/chip), not just by reading text.
- Nothing functional regresses: login, create, edit, list-navigation, and the reveal-key
  flow all behave exactly as before.
