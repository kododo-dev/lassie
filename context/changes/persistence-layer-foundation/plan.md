# Persistence Layer Foundation Implementation Plan

## Overview

Wire EF Core + Npgsql to the already-deployed Postgres `lassie` database, establish a reusable audit-history mechanism as infrastructure (no domain entities yet), give local development its own throwaway Postgres, and make deploys apply pending migrations automatically. This is roadmap item `F-01` in `context/foundation/roadmap.md` — the foundation every other slice (S-01 through S-05) depends on.

## Current State Analysis

- `src/lassie.csproj` has zero data-access packages — only `Microsoft.AspNetCore.OpenApi` and `Microsoft.OpenApi`. No `DbContext`, no migrations, no EF tooling.
- `src/Program.cs` is still the scaffold's default `/weatherforecast` minimal API — nothing reads configuration for a connection string today.
- `deploy/docker-compose.yml` already sets `ConnectionStrings__DefaultConnection: "Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"` as a container env var, and `.env` on the VPS already has `POSTGRES_DB=lassie`, `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD=postgres` (`context/deployment/deploy-plan.md`). The `lassie` database itself already exists in the shared Postgres container on the VPS.
- No test project exists anywhere in the repo (confirmed via file listing) — there's no existing automated-testing convention to follow yet.
- `.github/workflows/deploy.yml` builds, pushes to GHCR, SSHes in, runs `sudo docker compose pull && up -d`, then polls the public URL as a health check. It does not run any migration step today.

## Desired End State

EF Core is connected end-to-end: a `LassieDbContext` resolves via DI, backed by Npgsql, reading the connection string that's already wired into the deploy config. A generic, entity-agnostic audit-history mechanism (`IAuditable` + `AuditLog` + a `SaveChanges` override) exists as infrastructure that future entities (starting with `License` in `S-03`) can opt into by implementing one interface — no `License`, `Module`, or any other domain entity is modeled in this plan. Local development has its own disposable Postgres via Docker Compose, independent of the VPS. Pending migrations apply automatically when the app starts, both locally and in production.

**Verification**: `docker compose -f docker-compose.dev.yml up -d && dotnet run --project src/lassie.csproj` boots cleanly against the local dev database with the `AuditLogs` table present; pushing to `main` deploys to the VPS and the same table is confirmed present in the production `lassie` database via `psql`.

### Key Discoveries:

- Production connection-string plumbing is already done (`deploy/docker-compose.yml`) — this plan only needs to *read* `ConnectionStrings:DefaultConnection` from configuration, not invent new secret handling.
- The `lassie` production database already exists (`context/deployment/deploy-plan.md`, step "Created database `lassie` in the shared `postgres` container") — nothing to provision there.
- `deploy: restart: unless-stopped` is already set on the `lassie` service in `deploy/docker-compose.yml`, which is what makes fail-fast-on-DB-unreachable a safe choice — Docker already retries a crashed container.

## What We're NOT Doing

- Modeling `License`, `Module`, or any other domain entity — that's `S-01`/`S-02`/`S-03`'s job.
- Building the admin identity/credentials table — that's `F-02`'s job.
- Standing up a test project or any automated integration tests against Postgres — explicitly deferred; this foundation is verified manually, matching its "foundation, not feature" scope.
- Retry/backoff logic for a DB that's unreachable at startup — explicitly rejected in favor of fail-fast + Docker's existing restart policy.
- Adding an explicit migration step to `.github/workflows/deploy.yml` — explicitly rejected in favor of auto-migrate-on-startup.
- Auditing entity *creation* (`Added` state) — only `Modified` and `Deleted` are captured, since FR-006 is about retaining a *previous* version on edit; there is no previous version to retain when a row is first created.

## Implementation Approach

Three phases, each building on the last: (1) get EF Core + a local dev database wired with an empty context, (2) add the audit-history convention and cut the first real migration against the local database, (3) turn on auto-migration at startup and prove the whole pipeline against the actual production database over the existing CI/CD path. Nothing here touches `deploy.yml`'s deploy mechanics beyond what already exists (build → push → SSH → `docker compose up -d` → health check) — the only new production behavior is that the app now migrates its own schema on boot.

## Critical Implementation Details

### State sequencing (SaveChanges override)

The audit interceptor must add `AuditLog` rows to the `ChangeTracker` **before** calling `base.SaveChangesAsync()`, not after — entities added to the tracker earlier in the same `SaveChangesAsync` call are included in the same database transaction as the original change. Adding them afterward would require a second round-trip and lose atomicity (an audit row could be written for a change that then fails to commit, or vice versa).

Capture `entry.OriginalValues` (the values as loaded from the database), not `entry.CurrentValues` — for a `Modified` entry, `CurrentValues` is the *new* state about to be saved, which is the opposite of what FR-006 needs retained. This is the one property EF Core's `ChangeTracker` API makes easy to get backwards.

## Phase 1: Persistence + local dev environment

### Overview

