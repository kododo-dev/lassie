# Panel UI Regression Guard Implementation Plan

## Overview

Close out Phase 3 of `context/foundation/test-plan.md` ("Panel UI regression guard"): protect risk #6 (a pending deactivate/reactivate confirmation getting applied to the wrong license after navigation) with a new bUnit component-test layer, and formalize risk #7's already-implemented, already-verified Playwright test (theme toggle silently no-oping across a MudBlazor render-mode boundary) into this change's history and the project's test documentation.

## Current State Analysis

- **Risk #6's guard already exists in production code.** `EditLicense.razor`'s `HandleIsActiveChanged` (line 138) has `if (license?.Id != licenseId) { return; }` — added in commit `3f625ac` (`context/archive/2026-08-12-license-deactivate-reactivate/plan.md`) specifically to stop a confirmed dialog result from one license's Id being applied after Blazor Server reuses the same component instance across a navigation to a different license's edit URL. This guard has **no automated test** — it is exactly the kind of fix a future refactor could silently drop.
- **No component-test layer exists yet.** `src/Lassie.Tests/Lassie.Tests.csproj` has xUnit + `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql` for unit/integration tests (Phase 1 of the test-plan), but no bUnit reference and no tests under a `Components/` folder.
- **Risk #7's E2E layer is already done, but unrecorded.** In a prior standalone `/10x-e2e` run (before this change folder existed), the following were created/fixed and manually verified (including a deliberate-break check — temporarily removing `ThemeState.OnChange += StateHasChanged;` in `MudProviders.razor` reproduced the exact "state flips, UI doesn't" bug, the new test went red, the removal was reverted, and the test went green again) but never committed:
  - `src/Components/Layout/AppBarActions.razor` — added `aria-label="Toggle dark mode"` to the theme-toggle `MudIconButton` (it had no accessible name at all, so `getByRole` couldn't target it — a real accessibility gap, not just a testability one).
  - `e2e/playwright.config.ts` — fixed a missing `import { defineConfig } from '@playwright/test'` and added `testDir`/`baseURL`.
  - `e2e/auth.setup.ts` — new, standard Playwright `storageState` login flow.
  - `e2e/theme-toggle.spec.ts` — new, the risk #7 regression test.
  - `e2e/package.json` / `package-lock.json` — local `@playwright/test` install (none existed at the repo level).
  - `.gitignore` — entries for `e2e/.auth/`, `e2e/node_modules/`, `e2e/test-results/`, `e2e/playwright-report/`, `e2e/.playwright-cli/`.
- **The E2E rules lever already exists** — root `CLAUDE.md`'s "10xDevs AI Toolkit - Module 3, Lesson 4 (E2E Tests)" section already carries the locator/wait/independence rules the `/10x-e2e` skill treats as the "rules" quality lever. Only the **seed test** lever (`e2e/seed.spec.ts`) is genuinely missing.
- **`context/foundation/test-plan.md` still describes both layers as absent**: §4 Stack lists bUnit as "None yet — see Phase 3" and e2e/browser smoke as "Only being considered for Risk #7 if research confirms..." — both are now settled facts, not open questions. §6.3 (component test cookbook) and §6.4 are still `TBD`.
- **The "never mock `LassieDbContext`" rule (§6.2, protecting risk #5) doesn't block this plan.** That rule protects the audit `SaveChanges`/`OriginalValues` mechanism specifically. Risk #6's bUnit test never calls `SaveChangesAsync` — it only exercises `OnParametersSetAsync`'s read (`SingleOrDefaultAsync`) and in-memory `Model` state before Save. An EF Core InMemory provider (a real `DbContext`/`ChangeTracker`, just a different storage backend — not a mock) is sufficient and appropriate here, and doesn't touch the concern that rule exists to protect.

## Desired End State

- `dotnet test src/Lassie.Tests/Lassie.Tests.csproj` includes two new bUnit tests under `src/Lassie.Tests/Components/EditLicenseTests.cs` that pass, one of which fails if the `license?.Id != licenseId` guard in `EditLicense.razor` is removed.
- `e2e/theme-toggle.spec.ts` and its supporting files are committed, along with a new `e2e/seed.spec.ts` demonstrating the four quality patterns from `references/seed-test-pattern.md` against a real Lassie flow.
- `context/foundation/test-plan.md` §4/§6.3/§6.5/§8 reflect the now-real bUnit and e2e stack, and §3's Phase 3 row points at this change folder with status `planned` (already updated below, ahead of `/10x-implement`).

**Verification**: `dotnet build src/lassie.csproj` and `dotnet test src/Lassie.Tests/Lassie.Tests.csproj` succeed; `npx playwright test` (from `e2e/`) passes both specs against a running app; manual confirmation per phase below.

### Key Discoveries:

- `src/Components/Pages/EditLicense.razor:114-144` (`HandleIsActiveChanged`) — the exact race window: the dialog `await` at line 121-125 is where a navigation to a different license's edit URL can land between the confirm click and the guard check at line 138.
- `src/Components/Pages/EditLicense.razor:72-98` (`OnParametersSetAsync`) — reused-instance-across-navigation behavior the race depends on; re-invoking this on the same component instance (via bUnit's `SetParametersAndRender`) is how the test reproduces the race without a real router.
- `src/Components/Shared/MudProviders.razor:14-17` — `ThemeState.OnChange += StateHasChanged` is the exact line the deliberate-break check for risk #7 targeted; confirms this is the right assertion target for future regressions of the same bug class.
- `context/foundation/lessons.md` — "MudBlazor providers must live in the same render scope as per-page `@rendermode` consumers" (the mechanism risk #7 depends on) and "Audit snapshots require load-before-mutate" (the mechanism risk #6's bUnit test must NOT be confused with — see Current State Analysis above).

## What We're NOT Doing

- Not wiring `dotnet test` or `npx playwright test` into CI — that's `test-plan.md` §3 Phase 4 ("Quality-gates wiring"), a separate rollout phase.
- Not adding a second E2E rules file — `CLAUDE.md` already carries the rules lever; duplicating it would just create a second source of truth to keep in sync.
- Not re-deriving or re-verifying risk #7's fix itself — that was already done (deliberate-break checked) in the prior standalone session. This plan formalizes it, it doesn't redo it.
- Not adding a mocking library (Moq/NSubstitute) — a hand-written `FakeDialogService` covers the one method under test.
- Not testing `EditLicense.razor`'s Save/audit path via bUnit — that path is already covered by existing integration tests (`AuditLoadBeforeMutateTests.cs`) and is out of scope for risk #6, which is purely about pre-Save in-memory state.
- Not adding manual browser verification for the bUnit phase — reproducing an async race reliably by hand isn't a meaningful signal; `dotnet test` passing (and failing under a deliberate break) is the verification.

## Implementation Approach

Two independent test-layer deliverables (bUnit for #6, formalizing already-done Playwright work for #7), landing as separate phases so each gets its own commit and manual-verification checkpoint, followed by a documentation-only phase syncing `test-plan.md` to the now-real stack — mirroring how Phase 1 (`testing-backend-critical-path-coverage`) closed out its own `test-plan.md` §6.5 note after implementation.

## Critical Implementation Details

**The race test must control dialog completion timing, not just its result.** A dialog fake that resolves immediately (e.g. `Task.FromResult<bool?>(true)`) can't reproduce the race — the whole point is that the navigation happens *while the dialog is still open*. Use a `TaskCompletionSource<bool?>` the test holds a reference to, so the sequence is: click switch off → `HandleIsActiveChanged` awaits the fake's task → test calls `SetParametersAndRender` with a different `Id` on the same rendered component (simulating Blazor Server's instance reuse) → test completes the `TaskCompletionSource` with `true` → assert the *second* license's state was not touched.

**DI registration order for the dialog fake.** bUnit's `Services.AddMudServices()` registers MudBlazor's real `DialogService` as `IDialogService`. Register the `FakeDialogService` *after* `AddMudServices()` in the test's service collection — last registration wins for constructor-injected singletons/scoped resolution in `Microsoft.Extensions.DependencyInjection`.

**JSInterop must be loose, not strict.** MudBlazor components (`MudSwitch`, `MudThemeProvider`, `MudPopoverProvider`, `MudDialogProvider` — all rendered via `<MudProviders />` inside `EditLicense.razor`) call JS interop for ripple/positioning effects that bUnit can't fulfill. Set `ctx.JSInterop.Mode = JSRuntimeMode.Loose` so unhandled JS calls no-op instead of throwing. Since the fake `IDialogService` intercepts `ShowMessageBoxAsync` before it ever reaches the real `MudDialogProvider`, that component's own popover rendering is never functionally exercised — it only needs to not throw.

## Phase 1: bUnit infrastructure and cross-license race regression (risk #6)

### Overview

Add bUnit to the existing `Lassie.Tests` project and write two tests against `EditLicense.razor`: the risk #6 race guard, and a sanity check of the ordinary confirm/cancel dialog flow it sits alongside.

### Changes Required:

#### 1. Test project dependencies

**File**: `src/Lassie.Tests/Lassie.Tests.csproj`

**Intent**: Add the packages needed to render and interact with a Blazor component under test, and to seed a real (non-mocked) `LassieDbContext` for `OnParametersSetAsync`'s read.

**Contract**: Add `PackageReference` entries for `bunit` (`2.9.0`) and `Microsoft.EntityFrameworkCore.InMemory` (pinned to `10.0.10`, matching the existing `Microsoft.EntityFrameworkCore`/`Microsoft.EntityFrameworkCore.Relational` pins and their comment about matching `src/lassie.csproj`'s floor). No `ProjectReference` changes needed — `MudBlazor` and `Lassie`'s own types are already reachable via the existing reference to `../lassie.csproj`.

#### 2. Fake dialog service

**File**: `src/Lassie.Tests/Infrastructure/FakeDialogService.cs`

**Intent**: A minimal `IDialogService` test double whose `ShowMessageBoxAsync` overload (matching the one `EditLicense.razor` calls: title, message, `yesText:`, `cancelText:`) returns a `Task<bool?>` the test controls via an exposed `TaskCompletionSource<bool?>`. Every other `IDialogService` member throws `NotSupportedException` — nothing else in scope calls them.

**Contract**: `public TaskCompletionSource<bool?> MessageBoxResult { get; } = new();` plus the one implemented method returning `MessageBoxResult.Task`. Reusable by both tests in this phase (the sanity test can pre-complete the source before invoking the switch, since it doesn't need mid-await control).

#### 3. Component tests

**File**: `src/Lassie.Tests/Components/EditLicenseTests.cs`

**Intent**: Prove the cross-license race guard holds, and that the ordinary deactivate confirm/cancel flow it's embedded in still works.

**Contract**: A bUnit `TestContext`-derived test class (xUnit `[Fact]`s, one class = one subject under test, per `test-plan.md` §6.1 convention). Setup shared by both tests: `Services.AddMudServices()`, `JSInterop.Mode = JSRuntimeMode.Loose`, an `Microsoft.EntityFrameworkCore.InMemory`-backed `LassieDbContext` (unique database name per test) seeded with two `License` rows, a `FakeDialogService` registered after `AddMudServices()`.
- `Deactivating license A does not affect license B when navigation lands mid-confirmation` (name binds to risk #6): render `EditLicense` with `Id` = license A's id; toggle the switch off (find and change the underlying switch input) — this calls into the fake dialog and blocks on its `Task`; call `SetParametersAndRender` with `Id` = license B's id on the same render handle; complete `MessageBoxResult` with `true`; await settling; assert license B's rendered switch still reads "Active" (the label the component derives from `Model.IsActive`).
- `Confirming the deactivate dialog turns the switch to Inactive` (sanity): pre-complete `MessageBoxResult` with `true` before toggling; toggle the switch off; assert the rendered switch now reads "Inactive". A `Cancel` variant (`MessageBoxResult` completed with `false`) asserting the switch stays "Active" may be folded into the same test or a second `[Fact]` — implementer's call, both assertions are cheap given the shared setup.

### Success Criteria:

#### Automated Verification:

- `dotnet build src/lassie.csproj` succeeds
- `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter EditLicenseTests` passes
- Deliberate-break check: temporarily reverting `EditLicense.razor`'s guard (`if (license?.Id != licenseId) { return; }`) to a no-op makes the race test fail; restoring the guard makes it pass again (confirmed once during implementation, not left in the tree)

#### Manual Verification:

(none — see "What We're NOT Doing")

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: E2E theme-toggle formalization (risk #7)

### Overview

Commit the already-implemented, already-verified risk #7 Playwright test and its supporting infrastructure (all currently uncommitted from a prior standalone `/10x-e2e` run), and add the one genuinely missing quality lever: a seed test.

### Changes Required:

#### 1. Existing risk #7 artifacts (already implemented, delivered by this phase's commit)

**Files**: `src/Components/Layout/AppBarActions.razor`, `e2e/playwright.config.ts`, `e2e/auth.setup.ts`, `e2e/theme-toggle.spec.ts`, `e2e/package.json`, `e2e/package-lock.json`, `.gitignore`

**Intent**: No new work — these already exist in the working tree from the prior standalone session (see Current State Analysis) and are verified (deliberate-break checked). This phase's job is to record them as this change's Phase 2 deliverable and get them committed.

**Contract**: No content changes. `git status` should show these as the only pre-existing untracked/modified files relevant to this phase when Phase 2 starts.

#### 2. Seed test

**File**: `e2e/seed.spec.ts`

**Intent**: The one missing E2E quality lever (`references/seed-test-pattern.md`) — the exemplar future generated tests are modeled on. Demonstrate all four patterns (role-based locators, test independence, wait-for-state not time, risk-tied naming) against a real, cheap Lassie flow rather than a placeholder.

**Contract**: Model directly on the reference exemplar, adapted to this app: create a license via `/licenses/new` (`CreateLicense.razor`) with a `Date.now()`-suffixed label, assert it's `getByRole('cell', ...)`-visible on `/` (`PanelHome.razor`'s table) after a `page.reload()`. The app has no delete feature (see `context/foundation/prd.md` Non-Goals / current `PanelHome.razor`), so the reference exemplar's delete-based cleanup doesn't apply as-is — use the deactivate switch (`EditLicense.razor`) as the closest available teardown, or note inline why an unused, uniquely-labeled test license is an acceptable terminal state for this app (implementer's call; either is consistent with test independence — no other test depends on the license *not* existing).

### Success Criteria:

#### Automated Verification:

- `npx playwright test` (from `e2e/`, against a running app on `http://localhost:5092`) passes both `auth.setup.ts` and `theme-toggle.spec.ts`
- `npx playwright test seed.spec.ts` (from `e2e/`) passes

#### Manual Verification:

- Confirm `git status` shows no other unrelated dirty files being swept into this phase's commit

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: test-plan.md documentation sync

### Overview

Update `context/foundation/test-plan.md` so its Stack and Cookbook sections describe the bUnit and e2e layers as they now actually exist, matching the precedent set by Phase 1's §6.5 note.

### Changes Required:

#### 1. Stack table

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the two "none yet" placeholder rows with the real, now-shipped stack.

**Contract**: §4 Stack table — `component (Blazor)` row: `bunit 2.9.0` (was "None yet — see Phase 3"), noting the `AddMudServices()` + `JSInterop.Mode = JSRuntimeMode.Loose` setup pattern. `e2e / browser smoke` row: `Playwright (@playwright/test)`, `e2e/` (was "Only being considered for Risk #7 if research confirms..."), noting risk #7 confirmed the need (component testing can't span the render-mode boundary — see the deliberate-break finding in this change's Phase 2).

#### 2. Cookbook: adding a component test

**File**: `context/foundation/test-plan.md`

**Intent**: Fill in §6.3 (currently `TBD — see §3 Phase 3`) with the concrete pattern this phase establishes, mirroring how §6.1/§6.2 document the unit/integration patterns.

**Contract**: Location (`src/Lassie.Tests/Components/`), the `AddMudServices()` + `JSInterop.Mode = JSRuntimeMode.Loose` setup, the `FakeDialogService` pattern for `IDialogService`-dependent components, and the note about EF Core InMemory being appropriate for component tests that don't exercise `SaveChangesAsync`/audit (contrast with §6.2's Testcontainers-only policy for integration tests). Reference test: `src/Lassie.Tests/Components/EditLicenseTests.cs`.

#### 3. Per-rollout-phase notes and freshness ledger

**File**: `context/foundation/test-plan.md`

**Intent**: Record this phase's implementation decisions for future readers, matching Phase 1's existing note; keep the freshness ledger honest.

**Contract**: §6.5 gains a "Phase 3" bullet summarizing: bUnit lives in the existing `Lassie.Tests` project (not a separate csproj), hand-written `FakeDialogService` over a mocking library, EF Core InMemory scoped narrowly to non-audit component tests, risk #7's E2E work predates this change folder (done in a standalone `/10x-e2e` run, formalized here). §8 Freshness Ledger — bump "Stack versions last verified" to today's date.

#### 4. Phased rollout status

**File**: `context/foundation/test-plan.md`

**Intent**: Point §3's Phase 3 row at this change folder (already done now, ahead of `/10x-implement`, mirroring how `/10x-plan` syncs `roadmap.md` status immediately on plan creation).

**Contract**: §3 Phased Rollout table, Phase 3 row: `Status` → `planned`, `Change folder` → `context/changes/panel-ui-regression-guard/`.

### Success Criteria:

#### Automated Verification:

- None (documentation-only phase)

#### Manual Verification:

- `context/foundation/test-plan.md` §4/§6.3/§6.5/§8 read consistently with Phases 1-2's actual outcome (spot-check after Phase 1/2 land, since exact package versions/file paths must match what was actually implemented)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

N/A — this plan's deliverable is itself test infrastructure (bUnit, Playwright), not application logic.

### Integration Tests:

N/A — see above; risk #6/#7 are component- and browser-level respectively, not integration-level (no new API/DB contract is being tested).

### Manual Testing Steps:

1. After Phase 1: run `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter EditLicenseTests` and confirm both tests pass; temporarily comment out the guard line in `EditLicense.razor`, re-run, confirm the race test fails, then restore the guard.
2. After Phase 2: with the app running on `http://localhost:5092`, run `npx playwright test` from `e2e/` and confirm all specs pass; check `git status` before committing to confirm only Phase 2's intended files are staged.
3. After Phase 3: read through the updated `test-plan.md` sections and confirm they match what actually shipped in Phases 1-2 (package versions, file paths).

## Performance Considerations

None — these are test-only additions; no production code path changes except the already-shipped `aria-label` attribute (Phase 2, cosmetic/accessibility, zero runtime cost).

## Migration Notes

N/A — no schema or data changes.

## References

- Risk source (#6): `context/archive/2026-08-12-license-deactivate-reactivate/plan.md` (guard added in commit `3f625ac`)
- Risk source (#7) and response guidance for both risks: `context/foundation/test-plan.md` §2 (Risk Response Guidance table)
- Lessons: `context/foundation/lessons.md` — "MudBlazor providers must live in the same render scope as per-page `@rendermode` consumers", "Audit snapshots require load-before-mutate"
- E2E skill reference (seed pattern, rules): `.claude/skills/10x-e2e/references/seed-test-pattern.md`, `.claude/skills/10x-e2e/references/e2e-quality-rules.md`
- Prior slice establishing the unit/integration test project: `context/archive/2026-08-18-testing-backend-critical-path-coverage/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: bUnit infrastructure and cross-license race regression (risk #6)

#### Automated

- [x] 1.1 `dotnet build src/lassie.csproj` succeeds — 9261b26
- [x] 1.2 `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter EditLicenseTests` passes — 9261b26
- [x] 1.3 Deliberate-break check: race test fails with the guard removed, passes with it restored — 9261b26

### Phase 2: E2E theme-toggle formalization (risk #7)

#### Automated

- [x] 2.1 `npx playwright test` (auth.setup.ts + theme-toggle.spec.ts) passes against a running app
- [x] 2.2 `npx playwright test seed.spec.ts` passes

#### Manual

- [x] 2.3 `git status` shows no unrelated dirty files swept into this phase's commit

### Phase 3: test-plan.md documentation sync

#### Manual

- [ ] 3.1 Updated test-plan.md sections match what actually shipped in Phases 1-2
