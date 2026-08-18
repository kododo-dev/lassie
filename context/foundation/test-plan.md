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
| 3 | Panel UI regression guard | Pin the confirmed cross-license race fix and resolve the live theme-toggle regression | #6, #7 | component (bUnit) + browser smoke (TBD by research) | not started | — |
| 4 | Quality-gates wiring | Wire `dotnet test` into the existing GitHub Actions deploy workflow as a required pre-deploy gate | cross-cutting | gates | not started | — |

**Status vocabulary** (fixed — parser literals): `not started` → `change opened` → `researched` → `planned` → `implementing` → `complete`.

## 4. Stack

No test project exists yet (`src/lassie.csproj` has no test-related package
references, and no `*.Tests.csproj` exists in the repo). Phase 1 bootstraps
the runner.

| Layer | Tool | Version | Notes |
|---|---|---|---|
| unit + integration | xUnit | latest stable for net10.0 | Standard .NET test runner; pairs with `Microsoft.AspNetCore.Mvc.Testing`/`WebApplicationFactory` for integration tests against the minimal-API verify endpoint. None yet — see Phase 1. |
| component (Blazor) | bUnit | latest compatible with net10.0/MudBlazor 9.x | Needed for Risk #6/#7 component-level tests in Phase 3. None yet — see Phase 3. |
| e2e / browser smoke | none yet | n/a | Only being considered for Risk #7 if research confirms component testing can't span the render-mode boundary — see Phase 3. |
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
| browser smoke (theme toggle) | CI on PR, or manual pre-deploy | optional, decided in Phase 3 | render-mode-boundary bugs component tests can't reach |
| pre-prod smoke | between merge + prod | optional | environment-specific failures on the live VPS deploy |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a unit test

- TBD — see §3 Phase 1 (status-precedence table-driven tests, key-secrecy structural tests).

### 6.2 Adding an integration test

- TBD — see §3 Phase 1 (audit load-before-mutate) and §3 Phase 2 (verify-endpoint outage/abuse tests).

### 6.3 Adding a component (Blazor) test

- TBD — see §3 Phase 3 (cross-license race regression, theme-toggle render-scope).

### 6.4 Adding a test for a new API endpoint

- TBD — see §3 Phase 2 (establishes the reference pattern for testing minimal-API endpoints against `LassieDbContext`).

### 6.5 Per-rollout-phase notes

(Filled in by `/10x-implement` as each phase ships.)

## 7. What We Deliberately Don't Test

- User explicitly declined to name exclusions when asked (Phase 2 interview Q5: "test it all seriously") — no negative-space carve-outs are recorded by request.
- The parked module-catalog work (`LicenseField`/`LicenseFieldOption`, roadmap `S-01`) is not shipped code — nothing to test there, not a deliberate skip. Re-evaluate if `S-01` is picked back up (see roadmap `## Parked`).

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-08-18
- Stack versions last verified: 2026-08-18
- AI-native tool references last verified: n/a (none proposed)

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
