---
change_id: module-catalog-management
title: License field schema management
status: parked
created: 2026-08-06
updated: 2026-08-07
archived_at: null
---

## Notes

**2026-08-07 — parked, code reverted.** After p1-p3 shipped (commits `a470efc`, `cd67a7e`,
`7b5a680`), the user decided to back out of the fully-configurable field schema for MVP: it added
real scope (dynamic form rendering, per-license dynamic value storage in `S-02`) that isn't needed
to prove the core hypothesis. MVP license now carries just a name + expiry date (see `prd.md`
FR-004/005/006/010 and `roadmap.md` S-01, both updated same session). `LicenseField`/
`LicenseFieldOption` entities, their migration's tables (dropped via a new forward migration
`RemoveLicenseFields`, not a history rewrite — `AddLicenseFields` was already applied in
production), and the `LicenseFields.razor` CRUD page were removed from the code
(commit — see git log after this note). The panel shell (`MainLayout`, Pico.css, the
`PanelHome.razor` logout-link fix) from `cd67a7e` was kept — it's generic panel infra, not
field-schema-specific, and the next slice (simple license creation) needs it too.

This change is parked, not abandoned: configurable field schema is explicit **nice-to-have,
post-MVP** scope now (see `prd.md` Non-Goals, `roadmap.md` Parked). `research.md` and `plan.md`
below are kept as-is as the historical record of the reverted implementation — re-read them first
if/when this is picked back up, rather than re-researching from scratch.

Reformulated twice on 2026-08-06, before implementation started:

1. First pass: originally scoped as a "module catalog" (admin-managed, multi-select feature flags
   a license could carry). Replaced with a simpler "license type" catalog (regular/professional/
   enterprise, single-select per license).
2. Second pass: the user clarified that type + user-count were only *examples* — the real ask is a
   fully admin-configurable field schema (field name + data type: number/text/single-select, with
   admin-managed options for select fields), not a fixed pair of concepts. This is the current
   scope. Architecture: relational field-definition + field-option entities (not JSONB), data
   *types* are a closed, code-fixed set — only field *names and instances* are dynamic.

Kept the original `module-catalog-management` change-id across both reformulations rather than
renaming/starting fresh (explicit user choice) — the `title` above reflects the real scope; the
slug is a historical artifact. `research.md` and `plan.md` were rewritten in place each time; the
plan-review findings from the first pass (namespace import gap, broken logout link, broad
DbUpdateException catch, missing `@key`) still apply structurally and were carried forward again
into the current plan.

`context/foundation/prd.md` (FR-004/005/006/010, US-01, Business Logic, Access Control, Non-Goals,
Vision) and `context/foundation/roadmap.md` (S-01, S-02, S-03, north star, backlog handoff, Parked)
were updated in the same session to match, across both reformulation passes.
