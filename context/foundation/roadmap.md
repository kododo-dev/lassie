---
project: Lassie
version: 1
status: draft
created: 2026-08-04
updated: 2026-08-31
prd_version: 1
main_goal: quality
top_blocker: capacity
---

# Roadmap: Lassie

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

A company that ships its own product to many customer deployments has no centralized way to manage those licenses today — granting access and configuring license parameters (e.g. type, user count) is all manual. The tool currently in use (Intellilock) binds a license to a physical machine, which breaks down in cloud environments where the machine under a deployment changes over time. Customer deployments are distributed and not always online, so license verification has to tolerate periodic — not continuous — connectivity, without falling back to hardware-locking.

## North star

**S-02: Admin creates a license (name + optional expiry) with a generated API key, and a client app can verify that license's status through the API** — this is the smallest end-to-end flow that proves Lassie's core hypothesis (a working, non-hardware-locked license lifecycle), and it maps directly to both primary Success Criteria in the PRD.

> Revised 2026-08-07: originally gated on `S-01` (admin-configurable license field schema) so license creation could render a dynamic field-values form. The user backed out of that scope for MVP — configurable fields are now nice-to-have, post-MVP (see `S-01` below and `## Parked`). `S-02` no longer depends on `S-01`; it's now unblocked by `F-01`/`F-02` alone, both already `done`.

> A reader-facing note on what "north star" means here: it's the smallest end-to-end slice whose successful delivery proves the core product hypothesis — placed as early as its Prerequisites allow, because every other slice only matters if this one works. This gloss is stated once, here; it isn't repeated later in this document.

**Why this wasn't split further:** the PRD's own `US-01` already frames license-creation and client-app-verification as a single acceptance-tested story — a license created but never machine-verified (or a verification endpoint with no way to create a license) proves nothing on its own. Splitting them into two slices would produce two halves that are each individually unverifiable against the PRD's own acceptance criteria, so they're kept as one vertical slice here even though it touches more FRs than its siblings.

## At a glance

| ID   | Change ID                          | Outcome (user can …)                                                                 | Prerequisites | PRD refs                          | Status   |
| ---- | ----------------------------------- | -------------------------------------------------------------------------------------- | -------------- | ---------------------------------- | -------- |
| F-01 | `persistence-layer-foundation`      | (foundation) DB connectivity + migration tooling verified end-to-end                   | —              | FR-006 (enabler), Access Control   | done     |
| F-02 | `admin-auth-foundation`             | (foundation) Admin can authenticate to the panel; unauthenticated requests are rejected | F-01           | FR-011, Access Control             | done     |
| S-01 | `module-catalog-management`         | *(parked, nice-to-have post-MVP)* Admin can define license fields (name + data type) and their options | F-01, F-02     | FR-004                             | parked |
| S-02 | `license-creation-and-verification` | Admin creates a license (name + optional expiry) + API key; client app verifies it via the API | F-01, F-02 | FR-005, FR-008, FR-009, FR-010, US-01 | done |
| S-03 | `license-edit-with-audit-history`   | Admin edits a license, with prior versions retained for audit                          | S-02, F-01, F-02 | FR-006                             | done |
| S-04 | `license-deactivate-reactivate`     | Admin deactivates a license and later reactivates it                                   | S-02, F-01, F-02 | FR-007                             | done |
| S-05 | `license-list-view`                 | Admin views the list of licenses and their current status                              | S-02, F-01, F-02 | FR-012                             | done |
| S-06 | `admin-panel-ui-refresh`            | Admin uses a panel that's visually polished and pleasant, not just functional — every screen shipped so far (login, list, create/edit, audit history) | F-02, S-02, S-03, S-05 | NFR (panel usability/readability) | done |
| S-07 | `license-verification-audit-log`     | Admin opens a license and sees its full verification history — every API check with timestamp, caller IP, and call parameters/result | S-02, F-01, F-02, S-05 | scope addition beyond PRD v1 (relates FR-009, FR-010, FR-006) | done |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                    | Chain                          | Note                                                                                   |
| ------ | ------------------------- | ------------------------------- | --------------------------------------------------------------------------------------- |
| A      | Foundations & north star  | `F-01` → `F-02` → `S-02`        | Mandatory path to the north star. `F-01`/`F-02` are both `done` — `S-02` is unblocked, ready for `/10x-plan`. `S-01` no longer sits on this chain — see `## Parked`. |
| B      | Audit & correction        | `S-03`                          | Joins Stream A at `S-02`. Sequenced first among the three post-launch branches — `quality` goal prioritizes protecting FR-006's audit guarantee as soon as licenses can be edited. |
| C      | Lifecycle control         | `S-04`                          | Joins Stream A at `S-02`. Parallel with Streams B and D — no shared prerequisites beyond `S-02`. |
| D      | Visibility                | `S-05`                          | Joins Stream A at `S-02`. Parallel with Streams B and C; lowest risk of the three (read-only). |
| E      | Polish & UX                | `S-02, S-03, S-05` → `S-06`     | Cross-cutting — redesigns every panel screen shipped by Streams A/B/D at once, so it's sequenced after they exist rather than joining at a single point. Independent of `S-04` (Stream C); can land before or after it. |
| F      | Verification audit         | `S-02, S-05` → `S-07`          | Joins Stream A at `S-02` (the API being audited) and leans on `S-05` for the panel surface to hang the history view off. First net-new slice after the MVP set closed — post-MVP scope addition (2026-08-31). Independent of Stream C/E. |

