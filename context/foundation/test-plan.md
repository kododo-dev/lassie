# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-08-18

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic visual diff that already catches
   the regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team
   is worried about X, and the failure would surface somewhere in area Y"
   carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `src/` (excl. `bin/`, `obj/`,
`.idea/`, generated `Migrations/*Designer.cs`/`*ModelSnapshot.cs`) — 33
commits in the last 30 days, concentrated in `src/Components/Pages/` (26),
`src/Data/` (21 across `Licenses/`, `LicenseFields/`, `Auditing/`), and
`src/` root (21, `Program.cs`/`lassie.csproj`).

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|---|---|---|---|
| 1 | Verify API or panel computes the wrong license validity — status precedence (Deactivated > Expired > Active) miscomputed at a boundary (e.g. expiry exactly "now") | High | High | PRD FR-007, FR-010; hot-spot dir `src/Data/Licenses/` (7 commits/30d, touched across 3 prior slices); interview Q1 |
| 2 | Generated API key leaks in plaintext after the initial reveal-once display — reappears in a panel re-render, a list/edit response, or a log line | High | Medium | PRD Non-Functional Requirements (key-secrecy line); interview Q1 |
| 3 | A Lassie outage or DB failure is misread by the verify API as "license invalid" instead of "service unavailable", causing a client app to treat downtime as revocation | High | Medium | PRD NFR guardrail (explicit outage-vs-invalid distinction); interview Q1 |
| 4 | Verify API leaks another license's data, or accepts a malformed/missing API key as valid (IDOR-class access-control gap on the machine-to-machine boundary) | High | Medium | PRD FR-009 acceptance criteria ("niepoprawny/brakujący klucz zwraca błąd autoryzacji"); abuse/security lens (mandatory — product has a machine-auth boundary) |
| 5 | A future write path skips load-before-mutate and silently corrupts the audit "before" snapshot, breaking the no-destructive-overwrite guarantee | High | Medium | `context/foundation/lessons.md` ("Audit snapshots require load-before-mutate"); PRD FR-006 |
| 6 | Cross-license UI race regresses: a pending deactivate/reactivate confirmation on one license's edit page gets applied to a different license after navigation | High | Medium | `context/archive/2026-08-12-license-deactivate-reactivate/plan.md` (confirmed incident, fixed commit `3f625ac`); interview Q2 |
| 7 | Dark/light theme toggle silently no-ops — state changes but the rendered UI never reflects it (MudBlazor provider/render-mode isolation) | Medium | High | interview Q2 (user-reported, currently live: "przełącznik jasny/ciemny nie działał — brak reakcji"); `lessons.md` (same bug class already documented once for a different component); hot-spot dirs `src/Components/Layout/`, `src/Components/Shared/` |

**Impact × Likelihood rubric** — coarse High/Medium/Low so two readers agree on the same row; no finer gradations.

| Rating | Impact | Likelihood |
|--------|--------|------------|
| High   | user loses access, data, or money; failure is publicly visible | area changes weekly, or we have already been burned here |
| Medium | feature degrades, a workaround exists, only some users affected | touched occasionally, has been a source of bugs |
| Low    | cosmetic, easily reverted, no data effect | stable code, rarely touched |