Get an empty `LassieDbContext` resolving via DI against a real Postgres, with a disposable local database for development that's independent of the VPS.

### Changes Required:

#### 1. EF Core + Npgsql packages

**File**: `src/lassie.csproj`

**Intent**: Add the Postgres EF Core provider and the design-time tooling needed for `dotnet ef migrations add`.

**Contract**: `PackageReference` entries for `Npgsql.EntityFrameworkCore.PostgreSql` (runtime provider) and `Microsoft.EntityFrameworkCore.Design` (design-time only — `PrivateAssets="all"`), both pinned to the latest stable versions compatible with `net10.0`. Confirm the `dotnet-ef` CLI tool is available (local tool manifest or global install) so `dotnet ef migrations add` works from `src/`.

#### 2. Empty DbContext

**File**: `src/Data/LassieDbContext.cs` (new)

**Intent**: The single EF Core entry point every future entity and the audit mechanism will hang off of. No `DbSet`s yet — this phase only proves DI + connectivity wiring.

**Contract**: `public class LassieDbContext(DbContextOptions<LassieDbContext> options) : DbContext(options)`. No overrides yet (added in Phase 2).

#### 3. DI registration + configuration read

**File**: `src/Program.cs`

**Intent**: Register `LassieDbContext` in the service container, reading the connection string that's already supplied via the `ConnectionStrings__DefaultConnection` environment variable in production (and a new local value in development).

**Contract**: `builder.Services.AddDbContext<LassieDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));` — added before `builder.Build()`.

#### 4. Local dev connection string

**File**: `src/appsettings.Development.json`

**Intent**: Point local development at the local Docker Postgres from item 5, independent of the VPS.

**Contract**: Add a `ConnectionStrings:DefaultConnection` key, e.g. `Host=localhost;Port=5432;Database=lassie_dev;Username=postgres;Password=postgres` — matching the local compose service below.

#### 5. Local dev Postgres

**File**: `docker-compose.dev.yml` (new, repo root — sibling to `Dockerfile`; kept separate from `deploy/docker-compose.yml`, which is VPS-deployment-only per `deploy/README.md`)

**Intent**: A single-service, throwaway Postgres for local development, unrelated to and unreachable from the VPS.

**Contract**: One `postgres` service (official `postgres` image), `POSTGRES_DB=lassie_dev`, `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD=postgres`, port `5432` published to `localhost`, a named volume for data persistence across restarts.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds

#### Manual Verification:

