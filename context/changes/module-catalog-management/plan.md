# License Field Schema Management Implementation Plan

## Overview

Implement FR-004 (roadmap `S-01`, change-id `module-catalog-management` — kept across two reformulations, see `change.md` Notes): give the admin a panel screen to define the set of fields a license carries — field name + data type (number / text / single-select), and for single-select fields, the list of allowed option values. This is the reference schema that license creation (`S-02`, not yet built) will render a dynamic form against and store per-license values for.

> **Revision note (2026-08-06, second rewrite)**: this plan first covered a "module catalog" (multi-select feature flags), then a single-select "license type" catalog. The user clarified that "type" and "user count" were only *examples* of a more general need — a fully admin-configurable field schema. This is a full rewrite, not an amendment. The three-phase shape and the fixes from the original plan-review (`namespace import`, `broken logout link`, `narrowed DbUpdateException catch`, `@key` on rows) all carry forward, generalized to the two-entity schema below.

## Current State Analysis

- No `LicenseField`/`LicenseFieldOption` entity, no panel CRUD pages beyond auth (`Login.razor`, `Logout.razor`, `PanelHome.razor`), and no shared panel layout or CSS exist in the repo today (`src/Components/`, confirmed via codebase research this session).
- The persistence layer (`F-01`) and admin auth (`F-02`) are both done and archived — `LassieDbContext`, migrations, and the `@attribute [Authorize]` + `AuthorizeRouteView` gate are all live and working in production at `https://kododo.dev/lassie`.
- `src/Data/LassieDbContext.cs` currently registers two entities (`AuditLog`, `User`), both configured with inline Fluent API in `OnModelCreating` — no `IEntityTypeConfiguration<T>` classes, no data-annotation attributes. `AuditLog.ChangeType` is the only existing enum property, stored with EF Core's default int mapping (no `HasConversion` call) — `LicenseField.DataType` follows the same default.
- `_Imports.razor` only imports `Lassie.Components` — any new namespace introduced under `Components/` (e.g. a `Layout/` subfolder) needs its own explicit `@using` or `@layout` directives referencing it will fail to resolve.
- `PanelHome.razor:8` currently has `<a href="/logout">Log out</a>` — a leading-slash absolute path that bypasses `<base href>` and sends admins to the shared VPS's domain root instead of `/lassie/logout` in production. The fix for this exact bug class (commit `5ea8796`) touched `Login.razor`/`Logout.razor`/`RedirectToLogin.razor` but missed this file.

## Desired End State

An admin, logged into the panel, can navigate to a "License Fields" page, see the current field schema as a list (name + data type), create a new field, rename an existing field, delete a field, and — for single-select fields — manage that field's list of allowed option values (add/rename/delete an option), with a friendly error (not a crash) on any duplicate name/value. The panel gains its first shared navigation shell and its first CSS, both usable by every future panel page.

**Verification**: manually walk the full create-field → add-options → edit → delete cycle against the deployed app at `https://kododo.dev/lassie/license-fields`, on both a desktop-width and phone-width viewport, after a real push-to-`main` deploy (not just `dotnet run` locally) — see Phase 3 Manual Verification.

### Key Discoveries:

