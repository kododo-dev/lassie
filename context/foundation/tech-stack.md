---
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
---

## Why this stack

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