- `docker compose -f docker-compose.dev.yml up -d` starts a local Postgres container
- `dotnet run --project src/lassie.csproj` starts without throwing (DI resolves `LassieDbContext` without error; no query has run against it yet, so connectivity itself isn't exercised until Phase 2)

---

## Phase 2: Audit-history convention + first migration

### Overview

Add the generic audit-history mechanism as infrastructure, and cut + apply the first real migration — proving the full EF Core pipeline end-to-end against the local dev database.

### Changes Required:

#### 1. Auditable marker interface

**File**: `src/Data/Auditing/IAuditable.cs` (new)

**Intent**: A no-member marker interface any future entity (starting with `License` in `S-03`) implements to opt into audit-history tracking.

**Contract**: `public interface IAuditable { }`

#### 2. Audit log entity

**File**: `src/Data/Auditing/AuditLog.cs` (new)

**Intent**: One generic table holding a snapshot of any `IAuditable` entity's prior state whenever it's modified or deleted — no per-entity history table needed.

**Contract**: Properties: `Id` (identity key), `EntityName` (string — CLR type name of the audited entity), `EntityId` (string — string-formatted primary key, entity-agnostic), `ChangeType` (enum: `Modified`, `Deleted`), `ChangedAtUtc` (`DateTimeOffset`), `Snapshot` (string — JSON of the entity's pre-change values, stored as a Postgres `jsonb` column via `.HasColumnType("jsonb")` in `OnModelCreating`).

#### 3. SaveChanges override

**File**: `src/Data/LassieDbContext.cs`

**Intent**: Before persisting, capture a snapshot of every `IAuditable` entity currently `Modified` or `Deleted`, and add it as an `AuditLog` row in the same transaction.

**Contract**: Add `DbSet<AuditLog> AuditLogs`. Override `SaveChangesAsync` (and `SaveChanges`): for each `ChangeTracker.Entries()` whose `Entity is IAuditable` and `State is EntityState.Modified or EntityState.Deleted`, serialize `entry.OriginalValues` to JSON via `System.Text.Json.JsonSerializer` and `Add()` a new `AuditLog` row — done before calling the base save method, per the sequencing note in Critical Implementation Details.

#### 4. First migration

**File**: `src/Migrations/*_InitialCreate.cs` (generated)

**Intent**: Prove the full pipeline — model → migration → apply — end-to-end against the local dev database, producing the `AuditLogs` table and EF's own `__EFMigrationsHistory` table.

**Contract**: Run `dotnet ef migrations add InitialCreate --project src/lassie.csproj`, then `dotnet ef database update --project src/lassie.csproj` against the local dev Postgres from Phase 1.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `src/Migrations/*_InitialCreate.cs` exists after running `dotnet ef migrations add InitialCreate`

#### Manual Verification:

- `dotnet ef database update --project src/lassie.csproj` applies cleanly against the local dev database with no errors
- `docker compose -f docker-compose.dev.yml exec postgres psql -U postgres -d lassie_dev -c '\dt'` lists both `AuditLogs` and `__EFMigrationsHistory`

---

## Phase 3: Startup auto-migration + production verification

### Overview

Turn on auto-migration at app startup, then prove the whole thing end-to-end against the real production database via the existing deploy pipeline — no changes to `deploy.yml` itself.

### Changes Required:

#### 1. Auto-migrate on startup

**File**: `src/Program.cs`

**Intent**: Apply any pending migrations automatically every time the app starts, so a deploy that ships a new migration self-applies it with no separate CI step.

**Contract**: After `var app = builder.Build();` and before `app.Run()`: resolve `LassieDbContext` from a DI scope and call `.Database.Migrate()`, unguarded — no `try`/`catch`. An exception here is allowed to crash the process; `deploy/docker-compose.yml`'s existing `restart: unless-stopped` on the `lassie` service handles the retry.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `.github/workflows/deploy.yml`'s `build-and-push` job passes unchanged (no workflow file changes in this phase)

#### Manual Verification:

- Push to `main`; the deploy workflow's existing health check (polling `https://kododo.dev/lassie/weatherforecast`) passes, confirming the app didn't crash-loop on migration
- `ssh` to the VPS and run `sudo docker exec postgres psql -U postgres -d lassie -c '\dt'` — confirms `AuditLogs` and `__EFMigrationsHistory` now exist in the **production** database
- `sudo docker compose -f /opt/docker/lassie/docker-compose.yml logs lassie` shows no migration-related errors

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

No automated test project exists yet in this repo, and standing one up was explicitly deferred out of this foundation's scope (see "What We're NOT Doing"). Verification for all three phases is manual, as detailed in each phase's Manual Verification section above.

### Manual Testing Steps:

1. Local: `docker compose -f docker-compose.dev.yml up -d`, `dotnet ef database update --project src/lassie.csproj`, confirm `AuditLogs`/`__EFMigrationsHistory` exist via `psql`.
2. Local: `dotnet run --project src/lassie.csproj`, confirm no startup crash.
3. Production: push to `main`, confirm the deploy workflow's health check passes.
4. Production: `psql` against the VPS's `lassie` database, confirm `AuditLogs`/`__EFMigrationsHistory` exist there too.
5. Production: tail `docker compose logs lassie` on the VPS, confirm a clean migration with no errors in the startup log.

## Performance Considerations

None specific to this foundation — no domain queries exist yet. `Database.Migrate()` runs once per container start, not per-request.

## Migration Notes

This is the *first* migration ever created for this project — there's no existing data or prior schema to migrate away from. Future migrations (starting with `S-01`'s module entities) follow the same `dotnet ef migrations add` → commit → auto-apply-on-deploy convention established here.

## References

- Roadmap item: `context/foundation/roadmap.md` → `F-01`
- Deployed infrastructure: `context/foundation/infrastructure.md`, `context/deployment/deploy-plan.md`
- PRD requirement driving the audit mechanism: `context/foundation/prd.md` → `FR-006`
- Backlog issue: [kododo-dev/lassie#1](https://github.com/kododo-dev/lassie/issues/1)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Persistence + local dev environment

#### Automated

- [x] 1.1 `dotnet build src/lassie.csproj` succeeds — 5d140a6

#### Manual

- [x] 1.2 `docker compose -f docker-compose.dev.yml up -d` starts a local Postgres container — 5d140a6
- [x] 1.3 `dotnet run --project src/lassie.csproj` starts without throwing — 5d140a6

### Phase 2: Audit-history convention + first migration

#### Automated

- [x] 2.1 `dotnet build src/lassie.csproj` succeeds — db83663
- [x] 2.2 `src/Migrations/*_InitialCreate.cs` exists after `dotnet ef migrations add InitialCreate` — db83663

#### Manual

- [x] 2.3 `dotnet ef database update` applies cleanly against the local dev database — db83663
- [x] 2.4 `psql` against the local dev database lists `AuditLogs` and `__EFMigrationsHistory` — db83663

### Phase 3: Startup auto-migration + production verification

#### Automated

- [x] 3.1 `dotnet build src/lassie.csproj` succeeds
- [ ] 3.2 `.github/workflows/deploy.yml`'s `build-and-push` job passes unchanged

#### Manual

- [ ] 3.3 Deploy workflow's health check passes after pushing to `main`
- [ ] 3.4 `psql` against the production `lassie` database lists `AuditLogs` and `__EFMigrationsHistory`
- [ ] 3.5 `docker compose logs lassie` on the VPS shows no migration-related errors
