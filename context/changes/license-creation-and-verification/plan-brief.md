# License Creation and Verification — Plan Brief

> Full plan: `context/changes/license-creation-and-verification/plan.md`
> Research: `context/changes/license-creation-and-verification/research.md`

## What & Why

Roadmap `S-02`, the north star: an admin creates a license (text label + optional expiry date), the
system generates a unique API key shown exactly once, and a client app verifies that license's
validity through a machine-to-machine (M2M) API using that key. This is the smallest end-to-end
slice that proves Lassie's core hypothesis — a working, non-hardware-locked license lifecycle.

## Starting Point

Fully greenfield: no `License` entity, no API-key handling, no second auth scheme anywhere in the
codebase. What already exists and gets reused: `User`/`AuditLog` entity conventions, the
`dotnet ef migrations` → auto-apply-on-startup workflow, the panel shell (`MainLayout`, Pico.css,
cookie auth + `[Authorize]` gate) from the just-reverted `module-catalog-management` change, and
`PasswordHasher<User>` as the only (non-reusable, salted) precedent for secret hashing.

## Desired End State

An admin creates a license in the panel and sees the generated API key exactly once, masked with a
reveal/copy control — never retrievable again. A client app sends that key in a header to a
verification endpoint and gets back `{"valid": true|false}`; a bad/missing key gets `401`; a real
outage gets `5xx` — never confused with `valid: false`.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| API-key lookup | Deterministic SHA-256 digest, unique indexed column | `PasswordHasher<T>`'s salted hash can't support lookup-by-equality; a fast digest is standard for high-entropy secrets | Plan (research flagged, user confirmed) |
| Where the key hash lives | Directly on `License` | User's explicit choice — accepts the audit-leak risk for now, defers it to `S-03` rather than adding a sibling entity today | Plan (user override of research's recommendation) |
| `License` + `IAuditable` | Not implemented in this slice | Creation isn't audited anyway (`Added` state is never snapshotted); `S-03` must decide how to keep the key hash out of `AuditLog.Snapshot` once edits are audited | Plan |
| M2M auth mechanism | Manual header validation in the handler, no new `AuthenticationScheme` | Zero new framework wiring; full control over status codes; matches the app's no-service-layer ethos | Plan (user confirmed) |
| License validity in this slice | Expiry-only, no `IsActive` column | Matches the `S-02`/`S-04` roadmap split; avoids a dead column with no consumer until `S-04` | Plan (user confirmed) |
| Verification response shape | `{"valid": bool}`, no detailed reason | Matches FR-010's explicit MVP scope — no expired-vs-deactivated breakdown | Plan (user confirmed) |
| Reveal-once key UX | Masked by default, separate reveal-toggle and copy-to-clipboard buttons | User's explicit spec | Plan (user override of research's plainer default) |
| License label | Unique (indexed) | User's explicit choice, stricter than the PRD's minimum requirement | Plan (user override of research's plainer default) |
| Test coverage | Manual verification only, no new test project | Matches every prior slice's precedent (`F-01`, `F-02`, reverted `S-01`) | Plan (user confirmed) |

## Scope

**In scope:**
- `License` entity (label, optional expiry, API-key hash) + migration
- API-key generation/hashing utility (`ApiKeyHasher`)
- Panel page to create a license and reveal its key once
- M2M verification endpoint (`GET /api/license/verify`, header-authenticated)

**Out of scope:**
- License list view (`S-05`), edit/audit-history (`S-04`), deactivate/reactivate (`S-04`)
- API-key rotation, rate limiting, automated tests
- Any change to `/weatherforecast` or the deploy health-check target

## Architecture / Approach

Three phases: persistence (entity + key utility + migration) → panel UI (create + reveal-once) →
M2M endpoint + end-to-end manual verification. The key's raw form exists only in the creation
page's in-memory component state between generation and the admin copying it — never persisted,
never in a URL, read by the verification endpoint from a header (never a query string), so it can
never leak into server/proxy access logs.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Data model + key hashing | `License` entity, `ApiKeyHasher`, migration | Getting the hash/lookup design right the first time — schema change later is more expensive |
| 2. Panel creation flow | Create form + reveal-once key UI | Raw key must never touch a URL or survive a page navigation |
| 3. Verification endpoint | M2M API, `<500ms`, unavailable≠invalid | Distinguishing a real outage from an invalid license under exception handling |

**Prerequisites:** `F-01`, `F-02` (both `done`) — no blockers.
**Estimated effort:** ~1 session across 3 phases (small, focused entity + two endpoints).

## Open Risks & Assumptions

- Storing the API-key hash directly on `License` (not a sibling entity) means `S-03` inherits a
  real, already-flagged problem: keeping that hash out of `AuditLog.Snapshot` once `License`
  becomes `IAuditable`. Not a blocker now, but must be resolved before `S-03` starts, not
  discovered mid-implementation there.
- No automated tests means the security-sensitive hash/lookup logic relies on manual verification
  and code review only, per this session's explicit choice.

## Success Criteria (Summary)

- Admin can create a license and see its API key exactly once, with no way to retrieve it again
- A client app can verify a license's validity via the API in under 500ms, using the key
- The API cleanly distinguishes "key invalid/missing" (401), "license invalid" (200, `valid:false`),
  and "service unavailable" (5xx) — never conflating the last two
