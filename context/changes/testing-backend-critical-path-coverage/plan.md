# Backend Critical-Path Coverage Implementation Plan

## Overview

Bootstrap the project's first test project (xUnit) and write regression-guard
tests for the three highest-severity, purely-backend risks named in
`context/foundation/test-plan.md` §3 Phase 1: license status precedence
(Risk #1), API key secrecy (Risk #2), and audit load-before-mutate (Risk #5).

## Current State Analysis

No test project exists anywhere in the repo — `src/lassie.csproj` is the
only project file, with zero test-related package references
(`context/changes/testing-backend-critical-path-coverage/research.md`,
"Test-project bootstrap feasibility"). All three target risks are already
**correctly implemented** in current code (same research doc, "Summary") —
this plan writes regression locks, not bug fixes.

## Desired End State

A `tests/Lassie.Tests/` xUnit project exists, builds, and runs via
`dotnet test tests/Lassie.Tests/Lassie.Tests.csproj`. It contains:
unit tests pinning `License.Status`'s precedence and UTC-day-boundary
behavior; unit + integration tests pinning API-key structural secrecy and
the verify endpoint's response shape; and an integration test pinning
audit-snapshot correctness against a real Postgres instance. All tests pass
locally against a Testcontainers-provisioned Postgres with no manual setup
beyond having Docker available (already required for local dev per
`docker-compose.dev.yml`).

### Key Discoveries:

- `License.cs:22-27` is the single source of truth for status precedence —
  both the verify endpoint (`Program.cs:158`) and the panel list consume it
  directly, no divergent logic (research.md, "Risk #1").
- The expiry check is a **date-only** UTC comparison (`DateOnly` vs.
  `DateOnly.FromDateTime(DateTime.UtcNow)`, `>=`) — the real boundary is
  UTC-calendar-day rollover, not an instant-level edge (research.md,
  "Corrections to test-plan.md §2").
- `ApiKeyHasher.Generate()` (`ApiKeyHasher.cs:9-16`) never lets the raw key
  reach persistence — only `Hash()`'s SHA-256 output is stored
  (`License.cs:19-20`). No logging middleware exists anywhere in
  `Program.cs`'s pipeline that could capture it.
- `LassieDbContext.SaveChanges`/`SaveChangesAsync` overrides
  (`LassieDbContext.cs:39-85`) build the audit snapshot from
  `entry.OriginalValues` — correct only when the entity was loaded via a
  query first. Both current License write paths (create = `Added`, not
  audited; edit/deactivate = loaded via `SingleOrDefaultAsync` then mutated
  in place, `EditLicense.razor:86,146-159`) already comply.
- `Program.cs` has **no `public partial class Program {}` marker** —
  required for `WebApplicationFactory<Program>` and must be added.
- App startup eagerly calls `context.Database.Migrate()` and throws if
  `ADMIN_EMAIL`/`ADMIN_PASSWORD` aren't set (`Program.cs:76-100`) — any
  `WebApplicationFactory`-based test host must supply these.
- A disposable local dev Postgres already exists
  (`docker-compose.dev.yml`, `postgres:17`, host port 5433) — confirms
  Docker is already an accepted local-dev dependency, supporting the choice
  of Testcontainers.PostgreSql for tests.

## What We're NOT Doing

- Not testing the Blazor `CreateLicense.razor` reveal-once UI behavior
  (masking, no re-display) — deferred to test-plan rollout Phase 3 (bUnit
  component tests), per user decision. This phase's Risk #2 coverage stops
  at DB persistence and the verify endpoint's response shape.
- Not testing Risk #3 (outage vs. invalid) or Risk #4 (API key IDOR) —
  those are test-plan rollout Phase 2's scope (`context/foundation/test-plan.md`
  §3).
- Not wiring `dotnet test` into CI — that's test-plan rollout Phase 4.
- Not resolving the open UX question of whether the admin-facing date
  picker should communicate UTC semantics for `ExpiresOn` — flagged in
  research.md as adjacent but uninvestigated; left as a documented open item
  in the test plan, not addressed in test code.
- Not migrating to xunit v3 (4.0.0, current major as of 2026-08-18) — the
  more mature xunit v2 line (2.9.3) is used instead, since
  `Microsoft.AspNetCore.Mvc.Testing`/`Testcontainers.PostgreSql`/bUnit
  (needed in Phase 3 of the rollout) are all overwhelmingly documented
  against v2, and this project's priority is low-friction over
  bleeding-edge.

