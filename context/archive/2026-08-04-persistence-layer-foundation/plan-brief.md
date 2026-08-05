# Persistence Layer Foundation — Plan Brief

> Full plan: `context/changes/persistence-layer-foundation/plan.md`

## What & Why

Wire EF Core to the already-deployed Postgres database and establish a reusable audit-history mechanism, before any domain entity (License, Module, admin identity) gets built on top of an ad-hoc pattern. This is `F-01` on the roadmap — the foundation every other slice depends on, either directly or transitively.

## Starting Point

`src/lassie.csproj` has zero data-access packages today — no EF Core, no `DbContext`, no migrations. Production connection-string plumbing already exists in `deploy/docker-compose.yml`, and the `lassie` database already exists on the VPS's shared Postgres — this plan only needs to consume that, not invent new secrets handling.

## Desired End State

`LassieDbContext` resolves via DI, backed by Npgsql. Any future entity can opt into audit history by implementing one interface (`IAuditable`) — no entity is modeled in this plan. Local dev has its own disposable Postgres, independent of the VPS. Deploys apply pending migrations automatically on startup, verified against both the local database and the real production database.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Local dev database | Local Postgres via Docker Compose | Safe to experiment freely, no VPS network dependency for day-to-day dev work. |
| Migration apply strategy | Auto-apply on app startup (`Database.Migrate()`) | Simplest pipeline, zero `deploy.yml` changes, matches solo/after-hours capacity. |
| Audit-history pattern scope | Decided and documented now (generic `IAuditable` + `AuditLog`), no entities yet | `S-03` gets a settled mechanism to build on instead of inventing one under pressure. |
| Verification approach | Manual only — no test project | Matches this being a foundation, not a feature; no test convention exists yet to follow. |
| DB unreachable at boot | Fail fast, no retry/backoff | Docker's existing `restart: unless-stopped` already handles this; extra retry logic has no payoff yet. |

## Scope

**In scope:** EF Core + Npgsql wiring, empty `DbContext`, generic audit-history mechanism (`IAuditable`/`AuditLog`/`SaveChanges` override), local dev Postgres via Compose, startup auto-migration, first migration applied to both local and production databases.

**Out of scope:** Any domain entity (`License`, `Module`, admin identity), automated tests, retry/backoff on DB connectivity, an explicit CI migration step, auditing entity *creation* (only `Modified`/`Deleted` are captured).

## Architecture / Approach

One new `LassieDbContext` with a `SaveChanges` override that, for any tracked entity implementing `IAuditable`, writes a JSON snapshot of its pre-change (`OriginalValues`) state into a single generic `AuditLog` table — in the same transaction as the actual change, added to the change tracker before the base save call. This keeps the mechanism entity-agnostic: `License` (in `S-03`) opts in by implementing one interface, no per-entity history table required.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Persistence + local dev environment | Empty `DbContext` resolving via DI, local Postgres via Compose | Low — standard EF Core wiring |
| 2. Audit-history convention + first migration | `IAuditable`/`AuditLog`/`SaveChanges` override, first migration applied locally | Medium — the `OriginalValues`-before-`base.SaveChanges()` sequencing is a real EF Core gotcha |
| 3. Startup auto-migration + production verification | Auto-migrate on boot, verified against the real VPS database | Low — relies on already-working CI/CD and Docker restart policy |

**Prerequisites:** None — the target database already exists on the VPS (per roadmap `F-01`).
**Estimated effort:** Not estimated — see `plan.md`; agentic execution is non-linear.

## Open Risks & Assumptions

- Assumes `dotnet-ef` tooling can be installed/run in this environment without additional setup beyond adding the `Microsoft.EntityFrameworkCore.Design` package.
- The generic `AuditLog` design is unvalidated against a real entity until `S-03` — if `License`'s audit needs turn out to require more than "snapshot of pre-change values" (e.g., who made the change), this mechanism will need a small extension then, not a redesign.

## Success Criteria (Summary)

- `AuditLogs` and `__EFMigrationsHistory` tables exist in both the local dev database and the production `lassie` database.
- The app boots cleanly (no crash-loop) both locally and after a production deploy.
- No domain entity was modeled — the next roadmap item (`F-02` or `S-01`) is the first thing to actually use this foundation.
