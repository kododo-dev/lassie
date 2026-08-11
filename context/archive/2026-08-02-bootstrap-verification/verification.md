---
bootstrapped_at: 2026-08-02T21:48:42Z
starter_id: dotnet
starter_name: .NET (ASP.NET Core webapi)
project_name: lassie
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: verified
phase_3_status: ok
audit_command: dotnet list package --vulnerable --include-transitive
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: lassie
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: self-host
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: verified
  path_taken: standard
  quality_override: false
  self_check_answers: null
  has_auth: true
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
```

### Why this stack

Lassie is a solo-built license-management system with two access surfaces: an
admin panel (email+password login, FR-011) and a machine-to-machine
verification API keyed per license (FR-009/FR-010). ASP.NET Core webapi is the
recommended default for `(web, dotnet)` and clears the agent-friendly gates —
strong typing, DI, OpenAPI, and Entity Framework give explicit contracts an
agent can reason from without running the program, which matters given the
PRD's audit-history and key-secrecy requirements (FR-006, key never revealed
in plaintext). Bootstrapper confidence is verified, so scaffolding should be
smooth. Deployment targets self-host per the user's pick (an alternative to
the card's Azure App Service default); CI runs on GitHub Actions with
auto-deploy-on-merge, matching the solo/short-timeline (3-week, after-hours)
profile. Payments, realtime, AI, and background jobs are all out of scope per
the PRD's Non-Goals.

## Pre-scaffold verification

| Signal             | Value                              | Severity | Notes                              |
| ------------------ | ----------------------------------- | -------- | ----------------------------------- |
| npm package         | not run                             | n/a      | non-JS starter (language_family: dotnet) |
| GitHub repo         | not run                             | n/a      | card's `docs_url` (learn.microsoft.com/aspnet/core) is not a GitHub URL |

## Scaffold log

**Resolved invocation**: `dotnet new webapi -n .bootstrap-scaffold --no-restore`
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 6 (`.bootstrap-scaffold.csproj`, `.bootstrap-scaffold.http`, `Program.cs`, `Properties/launchSettings.json`, `appsettings.Development.json`, `appsettings.json`)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold (dotnet's `webapi` template does not ship a `.gitignore`; none existed in cwd either)
**.bootstrap-scaffold cleanup**: deleted

**Note**: the `{name}` substitution for this strategy uses the internal temp-directory name (`.bootstrap-scaffold`), not `project_name` — this is fixed bootstrapper mechanic, not specific to this run. Because `dotnet new webapi -n <name>` names the generated `.csproj`/`.http` files after `<name>`, the moved files are literally `.bootstrap-scaffold.csproj` and `.bootstrap-scaffold.http` rather than `lassie.csproj` / `lassie.http`. Manual rename recommended (see Next steps).

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 1 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: 0/1/0/0 direct of total 0/1/0/0 — the 1 HIGH finding is transitive

#### HIGH findings

- **Package**: `Microsoft.OpenApi` 2.0.0 (transitive)
  **Advisory**: https://github.com/advisories/GHSA-v5pm-xwqc-g5wc
  **Description**: Known high-severity vulnerability flagged by NuGet's advisory feed (NU1903) during restore.
  **Fix version**: not captured in tool output — check the advisory URL for the patched version.
  **Resolved**: pinned `Microsoft.OpenApi` to `2.11.0` via a direct `PackageReference` (overrides the transitive 2.0.0 pulled in by `Microsoft.AspNetCore.OpenApi` 10.0.9), staying within the same major line for compatibility. Re-audit (`dotnet list package --vulnerable --include-transitive`) confirms 0 findings.

## Hints recorded but not acted on

| Hint                       | Value                              |
| -------------------------- | ----------------------------------- |
| bootstrapper_confidence    | verified                            |
| quality_override           | false                                |
| path_taken                 | standard                             |
| self_check_answers         | null                                 |
| team_size                  | solo                                 |
| deployment_target          | self-host                            |
| ci_provider                | github-actions                       |
| ci_default_flow            | auto-deploy-on-merge                 |
| has_auth                   | true                                 |
| has_payments                | false                                |
| has_realtime                | false                                |
| has_ai                      | false                                |
| has_background_jobs         | false                                |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- `git init` (if you have not already) to start your own repo history — this cwd already has a `.git/`, so no action needed here.
- Rename `.bootstrap-scaffold.csproj` and `.bootstrap-scaffold.http` to `lassie.csproj` / `lassie.http` (the scaffold mechanic named them after the internal temp directory, not the project name).
- Address the 1 HIGH transitive finding (`Microsoft.OpenApi` 2.0.0, GHSA-v5pm-xwqc-g5wc) per your project's risk tolerance.
- Review any `.scaffold` siblings the conflict policy created and decide which version of each file to keep (none were created this run).