## Implementation Approach

Four phases: scaffold the test project and shared integration-test
infrastructure first (Phase 1), then one phase per risk in ascending cost
(Phase 2: pure unit tests for Risk #1, no DB; Phase 3: unit + integration
for Risk #2; Phase 4: integration-only for Risk #5, plus the cookbook
update). Every integration test shares one Testcontainers-provisioned
Postgres container per test run (started once, per the xUnit collection
fixture) and isolates itself via a per-test transaction that's rolled back
— confirmed decisions from planning. Migrations run once, at container
startup, via the app's own existing `Migrate()` call inside
`WebApplicationFactory`'s host startup (idempotent — safe to also let this
double-run per test-factory instance, see Critical Implementation Details).

## Critical Implementation Details

### Timing & lifecycle: satisfying `WebApplicationFactory`'s host startup

`Program.cs:76-100` calls `context.Database.Migrate()` and reads
`ADMIN_EMAIL`/`ADMIN_PASSWORD` from configuration, throwing if either is
unset, before the host finishes building. The custom
`WebApplicationFactory<Program>` subclass must override `ConfigureWebHost`
to (a) inject `ADMIN_EMAIL`/`ADMIN_PASSWORD` test values into
configuration, and (b) override the `LassieDbContext` registration to point
at the Testcontainers connection *before* `CreateClient()`/`CreateHost()`
triggers the startup block — otherwise host creation throws or migrates
against the wrong (or no) database.

### State sequencing: sharing one open connection for transaction-rollback isolation

