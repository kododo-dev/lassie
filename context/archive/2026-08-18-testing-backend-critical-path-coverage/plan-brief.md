# Backend Critical-Path Coverage — Plan Brief

> Full plan: `context/changes/testing-backend-critical-path-coverage/plan.md`
> Research: `context/changes/testing-backend-critical-path-coverage/research.md`

## What & Why

Bootstrap the project's first test project and lock in regression coverage
for the three highest-severity, purely-backend risks from
`context/foundation/test-plan.md` §3 Phase 1: license status precedence,
API key secrecy, and audit load-before-mutate. Research confirmed all three
are already correctly implemented — this is regression-locking, not
bug-fixing.

## Starting Point

No test project exists anywhere in the repo (`src/lassie.csproj` is the
only project file, zero test packages). The app is a single ASP.NET Core
host mixing minimal-API (license verify) and Blazor Server (admin panel),
with no `WebApplicationFactory`-compatible entry-point marker yet.

## Desired End State

A `tests/Lassie.Tests/` xUnit project that builds and runs via
`dotnet test`, containing unit tests for status precedence and key
structural secrecy, plus integration tests (against a real, disposable
Postgres) for verify-endpoint response shape and audit-snapshot
correctness. No manual setup beyond Docker already running locally.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
|---|---|---|---|
| Test-DB strategy | Testcontainers.PostgreSql | Real Postgres semantics (jsonb, true EF change-tracking) match `lessons.md`'s explicit warning against mocked/InMemory providers, with zero manual CI setup. | Plan |
| Test isolation | Per-test transaction rollback | Fast, standard EF Core integration-test pattern, no cross-test pollution. | Plan |
| Project location | `tests/Lassie.Tests/` | Standard .NET src/tests separation. | Plan |
| Risk #2 scope | Backend-only this phase | Matches test-plan's stated purely-backend Phase 1 scope; Blazor UI check deferred to Phase 3 (bUnit). | Plan |
| Risk #1 boundary depth | UTC-day-rollover matrix only | Matches what research actually verified in code; the date-picker's timezone UX is a separate, uninvestigated question. | Research → Plan |
| Test framework | xUnit v2 (2.9.3), not v3 (4.0.0) | v2 is what `Testcontainers.PostgreSql`/`Microsoft.AspNetCore.Mvc.Testing`/bUnit (needed next phase) are documented against; lower friction over bleeding-edge. | Plan |
| Risk framing correction | Risk #1 is a UTC-day-boundary question, not an instant-level edge | `ExpiresOn` is `DateOnly`, compared via `DateOnly.FromDateTime(DateTime.UtcNow)` — date-only, not instant. | Research |

## Scope

**In scope:**
- New `tests/Lassie.Tests/` project + shared Testcontainers/WebApplicationFactory fixtures
- Unit tests: `License.Status` precedence + boundary matrix; `ApiKeyHasher` structural properties
- Integration tests: verify-endpoint response-shape secrecy guard; audit snapshot correctness (edit + deactivate paths)
- `Program.cs` gets a `public partial class Program;` marker (additive only)
- `test-plan.md` §6.1/§6.2 cookbook update (final phase)

**Out of scope:**
- Blazor `CreateLicense.razor` reveal-once UI test (→ rollout Phase 3, bUnit)
- Risk #3 (outage vs. invalid), Risk #4 (API key IDOR) (→ rollout Phase 2)
- CI wiring for `dotnet test` (→ rollout Phase 4)
- Date-picker UTC-semantics UX question (documented, not resolved)

## Architecture / Approach

One Testcontainers-provisioned Postgres container, started once per test
run via an xUnit collection fixture. Each integration test opens its own
connection + transaction against that container, and a custom
`WebApplicationFactory<Program>` is rebound to that same connection so
HTTP-driven writes participate in the same transaction — rolled back after
each test for isolation. Unit tests (status precedence, key-hasher
properties) need no DB at all.

## Phases at a Glance

| Phase | What it delivers | Key risk |
|---|---|---|
| 1. Scaffold + fixtures | Test project, Testcontainers fixture, WebApplicationFactory, transaction-scoped base class | Getting connection-sharing right for rollback isolation to actually isolate HTTP-driven writes |
| 2. Risk #1 tests | Status-precedence + UTC-day-boundary unit tests | None significant — pure unit tests, no DB |
| 3. Risk #2 tests | Key-hasher unit tests + verify-endpoint secrecy integration test | Keeping the DTO-leak assertion structural, not a brittle fixed-string check |
| 4. Risk #5 tests + cookbook | Audit-snapshot integration tests, `test-plan.md` §6 update | None significant — pattern already proven in Phase 1's fixtures |

**Prerequisites:** Docker available locally (already required for `docker-compose.dev.yml`).
**Estimated effort:** ~1 session across 4 phases — small, well-grounded scope.

## Open Risks & Assumptions

- Connection-sharing between the test's transaction and `WebApplicationFactory`'s DI-resolved `DbContext` instances is the one genuinely fiddly piece — if it's wrong, tests silently stop isolating rather than failing loudly. Phase 1's smoke test is designed to catch this early.
- The date-picker UTC-semantics UX question (surfaced by research, not resolved here) may warrant its own future change if an admin ever reports expiry-timing confusion.

## Success Criteria (Summary)

- `dotnet test tests/Lassie.Tests/Lassie.Tests.csproj` passes end-to-end with no manual DB setup beyond Docker running
- All three target risks have a regression test that would fail if the corresponding current-correct behavior ever regressed
- `test-plan.md` §6.1/§6.2 give a future contributor enough to add a new unit or integration test without re-deriving the fixture pattern
