---
change_id: module-catalog-management
title: License field schema management
status: implementing
created: 2026-08-06
updated: 2026-08-06
archived_at: null
---

## Notes

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
