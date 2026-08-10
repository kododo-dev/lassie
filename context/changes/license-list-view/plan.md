# License List View Implementation Plan

## Overview

Admin lands on `/` and sees every license — label, current status, and expiry — with a link
into each license's edit page, instead of today's placeholder. This is roadmap slice `S-05`
(`context/foundation/roadmap.md`), implementing PRD `FR-012`. Status is computed by a shared
rule reused from the verification API, so the list can never disagree with what a client app
is actually told.

## Current State Analysis

- `PanelHome.razor` (route `/`) is a literal placeholder — "Logged in as X" + a "Log out" link,
  no license data (`src/Components/Pages/PanelHome.razor:1-23`).
- `MainLayout.razor`'s nav already has a `Home` link pointing at `""` (`src/Components/Layout/MainLayout.razor:3`)
  — no nav change is needed once `/` shows the list.
- `License` (`src/Data/Licenses/License.cs`) has `Id`, `Label`, `ExpiresOn` (`DateOnly?`),
  `ApiKeyHash` (`[NotAudited]`) — no status/active flag. Deactivation (`S-04`,
  `license-deactivate-reactivate`) is a separate, not-yet-built roadmap slice, so "current status"
  can only mean Active vs Expired for now.
- The validity rule already exists, inlined once, in the verification endpoint:
  `license.ExpiresOn is null || license.ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)`
  (`src/Program.cs:154`). This is not an arbitrary choice — `context/archive/2026-08-07-license-creation-and-verification/reviews/plan-review.md`
  finding F1 (CRITICAL, fixed) exists specifically because an earlier draft of this exact rule
  had a silent one-day-early expiry bug. No shared helper exists yet; a second hand-copied version
  of this expression in the list view would risk re-introducing that bug class through drift.
- `context/archive/2026-08-08-license-edit-with-audit-history/plan.md` (`S-03`) explicitly deferred
  in-panel navigation to the edit page, stating this slice "will supply the real entry point later"
  — `/licenses/{Id}/edit` is currently reachable by URL only.
- No page anywhere in this codebase renders a list/table of multiple entities today — this is the
  first one. `CreateLicense.razor`/`EditLicense.razor` establish the page-header and
  single-entity-query conventions to follow, but not table markup.
- The app uses Pico.css (`src/wwwroot/css/pico.min.css`), a classless framework — existing pages
  have no custom CSS classes and still render styled, because Pico styles bare HTML elements
  directly. No `app.css`/custom stylesheet exists.
- No test project exists in the repo (per `CLAUDE.md`); prior slices were all verified manually.

### Key Discoveries:

- `src/Program.cs:139-157` — the `/api/license/verify` minimal-API endpoint: authenticates via
  `X-Api-Key` header → hash lookup → the validity expression at line 154 → `{ valid }` JSON.
  No `.RequireAuthorization()` — the handler validates the key itself. This endpoint's behavior
  must not change as part of this plan.
- `src/Components/Pages/EditLicense.razor:100-102` — the existing date-display convention:
  `expires on {date:yyyy-MM-dd}` / omitted when null. The list page's expiry column should match
  this format for consistency.
- `src/Components/Pages/CreateLicense.razor:1-9`, `src/Components/Pages/EditLicense.razor:1-9` —
  the standard panel-page header: `@page`, `[Authorize]`, `@layout MainLayout`,
  `@rendermode InteractiveServer`, `@inject LassieDbContext DbContext`.
- `src/Data/Auditing/AuditLog.cs:1-7` — existing convention for a small enum living in the same
  file as its owning type (`AuditChangeType` above `AuditLog`). `LicenseStatus` should follow the
  same shape, defined in `License.cs`.

## Desired End State

Navigating to `/` while authenticated shows every license sorted by label, each with an
Active/Expired badge and its expiry date (or "No expiry"), and a link to that license's edit
page. With zero licenses, an empty-state message with a link to create one is shown instead.
The verification API's `/api/license/verify` behavior is byte-for-byte unchanged. Verification:

- `dotnet build src/lassie.csproj` succeeds.
- Manual walkthrough (Testing Strategy below) confirms the list's status badges agree with what
  `/api/license/verify` reports for the same licenses, and that the verify endpoint's responses
  are unchanged before/after the refactor.

## What We're NOT Doing

- No search/filtering — explicit PRD non-goal ("Wyszukiwanie/filtrowanie listy licencji").
- No pagination — PRD's `target_scale` is small users/data volume; a full unpaginated list is
  sufficient for MVP.
- No deactivation/reactivation UI or `IsActive` field — that's `S-04`, a separate roadmap slice.
  `LicenseStatus` is intentionally an enum (not a bool) so a future `Deactivated` case can be
  added there without reshaping this list page.