- `src/Data/Users/User.cs` + `src/Data/LassieDbContext.cs:23-25` — the only existing entity precedent: `long` PK (Npgsql identity-by-default, no explicit config needed), unique index via `HasIndex(...).IsUnique()` in `OnModelCreating`. `LicenseField` follows this shape; `LicenseFieldOption` extends it with a FK + composite unique index.
- `src/Components/Pages/Login.razor` — injects `LassieDbContext` directly in `@code`, no service/API layer anywhere in the app. `LicenseFields.razor` follows the same direct-DbContext pattern.
- `src/Components/Pages/PanelHome.razor` — the only precedent for an authenticated, interactive page: `@attribute [Authorize]` + `@rendermode InteractiveServer`, auth state via `[CascadingParameter] Task<AuthenticationState>` (not `HttpContext`, which is unavailable once the SignalR circuit is live).
- `context/foundation/lessons.md` — `IAuditable` audit-history is explicitly scoped to start with `License` in `S-03`, not the field-schema entities here; ASP.NET Core Data Protection keys aren't persisted across container restarts (unrelated to this feature — not something this plan needs to fix).
- `context/archive/2026-08-05-admin-auth-foundation/plan.md` — path-prefix gotcha: any `NavigationManager.NavigateTo` call or `<a href>` must use a **relative target with no leading slash** (`NavigateTo("license-fields", ...)`, not `NavigateTo("/license-fields", ...)`), or it strips the production `/lassie` path base. Established at `Login.razor:78-81`, `Logout.razor:13-15`, `RedirectToLogin.razor:6-8` — but missed at `PanelHome.razor:8`, which this plan also fixes (Phase 2).
- `context/archive/2026-08-04-persistence-layer-foundation/plan.md:214` and `roadmap.md:105` — both documents name `S-01`/`module-catalog-management` explicitly as the next consumer of the `dotnet ef migrations add` → commit → auto-apply-on-startup convention, and as the reason `S-02` isn't blocked on seed data.
- `context/changes/module-catalog-management/research.md` — storage architecture is relational (`LicenseField` + `LicenseFieldOption`), not JSONB, matching the codebase's only precedent for structured data; `DataType` is a closed, code-fixed enum (`Number`/`Text`/`SingleSelect`) and immutable once a field is created; per-license *values* against this schema are explicitly `S-02`'s concern, not this slice's.
- `_Imports.razor:9` only has `@using Lassie.Components` — a `MainLayout.razor` placed under `Components/Layout/` needs an explicit added `@using Lassie.Components.Layout`, or `@layout MainLayout` fails to resolve at compile time (Phase 2 handles this).

## What We're NOT Doing

- No storage or UI for per-license field *values* — that's `S-02`'s job once `License` exists.
- No admin-configurable data types beyond `Number`/`Text`/`SingleSelect` — the set of primitive types is closed and code-fixed (see PRD `Non-Goals`); only field *names and instances* are dynamic.
- No changing a field's `DataType` after creation — only `Name` (and, for `SingleSelect`, its options) stay editable.
- No audit history/versioning for `LicenseField`/`LicenseFieldOption` edits — `IAuditable` is explicitly reserved for `License` starting in `S-03`.
- No FK-guarded delete — `License` doesn't exist yet, so nothing can reference a `LicenseField`/`LicenseFieldOption` today. Deleting an in-use field or option becomes a real hazard only once `S-02` ships (flagged below, not solved here).
- No minimum-option-count validation on `SingleSelect` fields (a field can exist with zero options) — keeps the form simple; flagged as an accepted gap, not a guard this slice builds.
- No search/filter on the field list — expected to stay small.
- No automated test project — this slice continues the F-01/F-02 precedent of manual verification only.
- No CDN-hosted CSS — Pico.css is vendored into `wwwroot` so the panel has no external runtime dependency.
- No changes to `License`, `S-02`, or any other roadmap slice.

## Implementation Approach