## Baseline

What's already in place in the codebase as of `2026-08-04` (auto-researched + user-confirmed).
Foundations below assume these are present and do NOT re-scaffold them.

- **Frontend:** absent — no UI framework/project exists; `src/lassie.csproj` is Microsoft.NET.Sdk.Web with only `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` packages (API-only scaffold). Admin-panel UI technology has not yet been chosen — deferred to the first slice that needs it, per progressive disclosure.
- **Backend / API:** partial — ASP.NET Core (net10.0) webapi scaffold runs with only the default `/weatherforecast` minimal-API sample (`src/Program.cs`); no domain routes yet.
- **Data:** absent — no EF Core or DB driver package referenced, no `DbContext`, no migrations.
- **Auth:** absent — no identity package, no login endpoint or middleware; FR-011 unimplemented.
- **Deploy / infra:** present — self-hosted on the existing VPS via Docker Compose, GitHub Actions CI (build → GHCR → SSH deploy → health check), live and verified at `https://kododo.dev/lassie` (`context/foundation/infrastructure.md`, `context/deployment/deploy-plan.md`, `Dockerfile`, `deploy/docker-compose.yml`, `.github/workflows/deploy.yml`).
- **Observability:** absent — only default ASP.NET Core console logging; no error tracking, metrics, or dashboards.

## Foundations

### F-01: Persistence layer wired

- **Outcome:** (foundation) EF Core is connected to the already-deployed Postgres instance (database `lassie`, created on the shared VPS per `deploy-plan.md`); one migration has been created and applied end-to-end. No domain entities modeled yet — that's each consuming slice's job.
- **Change ID:** `persistence-layer-foundation`
- **PRD refs:** FR-006 (audit-history requirement — the reason this is stood up as a deliberate pattern rather than improvised later), Access Control section (backing store for admin identity)
- **Unlocks:** S-01, S-02, S-03, S-04, S-05 (every slice below persists something), and F-02 (auth needs a backing store)
- **Prerequisites:** — (the target database already exists on the VPS; nothing else blocks starting this)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Getting the audit-history-friendly persistence pattern (FR-006: edits must retain history, never destructively overwrite) decided once, here, is cheaper than retrofitting it after S-01/S-02 have already been built against a naive overwrite assumption. `main_goal: quality` weighs this sequencing.
- **Status:** done

### F-02: Admin authentication foundation