Risk #7 sits at Medium impact (UX degradation, no data/access loss) despite High likelihood (already occurred once per a documented lesson, and the user reports it recurring live) — that combination keeps it below the six High-impact rows.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|---|---|---|---|---|
| #1 | For a matrix of (IsActive × expiry-vs-now, including the boundary instant) the status computation returns the PRD-defined precedence deterministically | "The switch/if-chain in code is the spec" — expected precedence must come from the PRD/roadmap decision, not from reading current output | Exact status-computation entry point, UTC-vs-local comparison, boundary handling at expiry == now | unit (table-driven) | Oracle problem — copying the expected value from the implementation instead of the PRD rule; missing the boundary case |
| #2 | After license creation, no subsequent panel render, API response, or log line ever contains the raw key — only a hash/masked form | "Reveal-once on the create page means we're covered" — must also check list/edit responses and request/exception logging middleware | Key generation/hashing code path, what gets persisted vs. displayed, whether ASP.NET logging middleware could capture request/response bodies | unit + integration | Asserting against the implementation's own hash output instead of a structural property (persisted value ≠ raw generated value; raw value absent from every response DTO) |
| #3 | When the verify endpoint's DB dependency is unavailable, the response is structurally distinguishable (status code/shape) from a normal "license invalid" response | "A caught exception returning some error is good enough" — must confirm the shape actually differs from the invalid-license shape, not just the message text | Current exception handling around the verify endpoint's DB call; the exact shape of a normal invalid-license response | integration (simulated DB failure) | Testing only happy-path + invalid-license paths and never simulating an actual outage; asserting on log text instead of the API contract |
| #4 | A request with license A's key never returns or affects license B's data; missing/malformed/unknown keys return a clean auth-error shape, never 200 and never leaked DB detail | "If the key doesn't match, the query returns null, that's fine" — must verify the null/not-found path returns the documented auth-error shape, not an ambiguous empty 200 | How the verify endpoint looks up a license by key, the exact response shape for auth-failure vs. success vs. invalid-license | integration (valid key A, valid key B, missing key, garbage key) | Testing only "valid key → correct status" and skipping adversarial inputs; asserting error message text instead of status code/shape |
| #5 | For the current auditable write path, the audit log's "before" snapshot always reflects the true persisted prior state | "License is the only auditable entity today so we're covered" — the rule is entity-agnostic; treat this test as the reference pattern any future entity must match, not a one-off | `LassieDbContext.SaveChanges` override, how `OriginalValues` is populated, whether enforcement is structural or by convention only | integration (real/test DbContext, loaded-then-modified vs. attach-and-mark-dirty comparison) | Unit-testing the `SaveChanges` override with a mocked `ChangeTracker` — doesn't exercise real EF change-tracking semantics, exactly what would miss this bug |
| #6 | Rapidly navigating from license A's edit page to license B's while a deactivate/reactivate confirmation is pending never applies the confirmed value to the wrong license | "The fix commit means this is solved forever" — a future refactor of the edit page could silently drop the Id-capture guard with nothing to catch it | The guard added in commit `3f625ac` (Id capture + comparison), component lifecycle sequencing (`OnParametersSetAsync` vs. the pending await) | component test (bUnit or equivalent) | Asserting the guard's own condition (oracle problem) instead of the observable outcome (license B unaffected by license A's pending confirmation) |
| #7 | Clicking the theme toggle changes the rendered theme within the same interaction, verified against actual rendered output — not just that the underlying bool flipped | "The property setter fires, so it works" — the reported bug is exactly a case where the property changes but the UI never reflects it across a render-mode boundary | Render-mode boundaries between the toggle component and the theme provider; how the state-change notification propagates | This is the one risk where a plain component/unit test is likely insufficient — cheapest *sufficient* layer needs confirming (component test spanning both render-mode hosts, or a minimal rendered/browser check) | Testing the theme-state property in isolation (passes trivially, doesn't catch the real bug); treating this as low priority because it "sounds cosmetic" |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|---|---|---|---|---|---|
| 1 | Backend critical-path coverage | Bootstrap the test project and defend the highest-severity, purely-backend risks first | #1, #2, #5 | unit + integration | change opened | `context/changes/testing-backend-critical-path-coverage/` |
| 2 | Verification API boundary & abuse surface | Lock down the north star's continuously-hit external contract against outages and adversarial input | #3, #4 | integration | not started | — |
| 3 | Panel UI regression guard | Pin the confirmed cross-license race fix and resolve the live theme-toggle regression | #6, #7 | component (bUnit) + browser smoke (Playwright) | planned | `context/changes/panel-ui-regression-guard/` |
| 4 | Quality-gates wiring | Wire `dotnet test` into the existing GitHub Actions deploy workflow as a required pre-deploy gate | cross-cutting | gates | not started | — |

**Status vocabulary** (fixed — parser literals): `not started` → `change opened` → `researched` → `planned` → `implementing` → `complete`.

## 4. Stack

No test project exists yet (`src/lassie.csproj` has no test-related package
references, and no `*.Tests.csproj` exists in the repo). Phase 1 bootstraps
the runner.

| Layer | Tool | Version | Notes |
|---|---|---|---|
| unit + integration | xUnit | latest stable for net10.0 | Standard .NET test runner; pairs with `Microsoft.AspNetCore.Mvc.Testing`/`WebApplicationFactory` for integration tests against the minimal-API verify endpoint. None yet — see Phase 1. |
| component (Blazor) | bUnit | 2.9.0 | Renders `EditLicense.razor` in-process against `Services.AddMudServices()` + `JSInterop.Mode = JSRuntimeMode.Loose`; covers Risk #6 (cross-license race). Shipped in Phase 3 — see §6.3. |
| e2e / browser smoke | Playwright (`@playwright/test`) | ^1.55.0, in `e2e/` | Needed because Risk #7 crosses a render-mode boundary bUnit's in-process render can't span — confirmed by a deliberate-break check during Phase 3 (see §6.6). Shipped in Phase 3 — see §6.4. |
| AI-native | none | n/a | Not justified under cost × signal for this project's size (single admin, small surface) — no AI-native row proposed in this rollout. |