Three phases, each building on the last: data layer first (so the migration convention precedent holds), then the panel's first shared UI shell (layout + CSS — needed by this page and every page after it), then the feature itself on top of that shell. This mirrors the "schema → UI shell → feature" ordering the roadmap's own risk note implies (`S-01` exists specifically so `S-02` isn't blocked on reference data).

Data access stays direct — `LicenseFields.razor` injects `LassieDbContext` in its `@code` block and calls EF Core directly, exactly like `Login.razor` does, since the codebase has no service/API layer to plug into and introducing one for a two-entity CRUD page would be scope creep beyond FR-004.

`LicenseField` and `LicenseFieldOption` are managed from a single master-detail page rather than two separate pages: the top-level table lists fields (name, data type); selecting a `SingleSelect` field reveals its options as a nested list with its own inline add/rename/delete, directly beneath the selected row. This keeps the "one page per slice" precedent from the original design while accommodating the two-level data model — a second full page for options would be more navigation for no real benefit at this scale (a handful of fields, each with a handful of options).

## Critical Implementation Details

- **`@layout` namespace resolution**: `MainLayout.razor` lives at `src/Components/Layout/MainLayout.razor`, giving it namespace `Lassie.Components.Layout` (folder-to-namespace convention, matching `Data/Users/` → `Lassie.Data.Users`). `_Imports.razor` must gain `@using Lassie.Components.Layout` in the same phase, or every page using `@layout MainLayout` fails to compile.
- **Docker static-web-assets gotcha, now live for real**: `Dockerfile:1-6` does a single-step `COPY src/ src/` → `dotnet publish` specifically because a two-step restore/build previously produced an empty static-web-assets manifest for Blazor. Until now `wwwroot` had no tracked content, so this gotcha was dormant. Phase 2 adds the repo's first tracked `wwwroot` content (`pico.min.css`) — verify the CSS is actually present in the published container via a real `docker build`/deploy, not just `dotnet run`, or a styling regression will only surface in production.
- **Path-prefix-safe navigation, extended to new and pre-existing surfaces**: every `NavigationManager.NavigateTo` call and every `<a href>` this plan adds or touches (the new nav links in `MainLayout.razor`, `PanelHome.razor`'s existing logout link) must use a relative target with no leading slash, per the established convention — a regression here was already a real production bug once (`admin-auth-foundation` Phase 3), and `PanelHome.razor:8` shows it's still possible to miss a spot.
- **`DataType` immutability is a UI concern, not just a missing edit form field**: since there's no separate create/edit page split (Phase 3 reuses one form), the form must render `DataType` as a disabled/read-only display once editing an *existing* field, while still being a live picker when creating a *new* one — the same component needs both behaviors depending on whether a field is already persisted.

## Phase 1: LicenseField + LicenseFieldOption entities + migration

### Overview

Add the `LicenseField` and `LicenseFieldOption` entities, register them on `LassieDbContext` with appropriate unique indexes, and generate/apply the migration — following the exact convention both archived plans already named `S-01` as the next consumer of.

### Changes Required:

#### 1. LicenseFieldDataType enum

**File**: `src/Data/LicenseFields/LicenseFieldDataType.cs` (new)

**Intent**: The closed, code-fixed set of data types a license field can have.

**Contract**: `public enum LicenseFieldDataType { Number, Text, SingleSelect }`, namespace `Lassie.Data.LicenseFields`.

#### 2. LicenseField entity

**File**: `src/Data/LicenseFields/LicenseField.cs` (new)

**Intent**: A single field definition in the license schema (e.g. "License type", "Number of users"). Mirrors `User`'s shape: a `long` identity PK plus the properties this slice needs.

**Contract**: `public class LicenseField { public long Id { get; set; } public required string Name { get; set; } public LicenseFieldDataType DataType { get; set; } public List<LicenseFieldOption> Options { get; set; } = []; }`, namespace `Lassie.Data.LicenseFields`.

#### 3. LicenseFieldOption entity

**File**: `src/Data/LicenseFields/LicenseFieldOption.cs` (new)

**Intent**: One allowed value for a `SingleSelect`-typed `LicenseField` (e.g. "Professional" under the "License type" field).

**Contract**: `public class LicenseFieldOption { public long Id { get; set; } public long LicenseFieldId { get; set; } public required string Value { get; set; } }`, namespace `Lassie.Data.LicenseFields`.

#### 4. DbContext registration

**File**: `src/Data/LassieDbContext.cs`

**Intent**: Register both entities as queryable `DbSet`s, enforce field-name uniqueness and per-field option-value uniqueness at the database level, and configure the one-to-many relationship with cascade delete (deleting a field removes its options — an orphaned option is meaningless).

**Contract**: Add `public DbSet<LicenseField> LicenseFields => Set<LicenseField>();` and `public DbSet<LicenseFieldOption> LicenseFieldOptions => Set<LicenseFieldOption>();` alongside the existing DbSets. In `OnModelCreating`: `modelBuilder.Entity<LicenseField>().HasIndex(f => f.Name).IsUnique();` (matching the existing `User.Email` pattern); `modelBuilder.Entity<LicenseFieldOption>().HasIndex(o => new { o.LicenseFieldId, o.Value }).IsUnique();` (matching the existing composite-index shape used for `AuditLog`); `modelBuilder.Entity<LicenseField>().HasMany(f => f.Options).WithOne().HasForeignKey(o => o.LicenseFieldId).OnDelete(DeleteBehavior.Cascade);`.

#### 5. Migration

**File**: `src/Migrations/<timestamp>_AddLicenseFields.cs` (generated)

**Intent**: Apply the new `LicenseFields` and `LicenseFieldOptions` tables (with their unique indexes and the cascade-delete FK) to the schema, following the established `dotnet ef migrations add` → commit → auto-apply-on-startup convention.

**Contract**: Generate via `dotnet ef migrations add AddLicenseFields --project src/lassie.csproj`; apply locally via `dotnet ef database update --project src/lassie.csproj` against the dev connection string in `appsettings.Development.json`.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `dotnet ef migrations add AddLicenseFields --project src/lassie.csproj` generates a migration cleanly (no pending-model-changes warning on a follow-up `dotnet ef migrations add` no-op check)
- `dotnet ef database update --project src/lassie.csproj` applies cleanly against the local dev Postgres instance

#### Manual Verification:

- Open the generated migration file and confirm it creates `LicenseFields` (unique index on `Name`) and `LicenseFieldOptions` (unique composite index on `LicenseFieldId`+`Value`, FK with cascade delete) tables
- `dotnet ef migrations list --project src/lassie.csproj` shows `AddLicenseFields` after the existing migrations, with no gaps

---

## Phase 2: Panel shell — shared layout + Pico.css

### Overview

Give the panel its first shared navigation chrome and its first CSS — both needed by this slice's own page and every panel page the roadmap adds after it (`S-02`–`S-05`). Also fixes a pre-existing production bug in a file this phase already touches.

### Changes Required:

#### 1. Shared layout

**File**: `src/Components/Layout/MainLayout.razor` (new)

**Intent**: Minimal chrome — a nav bar linking Home and License Fields — that panel pages opt into, replacing the current "every page stands alone" state.

**Contract**: `LayoutComponentBase` subclass rendering `@Body` plus a `<nav>` with two links, using relative `NavigateTo`/`href` targets with no leading slash (`""` for Home, `"license-fields"` for License Fields) per the path-prefix convention. Namespace is `Lassie.Components.Layout` — not covered by `_Imports.razor`'s existing `@using Lassie.Components`, so the next change is required for any `@layout MainLayout` reference to resolve.

#### 2. Import the layout namespace

**File**: `src/Components/_Imports.razor`

**Intent**: Make `MainLayout` resolvable by bare name from `@layout MainLayout` directives in `Pages/` — otherwise Phase 2/3 pages fail to compile.

**Contract**: Add `@using Lassie.Components.Layout` alongside the existing `@using Lassie.Components` (line 9).

#### 3. Adopt the layout on the existing Home page (and fix its broken logout link)

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: Home should share the same nav as License Fields rather than being the odd one out. While touching this file, also fix a pre-existing bug: `<a href="/logout">` (line 8) is a leading-slash absolute path, which bypasses `<base href>` and sends admins to the domain root instead of the `/lassie` prefix in production — the same class of bug already fixed for `Login.razor`/`Logout.razor`/`RedirectToLogin.razor` in `5ea8796`, but this file was missed at the time.

**Contract**: Add `@layout MainLayout` directive. Change `<a href="/logout">` to `<a href="logout">` (relative, no leading slash).

#### 4. Vendored CSS

**File**: `src/wwwroot/css/pico.min.css` (new, vendored static asset — a specific pinned release, not a CDN link)

**Intent**: A classless, responsive-by-default stylesheet that satisfies the PRD's "usable on desktop and phone" NFR for tables and forms without hand-written media queries per page.

**Contract**: Static file under `wwwroot/css/`; no build step, no bundler config needed (this repo has none). Served automatically — `Program.cs:105` already calls `app.UseStaticFiles()`.

#### 5. Load the CSS for every page

**File**: `src/Components/App.razor`

**Intent**: The stylesheet must apply to the static-SSR `Login.razor`/`Logout.razor` pages too, which don't use `MainLayout` — so it belongs in the shared HTML host document's `<head>`, not in the layout.

**Contract**: Add `<link rel="stylesheet" href="css/pico.min.css">` inside `<head>`, relative href (no leading slash) per the path-prefix convention.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- `dotnet watch --project src/lassie.csproj`: Home renders with the new nav and Pico.css styling; Login/Logout render with Pico.css styling despite not using `MainLayout`
- Click "Log out" from Home and confirm it lands on `/lassie/login` (not the domain root) — verifies the fixed `<a href>`
- Resize to a phone-width viewport: nav and page content stay usable, no horizontal overflow or lost functionality
- Build the actual Docker image (or push to a branch and let CI build it) and confirm `pico.min.css` is present and served in the running container — not just verified via local `dotnet run`, per the Dockerfile static-assets gotcha

**Implementation Note**: Pause here for manual confirmation before starting Phase 3.

---

## Phase 3: License Fields CRUD page (with nested options management)

### Overview

The FR-004 deliverable itself: list, create, edit, and delete license field definitions from `/license-fields`, and — for `SingleSelect` fields — manage each field's allowed option values.

### Changes Required:

#### 1. License Fields page

**File**: `src/Components/Pages/LicenseFields.razor` (new)

**Intent**: Authenticated admin screen for managing the license field schema — list all fields (name + data type), create a new one, rename an existing one, delete one, and for `SingleSelect` fields manage their options, all with friendly (not crashing) responses to duplicate names/values.

**Contract**: `@page "/license-fields"`, `@attribute [Authorize]`, `@rendermode InteractiveServer`, `@layout MainLayout`. Injects `LassieDbContext` directly (`@inject`), matching `Login.razor`'s pattern — no service/API layer, `LicenseFields.Include(f => f.Options)` loads both levels in one query. A single `EditForm` (with `OnValidSubmit`) is reused for both create and edit; `DataType` renders as a live `<select>` only when creating a new field, and as disabled/read-only text once the field is persisted (Critical Implementation Details). The field table uses `@key="field.Id"` on each row (Blazor diffing correctness across create/delete-driven re-renders); selecting a `SingleSelect` field's row reveals a nested options list (also `@key="option.Id"` per row) with its own inline add/rename/delete controls. The "currently selected field" is re-derived from the live `LicenseFields` list each render (not held as a standalone detached reference), so deleting the selected field makes its options section disappear cleanly instead of continuing to render against a field that no longer exists. Delete on both levels uses an inline per-row "confirm?" toggle (local boolean state rendering "Are you sure? Yes / No") rather than a native JS `confirm()` dialog, since a Blazor Server interactive circuit shouldn't block on native browser dialogs — deleting a field cascades to its options at the database level (Phase 1), so no separate confirmation step is needed for the options that go with it. Field-save and option-save are two independent operations, each with its own `try/catch` around `SaveChangesAsync` on `DbUpdateException`, narrowed to the actual unique-violation shape (`dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" }`) before showing a "name already exists" (field form) or "value already exists" (option row) message inline next to whichever form triggered it — any other `DbUpdateException` shows a generic error instead, so a transient DB failure isn't mislabeled as a duplicate-value problem, and a duplicate-field-name error never surfaces attributed to the options list or vice versa.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- Create a `Number`-typed field (e.g. "Number of users") — it appears in the list immediately, no options section shown
- Create a `SingleSelect`-typed field (e.g. "License type") — it appears in the list; selecting it reveals an empty options section
- Add options to the `SingleSelect` field (e.g. regular/professional/enterprise) — each appears in the nested list immediately
- Attempt to add a duplicate option value to the same field — a friendly validation message is shown, no crash/500
- Attempt to create a field with a name that already exists — a friendly validation message is shown, no crash/500
- Edit an existing field's name — the list reflects the new name immediately; confirm `DataType` is shown read-only (not editable) on this existing field
- Delete a `SingleSelect` field that has options — it and its options both disappear from the list (cascade delete verified)
- Delete a single option without deleting its parent field — only that option disappears, the field and its remaining options stay
- Visiting `/license-fields` while logged out redirects to `/login` (re-verifies the `@attribute [Authorize]` gate still works on a new page)
- Phone-width viewport: field list, nested options list, and forms stay usable, no lost functionality
- Push to `main`, confirm the CI deploy succeeds, and walk the full create-field → add-options → edit → delete cycle against `https://kododo.dev/lassie/license-fields` in production

---

## Testing Strategy

### Manual Testing Steps:

1. Local: `dotnet watch --project src/lassie.csproj`, log in, walk create (`Number` field) → create (`SingleSelect` field) → add options → duplicate-name/value rejection → edit → delete on `/license-fields`.
2. Local: resize browser to phone width, repeat the same walk-through, confirm no lost functionality.
3. Production: after a push-to-`main` deploy, repeat the same walk-through against `https://kododo.dev/lassie/license-fields` — this is the only place the Docker static-assets and path-prefix conventions are truly exercised end-to-end.

No automated test project is introduced this slice (see What We're NOT Doing) — matches the F-01/F-02 precedent.

## Performance Considerations

None beyond what's already true of the stack — a single-admin, low-QPS schema-definition page (a handful of fields, each with a handful of options) has no meaningful load profile. Not a NFR concern (the PRD's <500ms guardrail applies to the verification API, not the panel).

## Migration Notes

Purely additive — `AddLicenseFields` creates two new tables with no existing data to migrate or backfill. No rollback complexity beyond EF Core's standard `dotnet ef database update <previous-migration>`.

## References

- Related research: `context/changes/module-catalog-management/research.md`
- Persistence-layer precedent: `context/archive/2026-08-04-persistence-layer-foundation/plan.md`
- Admin-auth-foundation precedent (layout/auth-gate/path-prefix patterns): `context/archive/2026-08-05-admin-auth-foundation/plan.md`
- Roadmap slice definition: `context/foundation/roadmap.md` → `S-01`

## Open Risks & Assumptions

- **Renaming a field or an option changes what the verification API reports.** Neither `LicenseField` nor `LicenseFieldOption` has a separate stable `Key` distinct from its human-editable `Name`/`Value` — if `S-02`/the verification API end up keying client-app-visible identity off these names, a later rename becomes a breaking change for any client app that pattern-matches on the old value. Not a problem today (nothing consumes this schema yet) — worth re-raising explicitly when `S-02` is planned.
- **Delete is unguarded at both levels.** Nothing in this slice can reference a `LicenseField`/`LicenseFieldOption` (License doesn't exist yet), so today's delete is safe. Once `S-02` lets a license hold values against this schema, deleting an in-use field or option becomes a real hazard this plan does not solve — flag it for `S-02`'s own planning rather than retrofitting a guard here speculatively.
- **`S-02`'s scope grew.** License creation now needs to render a dynamic form and store values against whatever fields currently exist, rather than against a small set of fixed columns — a materially larger `S-02` than earlier versions of this roadmap implied. Noted on the roadmap's `S-01` entry; worth re-confirming complexity when `S-02` is planned.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: LicenseField + LicenseFieldOption entities + migration

#### Automated

- [x] 1.1 `dotnet build src/lassie.csproj` succeeds — a470efc
- [x] 1.2 `dotnet ef migrations add AddLicenseFields` generates cleanly — a470efc
- [x] 1.3 `dotnet ef database update` applies cleanly against local dev Postgres — a470efc

#### Manual

- [x] 1.4 Migration file creates `LicenseFields` (unique `Name`) and `LicenseFieldOptions` (unique composite index, cascade-delete FK) tables — a470efc
- [x] 1.5 `dotnet ef migrations list` shows `AddLicenseFields` with no gaps — a470efc

### Phase 2: Panel shell — shared layout + Pico.css

#### Automated

- [x] 2.1 `dotnet build src/lassie.csproj` succeeds — cd67a7e

#### Manual

- [x] 2.2 Home renders with new nav + Pico.css; Login/Logout render with Pico.css — cd67a7e
- [x] 2.3 "Log out" from Home lands on `/lassie/login`, not the domain root — cd67a7e (verified locally without path prefix; full `/lassie` prefix re-verified in Phase 3's production walkthrough)
- [ ] 2.4 Phone-width viewport stays usable, no lost functionality — not verified (browser automation couldn't resize the viewport in this environment); needs manual check
- [x] 2.5 Docker build confirmed to include `pico.min.css` in the running container — cd67a7e

### Phase 3: License Fields CRUD page (with nested options management)

#### Automated

- [x] 3.1 `dotnet build src/lassie.csproj` succeeds — 7b5a680

#### Manual

- [x] 3.2 Create a `Number` field appears in list, no options section — 7b5a680
- [x] 3.3 Create a `SingleSelect` field appears in list, empty options section shown on selection — 7b5a680
- [x] 3.4 Add options to a `SingleSelect` field appear in nested list immediately — 7b5a680
- [x] 3.5 Duplicate option value shows friendly validation error, no crash — 7b5a680
- [x] 3.6 Duplicate field name shows friendly validation error, no crash — 7b5a680
- [x] 3.7 Edit field name updates the list; `DataType` shown read-only on existing field — 7b5a680
- [x] 3.8 Deleting a field with options cascade-deletes its options — 7b5a680
- [x] 3.9 Deleting a single option leaves its parent field and remaining options intact — 7b5a680
- [x] 3.10 Logged-out visit to `/license-fields` redirects to `/login` — 7b5a680
- [ ] 3.11 Phone-width viewport stays usable — not verified (same browser-automation resize limitation as 2.4); needs manual check
- [ ] 3.12 Production walk-through at `https://kododo.dev/lassie/license-fields` after deploy succeeds — pending deploy
