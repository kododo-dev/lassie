---
date: 2026-08-06T22:05:54+02:00
researcher: Jacek Łapiński
git_commit: d3c092930f0c0104bc27346d24d8afb26be239a6
branch: main
repository: lassie
topic: "License field schema: storage architecture for an admin-configurable set of typed fields"
tags: [research, codebase, license-field-schema, data-model, prd]
status: complete
last_updated: 2026-08-06
last_updated_by: Jacek Łapiński
last_updated_note: "Second supersession — generalized from a single 'license type' catalog to a fully admin-configurable field schema, after the user clarified type+user-count were only examples"
---

# Research: License field schema — storage architecture and scope boundaries

**Date**: 2026-08-06T22:05:54+02:00
**Researcher**: Jacek Łapiński
**Git Commit**: d3c092930f0c0104bc27346d24d8afb26be239a6
**Branch**: main
**Repository**: lassie

> **Supersedes two earlier versions of this document.** First scoped as a "module catalog"
> (multi-select feature flags per license). Simplified to a single-select "license type" catalog.
> Then the user clarified that "license type" and "number of users" were only *examples* of a more
> general need: the admin should be able to define an arbitrary set of typed fields a license
> carries. This document covers that final shape. Prior conclusions about tiered-licensing being a
> Non-Goal, and about not gating client-app features through this mechanism, still hold — see
> Historical Context.

## Research Question

1. What's the right storage architecture in this stack (EF Core 10 + Npgsql/Postgres + Blazor
   Server, no service/API layer) for a set of license fields whose *names and instances* are
   admin-defined at runtime, while their *data types* come from a small fixed set (number, text,
   single-select)?
2. Where's the right scope boundary — what should this slice (`S-01`) actually build vs. defer to
   the license-creation slice (`S-02`, not yet planned)?

## Summary

1. **Relational field-definition/field-option entities, not a JSONB blob.** A `LicenseField`
   entity (`Id`, `Name`, `DataType`) plus a child `LicenseFieldOption` entity (`Id`,
   `LicenseFieldId`, `Value`) for single-select fields. This matches the codebase's only existing
   convention for this kind of data (plain relational POCOs + Fluent API), keeps referential
   integrity at the database level (an FK guarantees a selected option really exists), and stays
   queryable/filterable in ordinary SQL — none of which a JSONB-blob-of-values approach gives for
   free. `AuditLog.Snapshot` is this codebase's only precedent for JSON storage, and it's
   specifically for free-form audit data where schema-on-read is the point — not a fit for data
   that needs validating against a live, admin-editable schema.
2. **`DataType` is a closed, code-fixed enum (`Number`, `Text`, `SingleSelect`); field *names and
   instances* are the dynamic part.** Extending the set of supported primitive types later is a
   code change (new enum case + new value-storage/rendering path), by design — this is a
   deliberate, explicit scope boundary now recorded in the PRD's `Non-Goals`.
3. **`DataType` is immutable once a field is created.** Only `Name` (and, for `SingleSelect`
   fields, the `LicenseFieldOption` list) stay editable. Same reasoning as the earlier
   `Key`-immutability discussion for the (now-superseded) module design: changing a field's data
   type after something depends on it would be silently destructive once `License` (`S-02`) exists.
4. **This slice (`S-01`) builds the schema only — `LicenseField` + `LicenseFieldOption` CRUD.**
   Rendering a dynamic license-creation form and storing per-license field *values* is `S-02`'s
   job, once `License` exists to hold them. `S-01`'s existing precedent (a small reference-data
   CRUD page, following the `User` entity's shape) still applies to `LicenseField`/
   `LicenseFieldOption` — it's a two-level version of the same pattern, not a different one.

## Detailed Findings

### 1. Storage architecture: relational vs. JSONB

