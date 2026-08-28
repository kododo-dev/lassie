# AI Artifact Distribution — decision for Lassie

10xDevs M5L4 ("Shared AI Registry") — Task 1: apply the decision table to this
project, answer the first row (who is the recipient?), pick a model, justify it.

## Row 1 — who is the recipient of Lassie's AI artifacts?

**A single developer, a single repo, everything on GitHub (`kododo-dev/lassie`).**

There is no second consumer repo and no teammate. The problem this lesson solves —
three copies of a skill drifting across repos until nobody knows which is
authoritative — does not exist here. Requirement 5 (multi-tool delivery) is also
moot: the only consuming tool is Claude Code.

AI artifacts currently in play, all living in-repo:

| Artifact | Location | Role |
|---|---|---|
| Rules | `CLAUDE.md` | project instructions for the agent |
| Skills | `.claude/` (pulled via `10x-cli`) | course toolkit, consumed as an end user |
| Custom agent | `agents/code-review/` | scripted Claude Agent SDK reviewer (M5L2) |
| Hook | `.githooks/pre-commit.js` | format + fast-test gate |

## Chosen model — **none yet**

Artifacts are versioned in the same git tree as the code that uses them. That
already satisfies:

- **Single source of truth** — one repo, one copy of each artifact.
- **Versioning** — git history; a skill change and the code change that needs it
  land in the same commit.
- **Install / update / uninstall** — the artifacts *are* files in the working
  tree; there is nothing to install or remove.

**Authentication** and **multi-tool delivery** have no work to do with one
consumer and one tool. A registry would add moving parts (a publish pipeline, a
read token to place in CI, a version-bump policy) to solve problems Lassie does
not have.

## When this changes → **Model 1: GitHub Packages**

Adopt a distribution mechanism only when one of these becomes true:

1. A second repo needs the same skills/rules.
2. A collaborator joins and needs the toolkit on their machine.
3. A skill has to reach a non-Claude tool (Cursor, Codex).

Model 1 is the fit because the code is already on GitHub, CI already runs on
GitHub Actions, and standing it up is one `publishConfig` field plus a committed
`.npmrc` on the consumer side. The one real cost — placing a long-lived read
token wherever the package installs — stays small while consumers are repos
inside the same GitHub org (they can read with the ephemeral `GITHUB_TOKEN`).

## Why not a heavier model

- **Model 2 (AWS CodeArtifact + Terraform)** — Lassie has no AWS footprint. Deploy
  is a plain OVH VPS pulling from GHCR (see `infrastructure.md`), not AWS. Standing
  up CodeArtifact here would be textbook "distribution for the CV": ~70+ lines of
  Terraform and an AWS account to solve a problem one `publishConfig` field
  already solves.
- **Model 3 (API + CLI)** — buys stack-independence and time-gated content
  delivery. Lassie is one .NET repo with one consumer and no audience to drip
  content to. (Lassie is already a *consumer* of a Model 3 product — `10x-cli` —
  for course material; that is the course relationship, not a team need this repo
  has to serve.)

## Verdict

Keep artifacts in-repo. Revisit with Model 1 the day a second consumer appears.
Recorded here so the choice is deliberate, not drift.