The confirmed isolation strategy (per-test transaction rollback) only
isolates HTTP-driven writes if every `LassieDbContext` instance created
during a test — including ones instantiated deep inside the app's own
request pipeline via DI — participates in the *same* already-open
`NpgsqlConnection` and outer transaction that the test began. The default
`UseNpgsql(connectionString)` registration lets EF Core pool/open its own
connections per scope, which would silently defeat rollback isolation (each
request gets its own connection, outside the test's transaction). The test
fixture must replace the DbContext registration with
`UseNpgsql(existingOpenConnection)` bound to the connection the test's base
class already opened and began a transaction on.

## Phase 1: Test Project Scaffold + Shared Fixtures

### Overview

Create the `tests/Lassie.Tests/` project, add the `Program` marker to
`src/Program.cs`, and build the shared integration-test infrastructure
(Testcontainers collection fixture, custom `WebApplicationFactory`,
transaction-scoped base class) that Phases 3 and 4 depend on.

### Changes Required:

#### 1. Test project file

**File**: `tests/Lassie.Tests/Lassie.Tests.csproj`

**Intent**: New xUnit test project targeting `net10.0`, referencing
`src/lassie.csproj`.

**Contract**: `Sdk="Microsoft.NET.Sdk"`, `TargetFramework=net10.0`,
`IsPackable=false`, `Nullable=enable`. Package references: `xunit` 2.9.3,
`xunit.runner.visualstudio` 2.8.2 (`PrivateAssets="all"`,
`IncludeAssets="runtime;build;native;contentfiles;analyzers;buildtransitive"`),
`Microsoft.NET.Test.Sdk` 18.9.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.11,
`Testcontainers.PostgreSql` 4.14.0. One `ProjectReference` to
`../../src/lassie.csproj`.

#### 2. `Program` marker for `WebApplicationFactory`

**File**: `src/Program.cs`

**Intent**: Expose the top-level-statement entry point as a type
`WebApplicationFactory<Program>` can bootstrap against.

**Contract**: Append `public partial class Program;` at the end of the
file. No other change to the file's existing behavior.

#### 3. Postgres collection fixture

**File**: `tests/Lassie.Tests/Infrastructure/PostgresCollectionFixture.cs`

**Intent**: Start one Testcontainers `PostgreSqlContainer` for the whole
test run, shared across every integration test via an xUnit
`[CollectionDefinition]`.

**Contract**: Implements `IAsyncLifetime`. `InitializeAsync` starts a
`postgres:17`-tagged container (matching `docker-compose.dev.yml`'s
version) and exposes its connection string. `DisposeAsync` stops it.
Declares `[CollectionDefinition("Postgres")]` for Phases 3–4's test classes
to opt into via `[Collection("Postgres")]`.

#### 4. Custom `WebApplicationFactory`

**File**: `tests/Lassie.Tests/Infrastructure/LassieWebApplicationFactory.cs`

**Intent**: Boot the real app host against the Testcontainers Postgres
instance, with `ADMIN_EMAIL`/`ADMIN_PASSWORD` satisfied and the
`LassieDbContext` registration swapped to the per-test open connection (see
Critical Implementation Details).

**Contract**: `class LassieWebApplicationFactory : WebApplicationFactory<Program>`,
constructed with the target `NpgsqlConnection` for a given test. Overrides
`ConfigureWebHost` to call `UseSetting` for `ADMIN_EMAIL`/`ADMIN_PASSWORD`
test values, and `ConfigureServices` to remove the existing
`DbContextOptions<LassieDbContext>` registration and re-add it bound to the
supplied connection via `UseNpgsql(connection)`.

#### 5. Transaction-scoped integration test base class

**File**: `tests/Lassie.Tests/Infrastructure/IntegrationTestBase.cs`

**Intent**: Give every integration test class a fresh, isolated
`LassieWebApplicationFactory` + `HttpClient` + `LassieDbContext`, backed by
a transaction rolled back on disposal.

**Contract**: `abstract class IntegrationTestBase : IAsyncLifetime`, takes
the `PostgresCollectionFixture` via constructor injection.
`InitializeAsync` opens a new `NpgsqlConnection` against the fixture's
connection string, begins a transaction, constructs the
`LassieWebApplicationFactory` bound to that connection, and exposes
`HttpClient` and a `LassieDbContext` (also bound to the same connection/
transaction) as protected properties. `DisposeAsync` rolls back the
transaction and disposes the connection and factory.

### Success Criteria:

#### Automated Verification:

- `dotnet build tests/Lassie.Tests/Lassie.Tests.csproj` succeeds
- `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj` runs cleanly (0
  tests collected is expected at this phase)
- A smoke test (`tests/Lassie.Tests/Infrastructure/FixtureSmokeTests.cs`)
  using `IntegrationTestBase` confirms: the container starts, migrations
  applied (querying `Licenses` and `AuditLogs` tables returns empty result
  sets without error), and the app host responds to a basic HTTP request

#### Manual Verification:

- Docker is running locally and `dotnet test` for this phase completes in
  well under a minute (confirms Testcontainers isn't hanging waiting for a
  daemon)

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Risk #1 — License Status Precedence

### Overview

Pure unit tests (no DB) pinning `License.Status`'s precedence rule and the
UTC-day-boundary behavior research corrected the risk's framing to.

### Changes Required:

#### 1. Status precedence and boundary tests

**File**: `tests/Lassie.Tests/Licenses/LicenseStatusTests.cs`

**Intent**: Table-driven coverage of `IsActive × ExpiresOn` combinations,
asserting against literal expected `LicenseStatus` values derived from the
PRD rule (Deactivated > Expired > Active) — not by re-deriving the
comparison the implementation itself uses, to avoid the oracle problem.

**Contract**: `[Theory]` with `[MemberData]` rows covering: `IsActive=false`
crossed with `ExpiresOn` = null / yesterday(UTC) / today(UTC) /
tomorrow(UTC) → all `Deactivated` (precedence trumps expiry in every case);
`IsActive=true, ExpiresOn=null` → `Active`; `IsActive=true,
ExpiresOn=today(UTC)` → `Active` (the corrected boundary case — still
valid for the entire UTC day it expires on); `IsActive=true,
ExpiresOn=yesterday(UTC)` → `Expired`; `IsActive=true,
ExpiresOn=tomorrow(UTC)` → `Active`. `today`/`yesterday`/`tomorrow` are
computed once per test run as `DateOnly` offsets from
`DateOnly.FromDateTime(DateTime.UtcNow)`, matching the entity's own clock
source so the boundary is exercised at the real current UTC day rather than
a hardcoded date that could itself drift into a different relationship over
time.

### Success Criteria:

#### Automated Verification:

- `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj --filter LicenseStatusTests` passes, all rows green

#### Manual Verification:

- Spot-check one row's expected value against `context/foundation/prd.md`
  FR-007/FR-010 by hand (confirms the oracle is the PRD, not the code)

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Risk #2 — API Key Secrecy

### Overview

Unit tests on `ApiKeyHasher`'s structural properties, plus an integration
test confirming the verify endpoint's response never contains the key or
its hash.

### Changes Required:

#### 1. `ApiKeyHasher` structural property tests

**File**: `tests/Lassie.Tests/Licenses/ApiKeyHasherTests.cs`

**Intent**: Assert structural invariants of key generation/hashing, not the
implementation's own literal output.

**Contract**: `[Fact]` tests asserting: `Generate()`'s `RawKey` and `Hash`
are never equal to each other; `Hash` is a 64-character uppercase hex
string (SHA-256 digest length — a structural property independent of any
specific input); two calls to `Generate()` produce distinct `RawKey`/`Hash`
pairs; calling the hash function twice on the same raw key string produces
the same hash both times (determinism, required for verify-lookup to work
— a legitimate same-function call since it tests a property of the
function, not a copied expected value).

#### 2. Verify-endpoint response-shape integration test

**File**: `tests/Lassie.Tests/Licenses/VerifyEndpointKeySecrecyTests.cs`

**Intent**: Prove the verify endpoint's JSON response never contains the
raw key or its hash, for both a valid and an invalid key — using
`IntegrationTestBase`.

**Contract**: `class VerifyEndpointKeySecrecyTests : IntegrationTestBase`.
Seeds a `License` directly via the test's `LassieDbContext` (inside the
rolled-back transaction) with a known raw key/hash pair. Calls
`GET /api/license/verify` via `HttpClient` with the valid raw key, parses
the response as a `JsonDocument`, and asserts its root object has exactly
one property (`valid`) — no `apiKeyHash`, `key`, or any other field, a
structural DTO-leak guard rather than a fixed-string comparison. Repeats
the same single-property assertion for a request with a garbage/missing
key.

