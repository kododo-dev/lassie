# License Field Schema Management — Plan Brief

> Full plan: `context/changes/module-catalog-management/plan.md`
> Research: `context/changes/module-catalog-management/research.md`
> Revision note: this change was originally a "module catalog" (multi-select feature flags), then a single-select "license type" catalog. Rewritten again 2026-08-06 into a fully admin-configurable field schema after the user clarified type+user-count were only examples. Change-id kept unchanged — see `change.md` Notes.

## What & Why

Implements FR-004 (roadmap `S-01`): the admin can define the set of fields a license carries — field name + data type (number/text/single-select), and for select-type fields, their allowed option values. This is the reference schema `S-02` (license creation, the roadmap's north star) will render a dynamic form against and store per-license values for — `S-01` exists so `S-02` isn't blocked on a schema to build against.

## Starting Point

Persistence (`F-01`) and admin auth (`F-02`) are both done and live in production. Beyond auth, the panel has exactly one page (`PanelHome.razor`) and no shared layout, no CSS, and no domain entities yet — this is the first slice to add a "real" CRUD feature, and the first with a two-level data model (fields, each optionally owning a set of options).

## Desired End State

A logged-in admin visits `/license-fields`, sees the current field schema as a list, and can create/rename/delete a field — with a friendly error instead of a crash on a duplicate name. For `SingleSelect` fields, the admin can also add/rename/delete that field's option values inline. The panel also gains its first shared nav shell and its first styling, both reused by every panel page from here on.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Concept | Fully admin-configurable field schema, not a fixed pair of concepts | User clarified type+user-count were only examples of a general need. | User (this session) |
| Storage architecture | Relational `LicenseField` + `LicenseFieldOption`, not JSONB | Matches the codebase's only precedent for structured (non-audit) data; keeps FK integrity for option values. | Research |
| Data-type set | Closed, code-fixed: Number / Text / SingleSelect | Field *names/instances* are dynamic; the primitive types available are not — a deliberate scope boundary. | Research |
| DataType mutability | Immutable after field creation | Changing a field's type after something depends on it would be silently destructive once `License` (S-02) exists. | Research |
| Delete scope | In scope at both levels, unguarded; field delete cascades to its options | Nothing references this schema yet (License doesn't exist), so it's safe today. | Plan (carried from prior Q&A) |
| Shared layout | Introduce `MainLayout` now | Every future panel slice (S-02–S-05) needs nav; cheaper to build once here. | Plan (carried from prior Q&A) |
| Styling | Pico.css, vendored locally | Satisfies the PRD's responsive NFR; no CDN runtime dependency. | Plan (carried from prior Q&A) |
| Testing | Manual verification only | Matches the F-01/F-02 precedent. | Plan (carried from prior Q&A) |

## Scope

**In scope:**
- `LicenseField` (`Id`, `Name`, `DataType`) + `LicenseFieldOption` (`Id`, `LicenseFieldId`, `Value`) entities + migration, unique indexes, cascade delete
- Shared `MainLayout.razor` + nav (Home, License Fields)
- Vendored Pico.css, loaded for every page including static-SSR Login/Logout
- `/license-fields` page: list/create/edit/delete fields; nested list/create/edit/delete of options for `SingleSelect` fields
- Duplicate-name/value handling as friendly form errors
- Opportunistic fix: `PanelHome.razor`'s pre-existing broken `<a href="/logout">` (leading-slash bug, missed by an earlier fix commit)

**Out of scope:**
- Storing or editing per-license field *values* — that's `S-02`'s job once `License` exists
- Any data type beyond Number/Text/SingleSelect
- Changing a field's DataType after creation
- Audit history on this schema (reserved for License in `S-03`)
- FK-guarded delete (no entity references this schema yet)
- Minimum-option-count validation on SingleSelect fields
- Search/filter on the field list
- Automated tests / new test project
- Any change to `License` or other roadmap slices

## Architecture / Approach

Direct `LassieDbContext` injection from `LicenseFields.razor`'s `@code` block — no service or API layer, matching the only existing precedent (`Login.razor`). One master-detail page rather than two separate pages: field list on top, a selected `SingleSelect` field's options revealed inline beneath it. Three phases: data layer → panel UI shell (layout + CSS, reusable by all future slices) → the feature itself on top of that shell.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. LicenseField + LicenseFieldOption + migration | Two-table schema with unique indexes + cascade-delete FK | Low — directly extends the established `User` entity + migration convention to a second level |
| 2. Panel shell | Shared `MainLayout` + vendored Pico.css + fixed logout link | `@layout` namespace resolution needs an explicit `_Imports.razor` update, or it won't compile; Docker static-web-assets gotcha is live for the first time |
| 3. License Fields CRUD page | `/license-fields` list/create/edit/delete, nested options management | Single form must render `DataType` as live-editable only at creation, read-only after — a UI-state nuance, not just a missing field |

**Prerequisites:** `F-01`, `F-02` (both done, live in production).
**Estimated effort:** slightly more than F-01/F-02's pace, given the two-level data model and nested UI in Phase 3.

## Open Risks & Assumptions

- Renaming a field or option later could break any client app that ends up pattern-matching on `Name`/`Value` via the verification API — worth re-raising when `S-02` is planned.
- Delete has no guard against in-use fields/options — safe today, but `S-02`'s own plan needs to address this once licenses can hold values against this schema.
- `S-02`'s scope grew as a side effect of this change: it now needs to render a dynamic form and store values per the current schema, rather than against a small set of fixed columns — flagged on the roadmap, worth re-confirming complexity when `S-02` is planned.

## Success Criteria (Summary)

- Admin can define a `Number` field and a `SingleSelect` field (with options), edit and delete both, including duplicate-name/value attempts showing friendly errors
- The panel works and looks reasonable on both desktop and phone-width viewports
- The feature is verified against the real production deploy (`https://kododo.dev/lassie/license-fields`), not just local `dotnet run`
