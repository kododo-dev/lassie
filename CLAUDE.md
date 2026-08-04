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

<!-- BEGIN @przeprogramowani/10x-cli -->

## 10xDevs AI Toolkit - Module 2, Lesson 2

Turn one roadmap item into the first implementation cycle with the **change planning chain**:

```
/10x-roadmap -> /10x-new -> /10x-plan -> /10x-plan-review -> /10x-implement
```

`/10x-new`, `/10x-plan`, `/10x-plan-review`, and `/10x-implement` are the lesson focus. `/10x-frame` and `/10x-research` are not required rituals here; they are escalation paths introduced in the next lesson.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Change setup (lesson focus)** | |
| `/10x-new <change-id>` | You selected a roadmap item and need a stable change folder. Creates `context/changes/<change-id>/change.md` so planning, implementation, progress, commits, and later review all share one identity. Use AFTER roadmap selection, BEFORE `/10x-plan`. |
| **Planning (lesson focus)** | |
| `/10x-plan <change-id>` | You have a change folder and need a reviewable implementation plan. Reads roadmap context, foundation docs, codebase evidence, and any existing change notes; writes `plan.md` and `plan-brief.md` with phases, file contracts, success criteria, and `## Progress`. |
| **Plan readiness (lesson focus)** | |
| `/10x-plan-review <change-id>` | You have `plan.md` and need a light pre-code readiness check. Use it to catch missing end state, weak contracts, malformed progress, scope drift, or blind spots before code changes begin. |
| **Implementation (lesson focus)** | |
| `/10x-implement <change-id> phase <n>` | You have an approved plan and want to execute one phase with verification, manual gate, commit ritual, and SHA write-back to `## Progress`. |
| **Lifecycle closure** | |
| `/10x-archive <change-id>` | A change is merged or intentionally closed. Move it out of active `context/changes/` into archive state. |

### How the chain hands off

- `/10x-new` creates the durable change identity.
- `/10x-plan` turns that identity into an implementation contract.
- `/10x-plan-review` checks the plan before the agent mutates code.
- `/10x-implement` executes one planned phase, verifies, asks for manual confirmation when needed, commits, and records progress.

### Lesson boundaries

- Plan is the default router after roadmap selection. Start with `/10x-plan` unless the problem is unclear or external evidence is blocking.
- Do not run `/10x-frame + /10x-research` as ceremony for every change.
- Do not turn this lesson into a full end-to-end product build. A checkpoint with a planned and partially or fully implemented stream is valid.
- Code review of the implemented diff belongs to Lesson 3 via `/10x-impl-review`.
- Lifecycle closure via `/10x-archive` after a change is merged or intentionally closed.

### Paths used by this lesson

- `context/foundation/roadmap.md` - upstream roadmap
- `context/changes/<change-id>/change.md` - change identity
- `context/changes/<change-id>/plan.md` - implementation contract
- `context/changes/<change-id>/plan-brief.md` - compressed handoff
- `context/foundation/lessons.md` - recurring rules and pitfalls
- `docs/reference/contract-surfaces.md` - load-bearing names registry

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
