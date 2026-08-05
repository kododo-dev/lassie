---
project: Lassie
version: 1
status: draft
created: 2026-08-04
updated: 2026-08-05
prd_version: 1
main_goal: quality
top_blocker: capacity
---

# Roadmap: Lassie

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

A company that ships its own product to many customer deployments has no centralized way to manage those licenses today — granting access, limiting modules, and capping user counts is all manual. The tool currently in use (Intellilock) binds a license to a physical machine, which breaks down in cloud environments where the machine under a deployment changes over time. Customer deployments are distributed and not always online, so license verification has to tolerate periodic — not continuous — connectivity, without falling back to hardware-locking.

## North star

**S-02: Admin creates a license with a generated API key, and a client app can verify that license's status, modules, and limit through the API** — this is the smallest end-to-end flow that proves Lassie's core hypothesis (a working, non-hardware-locked license lifecycle), and it maps directly to both primary Success Criteria in the PRD.

> A reader-facing note on what "north star" means here: it's the smallest end-to-end slice whose successful delivery proves the core product hypothesis — placed as early as its Prerequisites allow, because every other slice only matters if this one works. This gloss is stated once, here; it isn't repeated later in this document.

**Why this wasn't split further:** the PRD's own `US-01` already frames license-creation and client-app-verification as a single acceptance-tested story — a license created but never machine-verified (or a verification endpoint with no way to create a license) proves nothing on its own. Splitting them into two slices would produce two halves that are each individually unverifiable against the PRD's own acceptance criteria, so they're kept as one vertical slice here even though it touches more FRs than its siblings.

## At a glance

| ID   | Change ID                          | Outcome (user can …)                                                                 | Prerequisites | PRD refs                          | Status   |
| ---- | ----------------------------------- | -------------------------------------------------------------------------------------- | -------------- | ---------------------------------- | -------- |
| F-01 | `persistence-layer-foundation`      | (foundation) DB connectivity + migration tooling verified end-to-end                   | —              | FR-006 (enabler), Access Control   | done     |
| F-02 | `admin-auth-foundation`             | (foundation) Admin can authenticate to the panel; unauthenticated requests are rejected | F-01           | FR-011, Access Control             | proposed |
| S-01 | `module-catalog-management`         | Admin can create and edit license module definitions                                   | F-01, F-02     | FR-004                             | proposed |
| S-02 | `license-creation-and-verification` | Admin creates a license + API key; client app verifies it via the API                  | S-01, F-01, F-02 | FR-005, FR-008, FR-009, FR-010, US-01 | proposed |
| S-03 | `license-edit-with-audit-history`   | Admin edits a license, with prior versions retained for audit                          | S-02, F-01, F-02 | FR-006                             | proposed |
| S-04 | `license-deactivate-reactivate`     | Admin deactivates a license and later reactivates it                                   | S-02, F-01, F-02 | FR-007                             | proposed |
| S-05 | `license-list-view`                 | Admin views the list of licenses and their current status                              | S-02, F-01, F-02 | FR-012                             | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme                    | Chain                          | Note                                                                                   |
| ------ | ------------------------- | ------------------------------- | --------------------------------------------------------------------------------------- |
| A      | Foundations & north star  | `F-01` → `F-02` → `S-01` → `S-02` | Mandatory path to the north star; `F-01` is ready to start now.                       |
| B      | Audit & correction        | `S-03`                          | Joins Stream A at `S-02`. Sequenced first among the three post-launch branches — `quality` goal prioritizes protecting FR-006's audit guarantee as soon as licenses can be edited. |
| C      | Lifecycle control         | `S-04`                          | Joins Stream A at `S-02`. Parallel with Streams B and D — no shared prerequisites beyond `S-02`. |
| D      | Visibility                | `S-05`                          | Joins Stream A at `S-02`. Parallel with Streams B and C; lowest risk of the three (read-only). |

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
- **Status:** proposed

## Slices

### S-01: Module catalog management

- **Outcome:** Admin can create and edit license module definitions from the panel.
- **Change ID:** `module-catalog-management`
- **PRD refs:** FR-004
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** License creation (S-02) requires a non-empty set of modules to choose from — sequenced first so S-02 isn't blocked on seed/reference data existing.
- **Status:** proposed

### S-02: License creation and client-app verification (north star)

- **Outcome:** Admin creates a license — label, chosen modules, user limit, optional expiry date — and the system generates a unique API key; a client app using that key gets back the license's validity, modules, and limit from the verification API.
- **Change ID:** `license-creation-and-verification`
- **PRD refs:** FR-005, FR-008, FR-009, FR-010, US-01
- **Prerequisites:** S-01, F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** This slice is what every client app depends on continuously once deployed, so the NFRs that matter most here — the API key never appearing in plaintext after generation, the <500ms response guardrail, and distinguishing "service unavailable" from "license invalid" (a network hiccup must never read as revocation) — deserve more scrutiny here than anywhere else on the roadmap. `main_goal: quality` weighs this.
- **Status:** proposed