- `src/Data/LassieDbContext.cs:16-18` — the only `jsonb`-typed column in the codebase today is
  `AuditLog.Snapshot`, a free-form serialized snapshot of "whatever an entity's prior state was."
  That's an appropriate JSON use case (write-once audit trail, never queried by field, schema
  varies by entity type). A license's field values are the opposite: read/written routinely,
  need to be validated against a live schema, and (per FR-010) need to come back out through an
  API response with predictable shape.
- Every other entity in the codebase (`User`, and the now-superseded `Module`/`LicenseType`
  designs) uses plain POCO properties + Fluent API config in `OnModelCreating` — a relational
  `LicenseField`/`LicenseFieldOption` pair is the same convention, just two levels instead of one.
- A relational `LicenseFieldOption` table gives a real FK for "this license's value for this
  select-type field must be one of its currently-defined options" — a JSONB value has no such
  guarantee without extra application-level validation on every write.

### 2. Data-type extensibility boundary

- `context/foundation/prd.md` → `## Non-Goals` (updated this session): *"Rozszerzanie zestawu
  dostępnych typów danych pola (poza liczba/tekst/wybór jednokrotny) bez zmiany kodu"* is now an
  explicit Non-Goal. This mirrors the same "keep scope closed where it doesn't need to be open"
  discipline already used elsewhere in this PRD (e.g. the Access Control section's flat
  single-admin-role model).
- Three data types (`Number`, `Text`, `SingleSelect`) cover both examples the user gave
  ("liczba użytkowników" → `Number`, "rodzaj licencji" → `SingleSelect`) plus one obviously-useful
  primitive (`Text`) for anything descriptive that doesn't fit the other two — chosen without
  further questioning since it's a low-cost, low-risk addition consistent with "the two examples
  given aren't exhaustive."

### 3. Scope boundary between S-01 and S-02

- `context/foundation/roadmap.md` → `S-01`'s own Risk note (updated this session): license
  creation (`S-02`) needs *at minimum* this schema to exist (even empty) before it can render a
  form — the same "S-01 unblocks S-02's reference data" relationship as every earlier version of
  this slice, just with a richer schema underneath it.
- Storing and returning per-license *values* against this schema (a `LicenseFieldValue`-shaped
  join between `License` and `LicenseField`) is explicitly `S-02`'s concern — `License` doesn't
  exist yet, and building that storage now would mean designing it without the concrete shape of
  `License` in front of us. Flagged as a risk for `S-02`'s own planning, not solved here.

## Code References

- `src/Data/LassieDbContext.cs` — entity registration + Fluent API config convention; still the
  pattern `LicenseField`/`LicenseFieldOption` follow (unchanged from prior research passes)
- `src/Data/Users/User.cs` — only precedent entity; `long` PK, plain POCO
- No `LicenseField`, `LicenseFieldOption`, or `License` entity exists anywhere in `src/` as of this
  commit

## Architecture Insights

The relationship between `S-01` and `S-02` has stayed structurally the same across all three
versions of this change (module catalog → license type → field schema): `S-01` builds a small,
admin-managed reference schema; `S-02` consumes it to shape what a `License` can hold. What changed
each time is only how rich that reference schema is — from "an arbitrary multi-select list," to
"a single fixed category," to "a fully admin-defined set of typed fields." The dependency direction
and the "flat, no hidden hierarchy beyond what's needed" design philosophy from the original
research pass both still hold.

## Historical Context (from prior changes)

- `context/foundation/idea-notes.md:16`, `shape-notes.md:179`, `prd.md` `## Non-Goals` — tiered
  licensing models excluded from the very first idea capture through the locked PRD; still the
  reason a license field's *value* (e.g. "Enterprise") carries no bundled behavior or default
  limits of its own — it's a plain attribute, not a plan/tier definition.
- `context/foundation/lessons.md:5-13` — `IAuditable`/audit-history scoped to start with `License`
  in `S-03`, not this schema-definition slice.
- Earlier versions of this document (superseded) — the module-catalog and license-type-only
  research passes, both folded into the summary above.

## Related Research

- None under `context/archive/**/research.md` touches this topic.

## Open Questions

None outstanding for this data-model question.
