# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Lassie is a solo-built, single-tenant license-management system: an admin panel (email+password auth) for creating and managing licenses, and a machine-to-machine verification API that client applications poll with a per-license API key. Full product intent lives in `context/foundation/prd.md`; the stack rationale is in `context/foundation/tech-stack.md`.

The repo is at the just-scaffolded stage — `src/` currently contains only the default ASP.NET Core `webapi` template (a `WeatherForecast` minimal-API endpoint in `Program.cs`), no domain code yet. Treat the PRD as the source of truth for what to build, not the current code.

Key constraints from the PRD worth carrying into any implementation:
- License API keys must never be exposed in plaintext after initial generation (not in the panel UI, not in logs).
- License edits must retain history for audit (no destructive overwrite).
- The verification API must distinguish "service unavailable" from "license invalid" — callers must not treat a network/outage error as license revocation.
- Verification API responses must return in under 500ms.
- No customer/tenant entity — a license is the flat, standalone unit (see PRD's `Non-Goals`).

## Commands

Project file is at `src/lassie.csproj` (target framework `net10.0`).

```
dotnet build src/lassie.csproj          # build
dotnet run --project src/lassie.csproj  # run (http://localhost:5092, https://localhost:7221)
dotnet watch --project src/lassie.csproj  # run with hot reload
dotnet list src/lassie.csproj package --vulnerable --include-transitive  # dependency audit
```

No test project exists yet. `src/lassie.http` has example requests for use with an HTTP client (VS Code REST Client, Rider, etc.).

One-time setup per clone: run `git config core.hooksPath .githooks` to enable the versioned pre-commit hook (`.githooks/pre-commit.js`) that checks `dotnet format` on staged `.cs` files and runs the fast unit-test subset (`--filter "Category!=Integration"`).

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 3, Lesson 4 (E2E Tests)

**For E2E tests, use the `/10x-e2e` skill.** It is the single source of truth
for the workflow — risk → seed test + rules → generate → review against the five
anti-patterns → re-prompt → verify. The skill's `references/` carry the full
rules, anti-patterns, seed pattern, and prompt-template.

A few hard rules that hold even before you invoke the skill:

- **Locators:** `getByRole` / `getByLabel` / `getByText` first; `getByTestId`
  only when accessibility attributes are ambiguous. Never CSS selectors, XPath,
  or DOM structure.
- **Never `page.waitForTimeout()`.** Wait for state: `toBeVisible()`,
  `waitForURL()`, `waitForResponse()`.
- **Test independence + cleanup.** Each test runs standalone — its own setup,
  action, assertion, and cleanup; unique ids (timestamp suffix) so parallel runs
  and re-runs don't collide.

Two boundaries to keep straight:

- **DOM (snapshot) is the default.** Vision (`--caps=vision`) is a supplement for
  visual-only risks (layout, z-index, animation); for pixel regression prefer
  deterministic tools (`toMatchSnapshot`, Argos, Lost Pixel). VLM model
  selection/cost is a debugging topic (Lesson 5), not testing.
- **Healer helps on selectors, harms on logic.** A changed selector → healer
  re-finds it (route through PR review). A changed business behavior → healer
  masks the bug; that failing-test-to-fix case is Lesson 5.

<!-- END @przeprogramowani/10x-cli -->