### S-03: License edit with audit history

- **Outcome:** Admin edits a license's modules, limits, or expiry date, and every prior version remains available for audit — no destructive overwrite.
- **Change ID:** `license-edit-with-audit-history`
- **PRD refs:** FR-006
- **Prerequisites:** S-02, F-01, F-02
- **Parallel with:** S-04, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Retrofitting audit history onto an edit path that already shipped without it is riskier than building it in from this feature's first version — sequenced right after the north star so no license has ever been edited without a history record. Prioritized ahead of S-04/S-05 among the three parallel branches per `main_goal: quality`.
- **Status:** proposed

### S-04: License deactivate / reactivate

- **Outcome:** Admin deactivates a license and can later reactivate it — deactivation is a reversible state, not permanent.
- **Change ID:** `license-deactivate-reactivate`
- **PRD refs:** FR-007
- **Prerequisites:** S-02, F-01, F-02
- **Parallel with:** S-03, S-05
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Independent of S-03/S-05 — a status-flag toggle on an entity that already exists after S-02. Low risk; no shared state with the other two parallel branches.
- **Status:** proposed

### S-05: License list view

- **Outcome:** Admin views the list of licenses and each one's current status.
- **Change ID:** `license-list-view`
- **PRD refs:** FR-012
- **Prerequisites:** S-02, F-01, F-02
- **Parallel with:** S-03, S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Read-only surface; lowest risk of the three parallel branches, needs only S-02's data to exist.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID                          | Suggested issue title                                    | Ready for `/10x-plan` | Notes                                   |
| ---------- | ------------------------------------ | ---------------------------------------------------------- | ---------------------- | ----------------------------------------- |
| F-01       | `persistence-layer-foundation`       | Wire EF Core + migrations against the deployed Postgres DB | yes                    | Nothing blocks starting this today       |
| F-02       | `admin-auth-foundation`              | Admin email/password login for the panel                   | no                      | Waiting on F-01                           |
| S-01       | `module-catalog-management`          | Admin can manage license module definitions                | no                      | Waiting on F-01, F-02                     |
| S-02       | `license-creation-and-verification`  | License creation + client-app verification API (north star) | no                    | Waiting on S-01; this is the north star   |
| S-03       | `license-edit-with-audit-history`    | License edit with audit-history retention                  | no                      | Waiting on S-02                           |
| S-04       | `license-deactivate-reactivate`      | License deactivate / reactivate                            | no                      | Waiting on S-02; parallel with S-03, S-05 |
| S-05       | `license-list-view`                  | License list view with status                              | no                      | Waiting on S-02; parallel with S-03, S-04 |

## Open Roadmap Questions

None. The PRD closed with zero open questions (`prd.md` → `## Open Questions`: "Brak nierozwiązanych kwestii"), and the Step 5 interview didn't surface a new question spanning more than one slice. The one real gap found (how the first admin account gets provisioned) is narrow enough to live as a non-blocking Unknown on F-02 rather than here.

## Parked

Lifted from PRD `## Non-Goals` — MVP scope was already deliberately trimmed during shaping, so nothing new was added here during roadmap generation.

- **Payment/invoicing integration** — Why parked: handled outside Lassie entirely (PRD §Non-Goals).
- **Hardware-locking / offline crypto** — Why parked: a deliberate departure from the previous tool (Intellilock), which breaks down in cloud environments.
- **Self-service portal for end customers** — Why parked: in MVP, only the supplier-side admin manages licenses.
- **Multi-tenant support (other companies as Lassie customers)** — Why parked: MVP serves one company only; multi-tenant is the target beyond MVP.
- **Grouping licenses into folders** — Why parked: a license is a flat, standalone unit in MVP.
- **Advanced licensing models (tiered subscriptions, auto-expiring trials, floating/shared licenses)** — Why parked: out of MVP scope.
- **Expiry-approaching notifications** — Why parked: out of MVP scope.
- **Unauthorized license-sharing detection** — Why parked: a target-state goal, not MVP.
- **API key rotation/regeneration without creating a new license** — Why parked: out of MVP scope.
- **Admin password reset / account recovery** — Why parked: out of MVP scope; recovery is manual.
- **License list search/filtering** — Why parked: a simple list is enough for MVP.
- **Multi-language / white-labeling of the admin panel** — Why parked: out of MVP scope.
- **Advanced telemetry/analytics on module usage** — Why parked: out of MVP scope.

## Done

- **F-01: (foundation) DB connectivity + migration tooling verified end-to-end** — Archived 2026-08-05 → `context/archive/2026-08-04-persistence-layer-foundation/`. Lesson: —.