- No "expiring soon" warning state — explicit PRD non-goal ("Powiadomienia o zbliżającym się
  wygaśnięciu licencji"). Status is strictly binary: Active or Expired.
- No changes to `CreateLicense.razor` or `EditLicense.razor` beyond being linked to.
- No `MainLayout.razor` nav changes — `Home` already points at `/`, which now shows the list.
- No new test project — stays consistent with prior slices' manual-verification convention.

## Implementation Approach

Extract the verification endpoint's inline validity expression into a shared `LicenseStatus`
enum + `License.GetStatus()` method (Phase 1), confirm the verify endpoint's observable behavior
is unchanged, then build the list page on top of that shared method (Phase 2) so the list can
never compute a different answer than the API a client app actually calls.

## Critical Implementation Details

### The status rule must stay byte-for-byte equivalent to the reviewed verify-endpoint logic

`License.GetStatus()` must reproduce `Program.cs:154`'s exact comparison — `DateOnly.FromDateTime(DateTime.UtcNow)`
(not `DateTime.Today`, which is local time) compared with `>=` (inclusive: a license expiring
"today" in UTC still reads Active through the whole day). This is not a style preference — an
earlier draft of this exact rule had a silent one-day-early expiry bug, caught and fixed in
`context/archive/2026-08-07-license-creation-and-verification/reviews/plan-review.md` (finding
F1, CRITICAL). Simplifying the comparison during this refactor would silently reintroduce that
bug class.

## Phase 1: Shared LicenseStatus + verify-endpoint refactor

### Overview

Extract the existing, already-reviewed validity rule out of the verify endpoint into a reusable
method on `License`, with no change to the endpoint's observable behavior. This isolates the
refactor of NFR-critical, previously-reviewed code from the new UI work in Phase 2.

### Changes Required:

#### 1. `LicenseStatus` enum + `License.GetStatus()`

**File**: `src/Data/Licenses/License.cs`

**Intent**: Give both the verify endpoint and the new list page a single source of truth for
"is this license currently valid," so they can never silently disagree.

**Contract**:
- Add `public enum LicenseStatus { Active, Expired }` above the `License` class (same file,
  mirroring `AuditLog.cs`'s `AuditChangeType` convention).
- Add an instance method `public LicenseStatus GetStatus()` on `License` that returns
  `LicenseStatus.Active` when `ExpiresOn is null || ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow)`,
  otherwise `LicenseStatus.Expired` — this must be the exact expression currently inlined at
  `Program.cs:154` (see Critical Implementation Details above), not a rewritten equivalent.

#### 2. Verify endpoint calls the shared method

**File**: `src/Program.cs`

**Intent**: Remove the endpoint's inline duplicate of the validity rule now that a shared method
exists, with zero change to the endpoint's response shape or behavior.

**Contract**: Line 154's `var valid = license.ExpiresOn is null || license.ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow);`
becomes `var valid = license.GetStatus() == LicenseStatus.Active;`. Everything else in the
endpoint (header/hash lookup, 401 paths, `{ valid }` response shape) is untouched.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification:

- Call `/api/license/verify` with a valid API key for a license with no expiry → `{"valid":true}`.
- Call with a valid API key for a license expiring in the future → `{"valid":true}`.
- Call with a valid API key for a license that expired in the past → `{"valid":false}`.
- Call with a valid API key for a license expiring exactly "today" (UTC) → still `{"valid":true}`
  (confirms the inclusive boundary survived the refactor).
- Call with a missing or unrecognized API key → still `401 Unauthorized` (unchanged).
- Response still returns comfortably under the 500ms NFR guardrail (quick manual timing check).

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding to Phase 2.

---

## Phase 2: License list page

### Overview

Replace `PanelHome.razor`'s placeholder content with a table of every license — label, status
badge, expiry, edit link — so the admin's landing page is useful and `/licenses/{Id}/edit` finally
has an in-panel entry point.

### Changes Required:

#### 1. License list at `/`

**File**: `src/Components/Pages/PanelHome.razor`

**Intent**: Turn the placeholder home page into the license list — the admin's default landing
view — while keeping the existing "logged in as / log out" line.

**Contract**:
- Same header shape as other panel pages (route stays `/`): `[Authorize]`, `@layout MainLayout`,
  `@rendermode InteractiveServer`, `@inject LassieDbContext DbContext`.
- `OnInitializedAsync` loads `DbContext.Licenses.OrderBy(l => l.Label).ToListAsync()` — read-only,
  no need to keep entities tracked past the query (unlike `EditLicense.razor`'s load-before-mutate
  requirement).
- Empty state: when the query returns no licenses, show a message plus a link to `/licenses/new`
  instead of an empty table.
- Non-empty: render a `<table>` — wrapped in Pico's `<figure>` responsive-table pattern for
  horizontal scroll on small screens, per the PRD's mobile-usability NFR — with columns Label /
  Status / Expiry / edit link. Status cell shows "Active" or "Expired" from `license.GetStatus()`.
  Expiry cell shows `{ExpiresOn:yyyy-MM-dd}` (matching `EditLicense.razor`'s existing date format)
  or "No expiry" when null. Each row's edit link points to `licenses/@license.Id/edit`.
- Keep the existing "Logged in as @userEmail" line and "Log out" link.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build src/lassie.csproj`

#### Manual Verification:

- Log in with zero licenses in the database → empty-state message shown, with a working link to
  `/licenses/new`.
- Create 2-3 licenses covering all three states (no expiry, future expiry, past expiry) → list
  shows all of them sorted alphabetically by label, each with the correct Active/Expired badge and
  correct expiry text, and each badge matches what `/api/license/verify` reports for that same
  license's API key.
- Click a row's edit link → lands on that license's `/licenses/{id}/edit`, pre-filled correctly.
- Resize to a small/mobile viewport (or use device emulation) → table stays usable, scrolls
  horizontally instead of breaking the page layout, no lost functionality.
- Visit `/` while logged out → redirected to `/login`, same as other `[Authorize]` panel pages.
- "Logged in as X" text and "Log out" link still work as before.

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation from the human that the manual testing was successful before
proceeding further.

---

## Testing Strategy

### Manual Testing Steps:

1. Refactor `/api/license/verify` onto the shared `GetStatus()` method — confirm all four
   validity cases (no expiry, future, past, exactly-today) and the two 401 cases are unchanged.
2. Build the list page — confirm empty state, then populated state with a mix of statuses.
3. Confirm each row's status badge agrees with what the verify endpoint reports for that license.
4. Confirm the edit link on each row works and the mobile/small-viewport layout holds up.
5. Confirm the logged-out redirect.

## Performance Considerations

A single unpaginated `SELECT * FROM "Licenses" ORDER BY "Label"` — trivial at the PRD's target
scale (small users/data volume). No index changes needed.

## Migration Notes

None — `LicenseStatus`/`GetStatus()` are pure C# (enum + computed method), no new column or
migration. No schema change anywhere in this plan.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `S-05`
- PRD requirement: `context/foundation/prd.md` → `FR-012`
- Verification endpoint: `src/Program.cs:139-157`
- Day-boundary rationale: `context/archive/2026-08-07-license-creation-and-verification/reviews/plan-review.md` → finding F1
- Deferred entry-point note: `context/archive/2026-08-08-license-edit-with-audit-history/plan.md`
- Pattern followed: `src/Components/Pages/CreateLicense.razor`, `src/Components/Pages/EditLicense.razor`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not
> rename step titles. See `references/progress-format.md`.

### Phase 1: Shared LicenseStatus + verify-endpoint refactor

#### Automated

- [x] 1.1 Build succeeds: `dotnet build src/lassie.csproj` — 62c7fb0

#### Manual

- [x] 1.2 No-expiry license verifies as valid — 62c7fb0
- [x] 1.3 Future-expiry license verifies as valid — 62c7fb0
- [x] 1.4 Past-expiry license verifies as invalid — 62c7fb0
- [x] 1.5 Exactly-today-expiry license still verifies as valid (inclusive boundary preserved) — 62c7fb0
- [x] 1.6 Missing/unrecognized API key still returns 401 — 62c7fb0
- [x] 1.7 Response time still well under the 500ms NFR guardrail — 62c7fb0

### Phase 2: License list page

#### Automated

- [x] 2.1 Build succeeds: `dotnet build src/lassie.csproj` — 92d42e5

#### Manual

- [x] 2.2 Empty state renders with working link to /licenses/new — 92d42e5
- [x] 2.3 Populated list sorts by label and shows correct status/expiry per license — 92d42e5
- [x] 2.4 List status badges agree with /api/license/verify for the same licenses — 92d42e5
- [x] 2.5 Edit link on each row navigates to the correct pre-filled edit page — 92d42e5
- [x] 2.6 Small/mobile viewport keeps the table usable (horizontal scroll, no broken layout) — 92d42e5
- [x] 2.7 Logged-out access to / redirects to /login — 92d42e5
- [x] 2.8 "Logged in as X" / "Log out" still work — 92d42e5