### Success Criteria:

#### Automated Verification:

- `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj --filter "ApiKeyHasherTests|VerifyEndpointKeySecrecyTests"` passes

#### Manual Verification:

- Manually inspect one captured response body in the test output/debugger
  to confirm it's literally `{"valid":true}` with no extra whitespace-hidden
  fields

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Risk #5 — Audit Load-Before-Mutate + Cookbook Update

### Overview

Integration test pinning audit-snapshot correctness against a real
Postgres instance for both the edit and deactivate/reactivate write paths,
then update the test plan's cookbook (§6) with the patterns this rollout
phase shipped — the constraint `/10x-test-plan` bakes into every rollout
phase's final sub-phase.

### Changes Required:

#### 1. Audit snapshot correctness test

**File**: `tests/Lassie.Tests/Auditing/AuditLoadBeforeMutateTests.cs`

**Intent**: Prove the audit log's "before" snapshot reflects the true
persisted prior state for the current License write path, using a real
DbContext against real Postgres — not a mocked `ChangeTracker`, per the
anti-pattern this exact test-plan risk names.

**Contract**: `class AuditLoadBeforeMutateTests : IntegrationTestBase`. Two
`[Fact]` tests, both seeding a `License` with a known initial `Label` (and
`IsActive=true`) via `Add` + `SaveChangesAsync`, then in a *separate*
load-and-mutate step (mirroring `EditLicense.razor:86,146-159`'s pattern:
query the entity fresh, mutate a field, `SaveChangesAsync`) — one test
mutates `Label`, the other mutates `IsActive` (the deactivate path). Each
queries the resulting `AuditLog` row, deserializes `Snapshot`, and asserts
the "before" value for the mutated field equals the literal value seeded at
the start of the test (the independent oracle) — not the post-mutation
value. Also asserts `EntityName == "License"`, `ChangeType == Modified`,
and `EntityId` matches the seeded license's `Id`.

#### 2. Cookbook update

**File**: `context/foundation/test-plan.md`

**Intent**: Fill in §6.1 (unit tests) and §6.2 (integration tests), which
currently read "TBD — see §3 Phase 1," with the concrete patterns this
phase shipped.

