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

## 10xDevs AI Toolkit - Module 2, Lesson 4

Prepare for a harder implementation stream with the **research-backed planning chain**:

```
internal research (/10x-research) + external research (exa.ai, Context7) -> /10x-plan -> /10x-implement -> success
```

The lesson focus is distinguishing internal from external research and using evidence to back planning decisions.

### Task Router - Where to start

| Skill | Use it when |
| --- | --- |
| **Internal research (lesson focus)** | |
| `/10x-research <change-id>` | You need evidence from the existing codebase — patterns, conventions, integration points, or existing implementations. Runs parallel sub-agents over the repo and writes structured findings to `research.md`. |
| **External research (lesson focus)** | |
| exa.ai | You need AI-native web search for library comparisons, best practices, or ecosystem context that the codebase cannot answer. |
| Context7 (`resolve-library-id` → `get-library-docs`) | You need live, current documentation for a specific library or framework. Resolves a library ID first, then fetches relevant doc pages. |
| **Framing spare wheel** | |
| `/10x-frame <change-id>` | The plan won't converge, the plan doesn't deliver expected results, or persistent drift keeps breaking the implementation. Use as an escape hatch on a separate problem (demonstrated on Space Explorers example), not as pre-research ritual. |
| **Planning and execution** | |
| `/10x-plan <change-id>` / `/10x-implement <change-id> phase <n>` | Use the same planning and execution chain from Lesson 2, now with upstream research evidence feeding the plan. |

### Research discipline

- Internal research (`/10x-research`) answers "what does our codebase already do?" — patterns, schemas, conventions, integration points.
- External research (exa.ai, Context7) answers "what should we do?" — library capabilities, API docs, ecosystem best practices.
- Combine both as evidence-backed input to `/10x-plan`. A plan without research evidence on a non-trivial stream is a guess.
- Agent-friendly docs (`llms.txt`, markdown-for-agents, `/md` endpoints) are a quality signal for library selection — libraries that publish agent-readable docs integrate faster.

### `/10x-frame` as spare wheel

Three triggers for reaching for `/10x-frame`:
1. The plan won't converge — research keeps opening more questions instead of narrowing to a contract.
2. The plan doesn't deliver — implementation repeatedly fails to meet success criteria.
3. Persistent drift — the implementation keeps diverging from the plan in ways that suggest the problem was mis-framed.

Demonstrated on a Space Explorers example, not the SRS path. It is an escape hatch, not a mandatory step.

### Paths used by this lesson

- `context/changes/<change-id>/research.md` - internal research output
- `context/changes/<change-id>/frame.md` - framing output when needed
- `context/changes/<change-id>/plan.md` - evidence-backed implementation contract
- `context/foundation/lessons.md` - recurring rules and pitfalls

Skills must not write to `context/archive/`. Archived changes are immutable; if a resolved target path starts with `context/archive/`, abort with: "This change is archived. Open a new change with `/10x-new` instead."

<!-- END @przeprogramowani/10x-cli -->