**Stack grounding tools (current session):**
- Docs: none available in current session (no Context7 or framework-docs MCP exposed) — Phase 1/2/3 planning should verify exact xUnit/bUnit package names and net10.0 compatibility via official docs or `dotnet add package` search before locking versions; checked: 2026-08-18
- Search: none available in current session (no Exa.ai or equivalent MCP; generic WebSearch/WebFetch tools exist but were not used for this grounding pass) — checked: 2026-08-18
- Runtime/browser: none available in current session (no Playwright/browser MCP) — not used; Phase 3's browser-smoke question is deferred to that phase's research step; checked: 2026-08-18
- Provider/platform: none connected relevant to this stack (a Supabase MCP is present but unauthenticated and unrelated — this project uses raw Postgres via Npgsql/EF Core) — not used; checked: 2026-08-18

## 5. Quality Gates

The full set of gates that must pass before a change reaches production.
"Required for §3 Phase <N>" means the gate is enforced once that rollout
phase lands; before that, the gate is `planned`.

| Gate | Where | Required? | Catches |
|---|---|---|---|
| build (`dotnet build`) | local + CI | required (already wired — `.github/workflows/deploy.yml`) | compile/type drift |
| unit + integration (`dotnet test`) | local + CI | required after §3 Phase 1 | logic regressions (status precedence, key secrecy, audit correctness, verify-endpoint contract) |
| component tests (bUnit) | local + CI | required after §3 Phase 3 | Blazor/MudBlazor UI-interaction regressions |
| browser smoke (theme toggle, Playwright) | manual pre-deploy (`npx playwright test` from `e2e/`) | optional — not yet wired into CI (see §3 Phase 4) | render-mode-boundary bugs component tests can't reach |
| pre-prod smoke | between merge + prod | optional | environment-specific failures on the live VPS deploy |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a unit test

- **Location**: `src/Lassie.Tests/<Area>/` (e.g. `Licenses/`, `Auditing/`) — a
  folder per feature area, mirroring `src/Data/<Area>/`.
- **Naming**: `<Subject>Tests.cs`, one test class per subject under test.
- **Reference test**: `src/Lassie.Tests/Licenses/LicenseStatusTests.cs` —
  table-driven (`[Theory]`/`[MemberData]`) coverage with literal expected
  values sourced from the PRD/a lesson, never re-derived from the
  implementation's own comparison (the oracle-problem guard). See also
  `src/Lassie.Tests/Licenses/ApiKeyHasherTests.cs` for plain `[Fact]`-style
  structural-invariant tests (no DB, no `[Theory]` needed).
- **Run**: `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter <ClassName>`

### 6.2 Adding an integration test