**Contract**: §6.1 documents: location `tests/Lassie.Tests/<Area>/`, naming
`<Subject>Tests.cs`, reference test `tests/Lassie.Tests/Licenses/LicenseStatusTests.cs`
(table-driven, PRD-sourced expected values), run command
`dotnet test tests/Lassie.Tests/Lassie.Tests.csproj --filter <ClassName>`.
§6.2 documents: location `tests/Lassie.Tests/<Area>/`, the
`IntegrationTestBase` + `PostgresCollectionFixture` pattern (Testcontainers
Postgres, one container per run, per-test transaction rollback), mocking
policy (never mock `LassieDbContext` — always the real Testcontainers
instance, per the lesson this risk exists to enforce), reference test
`tests/Lassie.Tests/Auditing/AuditLoadBeforeMutateTests.cs`, run command
`dotnet test tests/Lassie.Tests/Lassie.Tests.csproj --filter <ClassName>`.

### Success Criteria:

#### Automated Verification:

- `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj` — full suite passes
  (all phases' tests together)
- `context/foundation/test-plan.md` §6.1 and §6.2 no longer read "TBD"

#### Manual Verification:

- Read the updated §6.1/§6.2 entries and confirm they'd actually let a
  future contributor add a new unit or integration test without re-deriving
  the fixture pattern from scratch

**Implementation Note**: After completing this phase and all automated
verification passes, pause here for manual confirmation from the human that
the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- `License.Status` precedence and UTC-day-boundary matrix (Phase 2)
- `ApiKeyHasher` structural invariants: raw≠hash, hash format, uniqueness,
  determinism (Phase 3)

### Integration Tests:

- Verify endpoint response-shape secrecy guard, valid and invalid key
  (Phase 3)
- Audit snapshot correctness for edit and deactivate write paths (Phase 4)

### Manual Testing Steps:

1. Confirm Docker is running before starting any phase's test run.
2. After Phase 1, confirm `dotnet test` completes quickly (container
   startup isn't hanging).
3. After Phase 2, spot-check one boundary row against the PRD by hand.
4. After Phase 3, inspect one raw HTTP response body for the verify
   endpoint.
5. After Phase 4, read the updated cookbook sections for completeness.

## Performance Considerations

Testcontainers adds container-startup latency (typically a few seconds) to
the first test in a run, but only once per run (collection-scoped fixture,
not per-test). Per-test transaction rollback keeps individual test overhead
low (no migration or truncation cost per test).

## Migration Notes

Not applicable — no existing data or systems to migrate. This phase only
adds a new, independent test project; it does not modify any production
code path except the additive `Program` marker (Phase 1) and the reused
production write paths under test (unmodified).

## References

- Related research: `context/changes/testing-backend-critical-path-coverage/research.md`
- Test plan: `context/foundation/test-plan.md` §2 (Risk Map, Risk Response
  Guidance), §3 Phase 1, §6 (Cookbook, updated by Phase 4)
- Lesson driving Risk #5: `context/foundation/lessons.md` ("Audit snapshots
  require load-before-mutate")
- Pattern to follow for load-before-mutate: `src/Components/Pages/EditLicense.razor:86,146-159`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Test Project Scaffold + Shared Fixtures

#### Automated

- [x] 1.1 `dotnet build tests/Lassie.Tests/Lassie.Tests.csproj` succeeds — 30c4cfc
- [x] 1.2 `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj` runs cleanly (0 tests collected is expected at this phase) — 30c4cfc
- [x] 1.3 Fixture smoke test confirms container start, migrations applied, host responds — 30c4cfc

#### Manual

- [x] 1.4 Docker running locally; `dotnet test` for this phase completes in well under a minute — 30c4cfc

### Phase 2: Risk #1 — License Status Precedence

#### Automated

- [x] 2.1 `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj --filter LicenseStatusTests` passes, all rows green — 0469750

#### Manual

- [x] 2.2 Spot-check one boundary row's expected value against `context/foundation/prd.md` FR-007/FR-010 by hand — 0469750

### Phase 3: Risk #2 — API Key Secrecy

#### Automated

- [x] 3.1 `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj --filter "ApiKeyHasherTests|VerifyEndpointKeySecrecyTests"` passes — 0e1b746

#### Manual

- [x] 3.2 Manually inspect one captured verify-endpoint response body to confirm it's exactly `{"valid":true}` — 0e1b746

### Phase 4: Risk #5 — Audit Load-Before-Mutate + Cookbook Update

#### Automated

- [x] 4.1 `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj` — full suite passes
- [x] 4.2 `context/foundation/test-plan.md` §6.1 and §6.2 no longer read "TBD"

#### Manual

- [x] 4.3 Read updated §6.1/§6.2 entries and confirm they're sufficient for a future contributor