- **Outcome:** (foundation) An admin can log in with email + password; requests to panel actions without a valid session are rejected. No role distinction (matches PRD's flat single-role model).
- **Change ID:** `admin-auth-foundation`
- **PRD refs:** FR-011, Access Control section
- **Unlocks:** S-01, S-02, S-03, S-04, S-05 (every panel action requires being logged in first)
- **Prerequisites:** F-01 (a persisted admin identity is the most likely backing store for login)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - How is the very first admin account provisioned? No FR covers admin-account creation or self-registration, and password reset is explicitly out of scope (`## Non-Goals`) — implying a seeded/manually-provisioned single account, but this isn't stated outright in the PRD. — Owner: user. Block: no (a sensible default — seed via migration/config — is available; naming this here just prevents it from being silently invented deep in implementation without anyone noticing).
- **Risk:** Sequenced right after F-01 and before every panel slice, so no panel UI gets built against an unauthenticated stub that later needs retrofitting.
- **Status:** done

## Slices

### S-01: License field schema management *(parked, post-MVP nice-to-have)*

- **Outcome:** Admin can define the set of fields a license carries — field name + data type (number / text / single-select), and for single-select fields, the list of allowed options. Ships with an example starting schema (e.g. a "License type" select field with regular/professional/enterprise options, a "Number of users" number field), but the schema itself is fully admin-managed, not fixed.
- **Change ID:** `module-catalog-management` (kept as-is across two reformulations — see `context/changes/module-catalog-management/change.md` Notes for why)
- **PRD refs:** FR-004
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** N/A while parked.
- **Status:** parked

> Revised 2026-08-07 (third iteration): p1-p3 were actually implemented (`LicenseField`/`LicenseFieldOption` entities, migration, panel CRUD page — commits `a470efc`, `cd67a7e`, `7b5a680`), then the user backed out of full configurability for MVP — it added real scope to `S-02` (dynamic form rendering, dynamic per-license value storage) that isn't needed to prove the core hypothesis. Code reverted 2026-08-07 (entities/migration/CRUD page removed via a new forward `RemoveLicenseFields` migration; panel shell from `cd67a7e` kept — generic infra, not field-schema-specific). This slice is parked, not cancelled: `research.md`/`plan.md` in `context/changes/module-catalog-management/` stay as the ready-made starting point for when configurable fields are picked up post-MVP. See `context/changes/module-catalog-management/change.md` Notes for the full account.
>
> Previously revised 2026-08-06 (second iteration): first scoped as a multi-select "module" catalog (arbitrary feature flags per license), then simplified to a single-select "license type" categorization, then generalized into a fully admin-configurable field schema. See `context/changes/module-catalog-management/research.md` for the architecture (relational field-definition + field-option entities, not JSONB) and the closed set of supported data types (number/text/single-select, fixed in code).

### S-02: License creation and client-app verification (north star)

- **Outcome:** Admin creates a license — text label, optional expiry date — and the system generates a unique API key; a client app using that key gets back the license's validity from the verification API.
- **Change ID:** `license-creation-and-verification`
- **PRD refs:** FR-005, FR-008, FR-009, FR-010, US-01
- **Prerequisites:** F-01, F-02 (both `done` — this slice is unblocked)
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** This slice is what every client app depends on continuously once deployed, so the NFRs that matter most here — the API key never appearing in plaintext after generation, the <500ms response guardrail, and distinguishing "service unavailable" from "license invalid" (a network hiccup must never read as revocation) — deserve more scrutiny here than anywhere else on the roadmap. `main_goal: quality` weighs this.
- **Status:** done

### S-03: License edit with audit history

- **Outcome:** Admin edits a license's name or expiry date, and every prior version remains available for audit — no destructive overwrite.
- **Change ID:** `license-edit-with-audit-history`
- **PRD refs:** FR-006
- **Prerequisites:** S-02, F-01, F-02
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Retrofitting audit history onto an edit path that already shipped without it is riskier than building it in from this feature's first version — sequenced right after the north star so no license has ever been edited without a history record. Prioritized ahead of S-04/S-05 among the three parallel branches per `main_goal: quality`.
- **Status:** done

### S-04: License deactivate / reactivate

- **Outcome:** Admin deactivates a license and can later reactivate it — deactivation is a reversible state, not permanent.
- **Change ID:** `license-deactivate-reactivate`
- **PRD refs:** FR-007
- **Prerequisites:** S-02, F-01, F-02
- **Parallel with:** S-03, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Independent of S-03/S-05 — a status-flag toggle on an entity that already exists after S-02. Low risk; no shared state with the other two parallel branches.
- **Status:** done

### S-05: License list view

- **Outcome:** Admin views the list of licenses and each one's current status.
- **Change ID:** `license-list-view`
- **PRD refs:** FR-012
- **Prerequisites:** S-02, F-01, F-02
- **Parallel with:** S-03, S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Read-only surface; lowest risk of the three parallel branches, needs only S-02's data to exist.
- **Status:** done

### S-06: Admin panel UI refresh

- **Outcome:** The admin panel looks and feels intentional — clean layout, consistent spacing/typography, clear visual hierarchy — instead of the current bare-bones styling. Covers every screen shipped so far: login (F-02), license create/edit (S-02/S-03), audit history (S-03), and the license list (S-05). Deactivate/reactivate (S-04) picks up the same design once it ships, whether S-06 lands before or after it.
- **Change ID:** `admin-panel-ui-refresh`
- **PRD refs:** Non-Functional Requirements — "Panel administracyjny jest użyteczny i czytelny zarówno na ekranie desktopowym, jak i na małym ekranie (smartfon) — responsywny, bez utraty funkcjonalności" and "Panel administracyjny jest użyteczny na dwóch najnowszych wersjach głównych przeglądarek". Note: those NFRs mandate usable/responsive/legible, not "elegant" — this slice goes beyond the letter of the PRD on the user's explicit request (2026-08-11), in the spirit of the same NFR.
- **Prerequisites:** F-02, S-02, S-03, S-05 (the screens being redesigned must exist first — all four are `done`, so this slice is unblocked)
- **Parallel with:** S-04 (independent — a state-toggle feature vs. a styling pass; either order works, but landing S-06 after S-04 avoids re-touching S-04's markup twice)
- **Blockers:** —
- **Unknowns:**
  - No visual-design direction (colors, typography, component library) has been chosen yet — currently the panel is unstyled/minimal (per `## Baseline`, no CSS framework picked). — Owner: user. Block: no (a sensible lightweight default — e.g. a small CSS framework or component kit appropriate to the panel's tech stack — can be proposed at `/10x-plan` time; naming the gap here just prevents it from being silently invented deep in implementation).
- **Risk:** Low functional risk (pure presentation layer, no data/behavior change), but touches every existing screen — worth a visual pass-through of the whole panel after implementation rather than screen-by-screen sign-off, to catch inconsistencies between screens.
- **Status:** done

### S-07: License verification audit log

- **Outcome:** Every call to the verification API is recorded as an audit entry — timestamp, the resolved license, the caller's IP address, and the call parameters/outcome (validity result returned, auth outcome: valid / invalid / missing key). The admin can open any license and view its chronological verification history alongside the existing edit-audit history.
- **Change ID:** `license-verification-audit-log`
- **PRD refs:** none directly — scope addition beyond PRD v1 on the user's explicit request (2026-08-31), analogous to how S-06 went past the letter of the PRD. Related: FR-009 / FR-010 (the verification API being audited), FR-006 (the append-only edit-audit pattern this mirrors), and the FR-009 Socrates note (installation-identifier / sharing-detection foundation, deferred).
- **Prerequisites:** S-02 (the verification API and the license entity must exist), F-01 (persistence), F-02 (panel auth), S-05 (a license surface in the panel to hang the history view off). All four are `done` — this slice is unblocked.
- **Parallel with:** — (the only open slice)
- **Blockers:** —
- **Unknowns:**
  - Boundary against the Non-Goal "Zaawansowana telemetria i analityka wykorzystania licencji" (`prd.md` → `## Non-Goals`, Niefunkcjonalne). This slice is a raw, append-only per-call audit trail viewable per license — not aggregated analytics, dashboards, or usage trends. Confirm that framing holds at `/10x-plan` time so the scope doesn't drift into the Non-Goal. — Owner: user. Block: no.
  - Retention / volume. Every client deployment polls verification periodically, so the audit table grows without bound. Keep everything, or a retention window (last N days / last N calls per license)? — Owner: user. Block: no (keep-everything is a safe MVP default at the PRD's `target_scale` of low QPS / small data volume; naming it here so it isn't silently decided in implementation).
  - Which call parameters beyond IP to capture — candidates: User-Agent, request timestamp, the validity result returned, the auth outcome. The API carries no installation identifier today (FR-009 Socrates note deferred that), so IP + User-Agent are the only caller signals available. — Owner: user. Block: no.
- **Risk:**
  - The < 500ms verification guardrail (Success Criteria) and the "distinguish service-unavailable from license-invalid" NFR both bind here: writing the audit row must not add latency to, or be able to fail, the verification response. Fire-and-forget / asynchronous write; an audit-write failure must never turn a valid license into an error for the caller.
  - The "API key never in plaintext, not even in logs the operator can see" NFR applies directly — the audit entry stores the resolved license identity, never the API key (raw or reconstructable).
  - Append-only, like the FR-006 edit history — audit rows are never editable or deletable from the panel UI (retention pruning, if adopted, is a separate mechanism, not an admin action).
  - This is the first foundation stone toward the target-state "unauthorized license-sharing detection" goal (FR-009 Socrates note, Non-Goals). Keep the schema shape (per-call rows, IP, timestamp) friendly to that later use without building any detection logic now.
- **Status:** done

## Backlog Handoff

| Roadmap ID | Change ID                          | Suggested issue title                                    | Ready for `/10x-plan` | Notes                                   |
| ---------- | ------------------------------------ | ---------------------------------------------------------- | ---------------------- | ----------------------------------------- |
| F-01       | `persistence-layer-foundation`       | Wire EF Core + migrations against the deployed Postgres DB | yes                    | Nothing blocks starting this today       |
| F-02       | `admin-auth-foundation`              | Admin email/password login for the panel                   | no                      | Waiting on F-01                           |
| S-01       | `module-catalog-management`          | Admin can define license fields and their options           | no                      | **Parked** — post-MVP nice-to-have, not blocking S-02 anymore |
| S-02       | `license-creation-and-verification`  | License creation + client-app verification API (north star) | **yes**               | Unblocked — F-01, F-02 both done; this is the north star |
| S-03       | `license-edit-with-audit-history`    | License edit with audit-history retention                  | no                      | Waiting on S-02                           |
| S-04       | `license-deactivate-reactivate`      | License deactivate / reactivate                            | no                      | Waiting on S-02; parallel with S-03, S-05 |
| S-05       | `license-list-view`                  | License list view with status                              | no                      | Waiting on S-02; parallel with S-03, S-04 |
| S-06       | `admin-panel-ui-refresh`             | Polished, elegant visual redesign of the admin panel        | **yes**                | Unblocked — F-02, S-02, S-03, S-05 all done; parallel with S-04 |
| S-07       | `license-verification-audit-log`     | Per-license verification audit log (timestamp, caller IP, call params/result) | **yes**  | Unblocked — S-02, F-01, F-02, S-05 all done. Post-MVP scope addition (2026-08-31); confirm the Non-Goal analytics boundary at plan time |

## Open Roadmap Questions

None spanning more than one slice. The PRD closed with zero open questions (`prd.md` → `## Open Questions`: "Brak nierozwiązanych kwestii"), and the Step 5 interview didn't surface a new question spanning more than one slice. The one real gap found (how the first admin account gets provisioned) is narrow enough to live as a non-blocking Unknown on F-02 rather than here.

> Note (2026-08-31): `S-07` (license verification audit log) is a post-MVP scope addition on the user's explicit request, with no backing FR in PRD v1. Its open decisions (retention/volume policy, exact captured parameters, boundary against the "advanced telemetry/analytics" Non-Goal) are captured as non-blocking Unknowns on the slice itself. If `S-07` is picked up, the PRD should get a matching FR (or an explicit note that it extends beyond v1) before or during `/10x-plan`.

## Parked

Lifted from PRD `## Non-Goals` — MVP scope was already deliberately trimmed during shaping, so nothing new was added here during roadmap generation.

- **License field schema management (`S-01`, `module-catalog-management`)** — Why parked: admin-configurable field schema (name + data type, options) added real scope to `S-02` (dynamic form rendering, dynamic per-license value storage) not needed to prove the core hypothesis. Parked 2026-08-07 after p1-p3 were implemented and reverted — nice-to-have, post-MVP. `research.md`/`plan.md` in `context/changes/module-catalog-management/` are the ready-made starting point when this is picked back up.
- **Payment/invoicing integration** — Why parked: handled outside Lassie entirely (PRD §Non-Goals).
- **Hardware-locking / offline crypto** — Why parked: a deliberate departure from the previous tool (Intellilock), which breaks down in cloud environments.
- **Self-service portal for end customers** — Why parked: in MVP, only the supplier-side admin manages licenses.
- **Multi-tenant support (other companies as Lassie customers)** — Why parked: MVP serves one company only; multi-tenant is the target beyond MVP.
- **Grouping licenses into folders** — Why parked: a license is a flat, standalone unit in MVP.
- **Advanced licensing models (tiered subscriptions, auto-expiring trials, floating/shared licenses)** — Why parked: out of MVP scope; the admin-configurable license field schema (S-01) is a flat set of descriptive attributes, not a subscription/billing system.
- **Extending the set of supported license-field data types beyond number/text/single-select without a code change** — Why parked: out of MVP scope; field *names and instances* are admin-configurable (S-01), the closed set of data *types* is not.
- **Expiry-approaching notifications** — Why parked: out of MVP scope.
- **Unauthorized license-sharing detection** — Why parked: a target-state goal, not MVP.
- **API key rotation/regeneration without creating a new license** — Why parked: out of MVP scope.
- **Admin password reset / account recovery** — Why parked: out of MVP scope; recovery is manual.
- **License list search/filtering** — Why parked: a simple list is enough for MVP.
- **Multi-language / white-labeling of the admin panel** — Why parked: out of MVP scope.
- **Advanced telemetry/analytics on license usage** — Why parked: out of MVP scope.

## Done

- **F-01: (foundation) DB connectivity + migration tooling verified end-to-end** — Archived 2026-08-05 → `context/archive/2026-08-04-persistence-layer-foundation/`. Lesson: —.
- **F-02: (foundation) An admin can log in with email + password; requests to panel actions without a valid session are rejected. No role distinction (matches PRD's flat single-role model).** — Archived 2026-08-05 → `context/archive/2026-08-05-admin-auth-foundation/`. Lesson: ASP.NET Core Data Protection keys aren't persisted across container restarts (see `context/foundation/lessons.md`).
- **S-02: Admin creates a license — text label, optional expiry date — and the system generates a unique API key; a client app using that key gets back the license's validity from the verification API.** — Archived 2026-08-08 → `context/archive/2026-08-07-license-creation-and-verification/`. Lesson: —.
- **S-03: Admin edits a license's name or expiry date, and every prior version remains available for audit — no destructive overwrite.** — Archived 2026-08-10 → `context/archive/2026-08-08-license-edit-with-audit-history/`. Lesson: —.
- **S-05: Admin views the list of licenses and each one's current status.** — Archived 2026-08-10 → `context/archive/2026-08-10-license-list-view/`. Lesson: —.
- **S-06: The admin panel looks and feels intentional — clean layout, consistent spacing/typography, clear visual hierarchy — instead of the current bare-bones styling. Covers every screen shipped so far: login (F-02), license create/edit (S-02/S-03), audit history (S-03), and the license list (S-05). Deactivate/reactivate (S-04) picks up the same design once it ships, whether S-06 lands before or after it.** — Archived 2026-08-11 → `context/archive/2026-08-11-admin-panel-ui-refresh/`. Lesson: MudBlazor providers must live in the same render scope as per-page @rendermode consumers (see `context/foundation/lessons.md`).
- **S-04: Admin deactivates a license and can later reactivate it — deactivation is a reversible state, not permanent.** — Archived 2026-08-12 → `context/archive/2026-08-12-license-deactivate-reactivate/`. Lesson: —.
- **S-07: Every call to the verification API is recorded as an audit entry — timestamp, the resolved license, the caller's IP address, and the call parameters/outcome (validity result returned, auth outcome: valid / invalid / missing key). The admin can open any license and view its chronological verification history alongside the existing edit-audit history.** — Archived 2026-08-31 → `context/archive/2026-08-31-license-verification-audit-log/`. Lesson: —.