- **Location**: same convention as unit tests — `src/Lassie.Tests/<Area>/`.
- **Pattern**: extend `Lassie.Tests.Infrastructure.IntegrationTestBase` and
  tag the class `[Collection("Postgres")]`. The base class gives you a
  `Factory` (`LassieWebApplicationFactory`), an `HttpClient`, and a
  `DbContext` (`LassieDbContext`) — all bound to one Testcontainers-provisioned
  Postgres container shared for the whole test run
  (`Lassie.Tests.Infrastructure.PostgresCollectionFixture`, started once via
  the `[CollectionDefinition("Postgres")]`). Each test class instance opens
  its own connection and transaction on `InitializeAsync` and rolls it back
  on `DisposeAsync`, so tests never see each other's writes — including
  writes made through `HttpClient` calls that hit the app's own
  DI-constructed `LassieDbContext` inside a request, which is enlisted into
  the same test transaction via an `IStartupFilter` middleware (see
  `LassieWebApplicationFactory.cs` for why: EF Core requires every
  additional `DbContext` sharing a connection to explicitly call
  `Database.UseTransactionAsync`, it isn't automatic).
- **Caveat — the seeded admin user is not rolled back**: `IntegrationTestBase`
  forces host startup (`Migrate()` + the admin-user seed) *before* the
  per-test transaction begins, so that the app's own migration transactions
  never nest inside the test's. That means the seed is a real, non-rolled-back
  commit against the collection-shared container: the first test in a run
  permanently seeds an admin `User` row that every later test in the same
  run will also see. Don't assert `Users` is empty in a fresh integration
  test — it may not be, depending on test execution order.
- **Mocking policy**: never mock `LassieDbContext` or the `ChangeTracker` —
  always the real Testcontainers Postgres instance. This is the direct
  enforcement mechanism for the lesson behind Risk #5
  (`context/foundation/lessons.md`, "Audit snapshots require
  load-before-mutate") — a mocked `ChangeTracker` can't reproduce
  `OriginalValues` semantics, so it would let a future load-before-mutate
  regression pass silently.
- **Reference test**: `src/Lassie.Tests/Auditing/AuditLoadBeforeMutateTests.cs`
  (real load-then-mutate-then-assert-snapshot flow) and
  `src/Lassie.Tests/Licenses/VerifyEndpointKeySecrecyTests.cs` (seeds via
  `DbContext`, asserts via `HttpClient` — the reference pattern for testing
  the minimal-API verify endpoint).
- **Run**: `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter <ClassName>`

### 6.3 Adding a component (Blazor) test

- **Location**: `src/Lassie.Tests/Components/` — mirrors `src/Components/`.
- **Naming**: `<Component>Tests.cs`, one test class per component under test.
- **Setup**: derive from bUnit's `BunitContext` (bunit 2.x). In the
  constructor, call `Services.AddMudServices()` first, then register any
  fakes for MudBlazor-adjacent services (e.g. `Services.AddScoped<IDialogService>(_ => dialogService)`)
  — later registrations win for scoped/singleton resolution, so fakes must
  come *after* `AddMudServices()`. Set `JSInterop.Mode = JSRuntimeMode.Loose`
  so the JS interop calls MudBlazor components make for ripple/positioning
  effects no-op instead of throwing.
- **Dialog-dependent components**: hand-write a minimal fake (e.g.
  `Lassie.Tests.Infrastructure.FakeDialogService`) implementing only the one
  `IDialogService` overload the component under test calls as a normal
  method; every other member is an explicit interface implementation that
  throws `NotSupportedException`. Expose a `TaskCompletionSource<bool?>` the
  test controls directly, so a test can render, trigger the dialog, do
  something *while it's still open* (e.g. re-render with different
  parameters to simulate Blazor Server reusing a component instance across
  navigation), and only then resolve the dialog — this is what makes a race
  reproducible, not just the dialog's eventual result. Don't reach for a
  mocking library (Moq/NSubstitute) for this — a hand-written fake is
  smaller and the interface surface is tiny.
- **DB-backed components (non-audit paths only)**: seed a real
  `LassieDbContext` backed by `Microsoft.EntityFrameworkCore.InMemory`
  (unique `Guid`-named database per test), registered via
  `Services.AddSingleton(dbContext)`. This is narrower than §6.2's
  "never mock `LassieDbContext`" policy: InMemory is acceptable *only* when
  the component under test never calls `SaveChangesAsync` on an auditable
  entity (InMemory doesn't reproduce `OriginalValues`/`ChangeTracker`
  semantics behind the load-before-mutate audit guarantee — see Risk #5).
  For anything that saves and must have its audit snapshot verified, use
  §6.2's Testcontainers-backed integration test instead.
- **Teardown**: if `Services.AddMudServices()` is in play, implement xUnit's
  `IAsyncLifetime` and dispose the `BunitContext` via its
  `IAsyncDisposable` (`await ((IAsyncDisposable)this).DisposeAsync()`) —
  MudBlazor registers at least one internal service
  (`PointerEventsNoneService`) as `IAsyncDisposable`-only, which a plain
  synchronous `Dispose()` can't tear down cleanly.
- **Reference test**: `src/Lassie.Tests/Components/EditLicenseTests.cs` (race
  regression + confirm/cancel sanity checks for Risk #6) and
  `src/Lassie.Tests/Infrastructure/FakeDialogService.cs` (the fake pattern
  above).
- **Run**: `dotnet test src/Lassie.Tests/Lassie.Tests.csproj --filter <ClassName>`

**When a component test isn't enough** (Risk #7): a render-mode boundary
(e.g. between a per-page `@rendermode`-consuming component and a shared
provider like `MudProviders.razor`) can't be reproduced by a single bUnit
render tree — bUnit renders one component graph in-process, not two
independently-hosted render scopes. That gap is covered by a real browser
test instead — see the Playwright setup below.

### 6.4 Adding a browser (Playwright) test

- **Location**: `e2e/*.spec.ts`, project root sibling `e2e/playwright.config.ts`.
- **Auth**: `e2e/auth.setup.ts` is a Playwright project dependency that logs
  in once and saves `storageState`; regular specs reuse that state instead
  of logging in per-test.
- **Locators**: `getByRole`/`getByLabel`/`getByText` only — never CSS
  selectors, XPath, or DOM structure (see root `CLAUDE.md`). If a target
  element has no accessible name, that's usually a real accessibility gap
  worth fixing in the component (e.g. `AppBarActions.razor`'s theme-toggle
  button needed an `aria-label` added for this reason), not a reason to fall
  back to a `data-testid`.
- **Waits**: wait for state (`toBeVisible()`, `waitForURL()`,
  `waitForResponse()`), never `page.waitForTimeout()`.
- **Independence**: every spec creates its own data with a
  `Date.now()`-suffixed label and doesn't depend on another spec's leftover
  state; the app has no delete feature, so specs that create a license (e.g.
  the seed test) treat an unused, uniquely-labeled leftover license as an
  acceptable terminal state rather than attempting deletion-based cleanup.
- **Reference tests**: `e2e/theme-toggle.spec.ts` (Risk #7 — verified with a
  deliberate-break check, see §6.6) and `e2e/seed.spec.ts` (the four E2E
  quality patterns — role-based locators, independence, wait-for-state,
  risk-tied naming — demonstrated against a real create-license flow; the
  exemplar future generated tests should be modeled on).
- **Run**: from `e2e/`, `npx playwright test` (all specs) or
  `npx playwright test <file>.spec.ts` against the app running on
  `http://localhost:5092`.

### 6.5 Adding a test for a new API endpoint

- TBD — see §3 Phase 2 (establishes the reference pattern for testing minimal-API endpoints against `LassieDbContext`).

### 6.6 Per-rollout-phase notes

(Filled in by `/10x-implement` as each phase ships.)

- **Phase 1** (`testing-backend-critical-path-coverage`): the test project
  was relocated mid-phase, at the user's request, from the plan's original
  `tests/Lassie.Tests/` to `src/Lassie.Tests/` (sibling of `src/lassie.csproj`),
  with a new `src/lassie.slnx` solution file referencing both projects.
  Every path in §6.1/§6.2 above reflects the actual `src/Lassie.Tests/`
  location. One consequence: `src/lassie.csproj` needed explicit
  `<Compile Remove="Lassie.Tests/**" />` (+ `Content`/`EmbeddedResource`/`None`)
  entries, since the test project now sits *inside* the app project's own
  directory and would otherwise be swept up by its implicit globs.
- **Phase 3** (`panel-ui-regression-guard`): bUnit lives in the existing
  `src/Lassie.Tests` project, not a separate csproj (same rationale as
  Phase 1's single-project layout). `FakeDialogService` is hand-written, not
  built on a mocking library (Moq/NSubstitute) — the `IDialogService`
  surface actually exercised is one method. EF Core InMemory is scoped
  narrowly to component tests that don't call `SaveChangesAsync` on an
  auditable entity (see §6.3) — it does not loosen §6.2's
  never-mock-`LassieDbContext` policy for integration tests, since InMemory
  doesn't reproduce `OriginalValues`/`ChangeTracker` semantics. Risk #6's
  race test was deliberate-break checked: reverting `EditLicense.razor`'s
  `if (license?.Id != licenseId) { return; }` guard to a no-op made the race
  test fail; restoring it made it pass again. Risk #7's Playwright work
  (render-mode-boundary theme-toggle bug) predates this change folder — it
  was implemented and deliberate-break checked (temporarily removing
  `ThemeState.OnChange += StateHasChanged;` in `MudProviders.razor`
  reproduced the exact "state flips, UI doesn't" bug) in a standalone
  `/10x-e2e` run before `panel-ui-regression-guard` existed, and formalized
  (committed, seed test added) by this phase.

## 7. What We Deliberately Don't Test

- User explicitly declined to name exclusions when asked (Phase 2 interview Q5: "test it all seriously") — no negative-space carve-outs are recorded by request.
- The parked module-catalog work (`LicenseField`/`LicenseFieldOption`, roadmap `S-01`) is not shipped code — nothing to test there, not a deliberate skip. Re-evaluate if `S-01` is picked back up (see roadmap `## Parked`).

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-08-18
- Stack versions last verified: 2026-08-21
- AI-native tool references last verified: n/a (none proposed)

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
