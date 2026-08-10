<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: License Edit With Audit History Implementation Plan

- **Plan**: context/changes/license-edit-with-audit-history/plan.md
- **Scope**: Phase 1 of 1
- **Date**: 2026-08-10
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — ApiKeyHash leaks into AuditLogs.Snapshot

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/Data/LassieDbContext.cs:59 (triggered by src/Data/Licenses/License.cs:5)
- **Detail**: `AddAuditLogEntries()` serializes `entry.OriginalValues.ToObject()` for every `IAuditable` entity — all scalar properties, no projection. Now that `License` implements `IAuditable`, every edit persists `ApiKeyHash` into `AuditLogs.Snapshot` (jsonb). `CLAUDE.md` states the API key "must never be exposed in plaintext... not in the panel UI, not in logs" — `AuditLogs` functions as a log here, and `plan.md` doesn't mention redaction. The stored value is a SHA-256 hash, not the plaintext key, so exploitability is limited, but this is a generic mechanism every future `IAuditable` entity will inherit, so the gap compounds silently.
- **Fix A ⭐ Recommended**: Add a lightweight exclusion mechanism to `AddAuditLogEntries` in `LassieDbContext.cs` (e.g. a per-entity-type ignore-list or a `[NotAudited]` marker attribute checked when building the snapshot) and exclude `License.ApiKeyHash` from the serialized snapshot.
  - Strength: Fixes the leak at the source, in the shared mechanism, protecting every future auditable entity rather than patching `License` alone.
  - Tradeoff: Touches shared audit infrastructure — wider blast radius than a single-file fix, and needs a small design decision on how exclusion is expressed.
  - Confidence: HIGH — the fix is narrowly scoped to snapshot serialization and testable in isolation.
  - Blind spot: Haven't verified whether `AuditLogs` table access is already restricted to the same single admin who can already see the license list — actual exposure risk may already be low in this single-tenant model.
- **Fix B**: Accept as-is for now — `ApiKeyHash` is a SHA-256 hash (not the raw key), and `AuditLogs` is only reachable by the same single admin who already sees the license.
  - Strength: Zero code change, ships now; hash disclosure alone doesn't grant verification-API access.
  - Tradeoff: Leaves `CLAUDE.md`'s "never in logs" constraint technically violated in spirit; the gap silently widens as more entities opt into `IAuditable`.
  - Confidence: MEDIUM — depends on how strictly "logs" is meant to cover internal audit-trail data.
  - Blind spot: Whether a future audit-log viewer UI (explicitly deferred by this plan to "a future slice") would surface this snapshot to a wider audience.
- **Decision**: FIXED via Fix A — added `NotAuditedAttribute` (`src/Data/Auditing/NotAuditedAttribute.cs`), applied to `License.ApiKeyHash`, and `LassieDbContext.AddAuditLogEntries` now builds the snapshot from a filtered property list instead of `OriginalValues.ToObject()`. Verified live: a saved edit's `AuditLogs.Snapshot` now reads `{"Id": 5, "Label": "redaction-verify", "ExpiresOn": null}` — no `ApiKeyHash` key.

### F2 — OnInitializedAsync-only load risks a stale License on Id-only navigation

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/Components/Pages/EditLicense.razor:61-72
- **Detail**: The `license` field loads only in `OnInitializedAsync`, keyed off the `Id` route parameter. Blazor's router reuses the existing component instance (not a fresh one) when navigating between two URLs that resolve to the same page type with only the route parameter changing — `OnInitializedAsync` does not re-run in that case, only `OnParametersSet(Async)` does. If a future in-app navigation goes directly from `/licenses/1/edit` to `/licenses/2/edit` without a full page reload (e.g. once `S-05`'s license list adds links between edit pages), the component keeps showing/editing the stale `license` for the old `Id` while the URL and route parameter show the new one — a real risk of editing the wrong license. No current caller triggers this (no license list page exists yet; this slice is URL-only reachable), so it's latent rather than active today.
- **Fix A ⭐ Recommended**: Override `OnParametersSetAsync` to detect an `Id` change and re-run the load-before-mutate query, so future navigation between two edit URLs can't operate on a stale license instance.
  - Strength: Closes the correctness/audit-integrity gap before `S-05` (license list) introduces direct links between edit pages, matching the plan's own load-before-mutate contract.
  - Tradeoff: A few extra lines of lifecycle-handling code in a page the plan describes as URL-only reachable for this slice.
  - Confidence: HIGH — standard, well-documented Blazor pattern for parameterized routable pages.
  - Blind spot: No current caller exercises this path, so the fix can't be manually verified against a live navigation scenario until `S-05` exists.
- **Fix B**: Leave as-is and record as a known limitation for `S-05` to handle when the license list page introduces in-app links between edit pages.
  - Strength: No code change now; scope stays minimal per "What We're NOT Doing" (URL-only reachability).
  - Tradeoff: The risk of a silent wrong-license edit is deferred, not eliminated, and could be forgotten by the time `S-05` ships.
  - Confidence: MEDIUM — relies on `S-05`'s implementer remembering this note.
  - Blind spot: Haven't checked whether `S-05` (not yet planned) will account for this by design.
- **Decision**: FIXED via Fix A — replaced `OnInitializedAsync` with `OnParametersSetAsync` in `EditLicense.razor`, reloading `license` (and clearing error/success messages) whenever `license is null || license.Id != Id`. Build verified green.

### F3 — No detach after successful save (unlike CreateLicense)

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/Components/Pages/EditLicense.razor:86 (vs. CreateLicense.razor:85)
- **Detail**: `CreateLicense.razor` explicitly detaches the entity after `SaveChangesAsync`, with a comment noting the long-lived per-circuit `DbContext` would otherwise accumulate tracked entities. `EditLicense.razor` never detaches `license` after a successful save. Low practical impact for a single row per page visit, but it diverges from the sibling pattern this page otherwise mirrors closely.
- **Fix**: Detach `license` from the change tracker after a successful `SaveChangesAsync()`, mirroring `CreateLicense.razor`'s post-save detach and its documented rationale.
- **Decision**: FIXED, but not as literally proposed — the literal fix (detach right after every save) would have regressed the multi-edit-in-one-visit flow just manually verified in 1.3-1.6: a detached entity stops being tracked, so a second save attempt on the same page instance would silently no-op (no DB write, no audit row) instead of erroring. `CreateLicense.razor` can detach post-save because its page reaches a terminal "created" state per submission; `EditLicense.razor` must stay editable against the same license across repeat saves. Applied the equivalent fix instead: `EditLicense.razor`'s `OnParametersSetAsync` now detaches the *previous* `license` right before loading a new one when `Id` changes, bounding change-tracker growth to one tracked license at a time without breaking same-license repeat saves. Build verified green.
