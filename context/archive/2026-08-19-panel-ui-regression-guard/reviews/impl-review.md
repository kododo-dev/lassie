<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Panel UI Regression Guard

- **Plan**: context/changes/panel-ui-regression-guard/plan.md
- **Scope**: Full plan (Phase 1, 2, 3 — all complete)
- **Date**: 2026-08-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — `LassieDbContext` not disposed in `EditLicenseTests`

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Lassie.Tests/Components/EditLicenseTests.cs:22-45
- **Detail**: `dbContext` is constructed manually in the constructor and registered via `Services.AddSingleton(dbContext)` (a pre-built instance). `Microsoft.Extensions.DependencyInjection` does not take disposal ownership of instances passed directly to `AddSingleton(instance)` — it only disposes services it constructs itself. `IAsyncLifetime.DisposeAsync` (line 45) only disposes the bUnit `BunitContext`/service provider, never calls `dbContext.DisposeAsync()`. The project's own integration-test base (`src/Lassie.Tests/Infrastructure/IntegrationTestBase.cs`) explicitly disposes its `DbContext` in teardown — this test doesn't follow that established pattern. Low real-world impact since EF Core InMemory holds no unmanaged handles, but it's an inconsistency that leaks change-tracker state across the run.
- **Fix**: In `EditLicenseTests`'s `IAsyncLifetime.DisposeAsync()`, add `await dbContext.DisposeAsync();` alongside the existing `BunitContext` disposal, matching `IntegrationTestBase`'s pattern.
- **Decision**: FIXED — `await dbContext.DisposeAsync();` added after the `BunitContext` disposal. Verified: `dotnet test --filter EditLicenseTests` still 3/3.

### F2 — `FakeDialogService`'s two events silently no-op instead of throwing

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: src/Lassie.Tests/Infrastructure/FakeDialogService.cs:23-33
- **Detail**: The plan's contract for this file states "every other [`IDialogService`] member throws `NotSupportedException`." All method members do. But the two interface events (`DialogInstanceAddedAsync`, `OnDialogCloseRequested`) are implemented as silent no-op `add {} / remove {}` accessors instead. Nothing in `EditLicense.razor` or the bUnit render path currently subscribes to them, so this is low-risk in practice, and the class carries an explanatory comment — but it's the one place the plan's "no accidental silent no-ops" bar isn't literally met.
- **Fix**: Change both event accessors to `throw new NotSupportedException();`, matching the throwing style used by every other unimplemented member and the plan's literal contract.
- **Decision**: DISMISSED — attempted the fix; it breaks all 3 tests. `MudDialogProvider` (rendered inside `<MudProviders />` in `EditLicense.razor`) subscribes to `DialogInstanceAddedAsync` unconditionally in its `OnInitialized()`, even though nothing in these tests ever raises it. Throwing there blows up every render (`BunitRenderer.AssertNoUnhandledExceptions`). Reverted to the original no-op accessors; strengthened the class-level comment to explain why they must stay no-op rather than throw, so this isn't rediscovered the hard way again. The plan's literal "every other member throws" contract is intentionally not met for these two members — this is a correct deviation, not an oversight.

### F3 — Race test's determinism relies on an undocumented EF InMemory assumption

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Lassie.Tests/Components/EditLicenseTests.cs:47-63
- **Detail**: `DeactivatingLicenseA_DoesNotAffectLicenseB_WhenNavigationLandsMidConfirmation` relies on EF Core InMemory's `SingleOrDefaultAsync` (invoked from `EditLicense.razor`'s `OnParametersSetAsync`) completing synchronously with no thread-hop, so that `cut.Render(p => p.Add(x => x.Id, 2))` (line 57) fully reassigns `license` to license B before `dialogService.MessageBoxResult.SetResult(true)` (line 59) resumes the suspended `HandleIsActiveChanged` continuation. This holds today but is never stated as a load-bearing assumption in the test. The trailing `cut.WaitForAssertion` (line 61) mitigates most flake risk, but a future EF Core InMemory change (or swapping providers in this test) could silently break the scenario without an obvious signal why.
- **Fix**: Add a one-line comment above the `SetResult` call noting that the re-render on line 57 depends on `OnParametersSetAsync`'s query completing synchronously under EF InMemory.
- **Decision**: FIXED — comment added above `SetResult`. Verified: `dotnet test --filter EditLicenseTests` still 3/3.

### F4 — `playwright.config.ts` has no `webServer` block

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: e2e/playwright.config.ts
- **Detail**: There's no `webServer` entry to auto-start or health-check the app before tests run. If the app isn't already running on `http://localhost:5092`, failures surface as opaque connection errors rather than a clear "app not running" message. Not blocking — this mirrors the plan's explicit choice not to wire CI (`## What We're NOT Doing`) — but worth flagging for when Phase 4 (quality-gates wiring) picks this up.
- **Fix**: Defer to Phase 4 (`test-plan.md` §3), which is exactly where CI/local-runner ergonomics for this suite belongs.
- **Decision**: SKIPPED — deferred to Phase 4 (quality-gates wiring) per the plan's own scope boundary.

## Verification evidence (this review)

- `dotnet build src/lassie.csproj` — succeeded, 0 errors.
- `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter EditLicenseTests` — 3/3 passed.
- `npx playwright test` (from `e2e/`, against a live `dotnet run` instance + the existing `lassie-postgres-dev` container) — 3/3 passed (`auth.setup`, `theme-toggle.spec.ts`, `seed.spec.ts`).
- Manual criteria: all three phases' Progress checkboxes are `[x]` with commit shas; `git status` at review time showed no stray files.

## Notes

- Files under `.claude/skills/10x-e2e/`, `.claude/skills/10x-tdd/`, `.claude/prompts/`, `.claude/.10x-cli-manifest.json`, and root `CLAUDE.md` were bundled into commit `9261b26` alongside this plan's Phase 1 changes. This is explicitly disclosed in that commit's message as "unrelated pre-existing dirty state from prior work, not part of this plan," bundled at the user's request — not scope creep needing a decision here.
- `context/foundation/test-plan.md`'s Phase 3 documentation sync went beyond the plan's literal contract (added a full §6.4 "Adding a browser (Playwright) test" cookbook subsection, renumbering old §6.4→§6.5 and §6.5→§6.6). This is beneficial elaboration done in direct continuation of the same session's Phase 3 work, with all cross-references updated consistently — not drift.
