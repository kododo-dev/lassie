---
project: lassie
researched_at: 2026-08-03
recommended_platform: Self-hosted VPS (Docker Compose)
runner_up: Railway
context_type: mvp
tech_stack:
  language: C#
  framework: ASP.NET Core (net10.0, minimal API)
  runtime: .NET 10
---

## Recommendation

**Self-host Lassie on the developer's existing VPS**, using Docker Compose (app + Postgres + Caddy reverse proxy) with GitHub Actions building and pushing an image to GHCR on every merge to `main`, then deploying over SSH.

This confirms — rather than overrides — the `deployment_target: self-host` hint already recorded in `context/foundation/tech-stack.md` from the earlier tech-stack-selection step. The developer already owns the VPS (sunk cost, zero incremental infra spend), which is the single strongest signal for a cost-minimizing, solo, 3-week MVP (interview Q2). The managed-PaaS shortlist (Railway, Render, Fly.io) was fully researched and scored first; Railway is recorded as runner-up in case the self-host operational burden (documented below) proves heavier than expected once implementation starts.

## Platform Comparison

Six managed platforms were researched against the five agent-friendly criteria (`references/agent-friendly-criteria.md`), filtered by the hard constraint that Lassie is ASP.NET Core (.NET 10) — not a JS/edge runtime. Three were eliminated outright: Cloudflare Workers, Vercel, and Netlify have no first-class .NET hosting path (Cloudflare: Wasm is impractical, Containers has no native Postgres; Vercel: container support is young, no static IP for DB access; Netlify: no .NET runtime at all, not even as a workaround). Self-host was added as a seventh option after the developer interview surfaced pre-existing VPS ownership.

| Platform | CLI-first | Managed/Serverless | Agent-readable docs | Stable deploy API | MCP / Integration | Total |
|---|---|---|---|---|---|---|
| Cloudflare | Pass | Fail (Containers immature for always-on .NET) | Pass | Partial | Pass | Eliminated — no native .NET, no managed Postgres (D1 is SQLite) |
| Vercel | Pass | Partial (containers GA June 2026, young) | Pass | Partial | Pass | Eliminated — no native .NET, no static IP for DB, Postgres is 3rd-party marketplace |
| Netlify | Pass | Fail (no general runtime) | Pass | Partial | Pass | Eliminated — .NET unsupported, not even via workaround |
| Fly.io | Pass | Pass | Pass | Pass | Partial (community-tier MCP) | Strong platform, but Managed Postgres floor (~$38/mo) conflicts with cost-minimize priority |
| Railway | Pass | Pass | Pass | Pass | Partial ("work in progress" MCP) | **Runner-up** — cheapest PaaS floor (~$5-20/mo incl. DB), zero-config co-located Postgres |
| Render | Pass | Pass | Pass | Pass | Pass (official GA MCP) | Close third — similar cost (~$14/mo paid), but free-tier Postgres expires after 30 days |
| **Self-host (VPS)** | Partial (assembled from SSH/Docker/gh, no single CLI) | Fail (raw infra — patching/TLS/backups are the developer's job) | Pass (Docker/Caddy/GH Actions are all standard, agent-known tooling) | Partial (deterministic once built, but hand-assembled, no built-in success/rollback signal) | Fail (no platform MCP for bare VPS; GitHub MCP covers only the CI/repo side) | **Recommended** — chosen for zero incremental cost and confirmed deployment_target, despite scoring lower on the agent-friendly criteria than every surviving PaaS candidate |

### Shortlisted Platforms

#### 1. Self-host on existing VPS (Recommended)

Zero incremental infrastructure cost — the VPS is already paid for and owned. Matches the `deployment_target: self-host` hint recorded during tech-stack selection. Scores worse than every PaaS candidate on the agent-friendly criteria (no managed layer, no platform MCP, more assembly required for CLI-first ops) — this is a conscious trade-off, not an oversight, accepted explicitly after a full anti-bias cross-check (below) and confirmed by the developer.

#### 2. Railway

Best-scoring PaaS alternative: fully managed, zero-config co-located Postgres, cheapest realistic floor (~$5-20/month including database), CLI and docs both solid. Held back only by a "work in progress" MCP server and Railpack's lack of native .NET support (Dockerfile required either way — same requirement as self-hosting). Recorded as the fallback if the self-host operational burden turns out to exceed what a solo/after-hours schedule can sustain.

#### 3. Render

Nearly tied with Railway — the only researched platform with an official, GA MCP server, and a comparably low paid-tier cost (~$14/month). Scored third because its free tier has a real trap for a pausable 3-week MVP: the free Postgres instance expires and is deleted after 30 days.

## Anti-Bias Cross-Check: Self-hosted VPS

### Devil's Advocate — Weaknesses

1. No managed TLS as a safety net — if the Caddy container goes down or ACME renewal silently fails, the license-verification API (which must distinguish "service unavailable" from "license invalid") could serve TLS errors indistinguishable from an outage, with nothing watching for it unless monitoring is built by hand.
2. All OS/Docker security patching becomes the solo developer's manual, unscheduled responsibility — a missed patch cycle is a real security exposure for a product whose entire purpose is access control.
3. No platform-level automatic failover — `restart: unless-stopped` doesn't help if the VPS itself has a hardware or network incident; recovery time depends entirely on the developer noticing.
4. Database backups don't happen automatically — Postgres running as a co-located container needs a hand-configured backup job (pg_dump + off-box storage); skipping this under deadline pressure would be catastrophic against the FR-006 audit-history requirement.
5. No official platform MCP exists for a bare VPS — an agent-driven deploy/rollback/log-read workflow has to be hand-built from SSH + Docker CLI, so "the agent operates infrastructure unattended" is structurally weaker here than on any of the three PaaS candidates.

### Pre-Mortem — How This Could Fail

Six months in, the VPS silently ran out of disk space — Postgres WAL files and Docker image layers from months of `docker compose pull` accumulated with no cleanup job, and no disk-usage alerting existed. The license-verification API for Lassie's busiest customer started timing out right as their busiest week began; the guarantee to distinguish "service unavailable" from "license invalid" meant nothing when the whole host was unresponsive. Recovery took hours — no dashboard, no on-call, just SSH and `journalctl` at midnight. Separately, Let's Encrypt renewal inside the Caddy container had failed two months earlier after an untested Docker network change — TLS quietly fell back to a self-signed cert that most client apps didn't validate strictly, silently defeating the point of encrypted license-key transport. The retrospective's conclusion: self-hosting saved money for five months, then cost a weekend, a customer escalation, and a security review that a managed platform would have prevented by design.

### Unknown Unknowns

- Docker's own disk usage (images, volumes, build cache) grows unbounded by default — a scheduled `docker system prune` or disk-usage alert isn't set up by anyone unless the developer adds it.
- ACME/Let's Encrypt renewal failures are silent by default — Caddy retries automatically, but if it can't (DNS change, firewall change, port 80 blocked), nobody knows until a client reports a certificate warning.
- "The provider does snapshots" is not the same as a tested, restorable `pg_dump` stored off-box — for the FR-006 audit-history requirement, losing this data is data loss, not just downtime.
- Zero-downtime deploys aren't free on a single VPS running docker compose — a naive `docker compose up -d` causes a brief outage during container swap; this may need explicit handling if it starts colliding with the 500ms verification-latency guardrail.
- The VPS provider account (login, 2FA, SSH keys) is now a single point of both operational and security failure — losing access (lost 2FA device, provider suspension for a billing issue) takes the whole product down with no platform support team to call.

## Operational Story

> **Update (2026-08-04)**: VPS recon found the target VPS already runs a shared `kododo` Docker Compose stack (one `caddy` reverse proxy on 80/443, one `postgres` instance) serving two other apps (`runway-demo-web`, `configway-demo-web`) via path-based routing (`kododo.dev/<app>`). Lassie joins that existing stack instead of owning its own Caddy/Postgres — see `deploy/README.md` for the concrete steps. This removes the "own TLS container" and "own Postgres container" risk items below (inherited from the shared stack instead) but adds a new one: changes to the shared Caddyfile or Postgres instance can affect the other two apps.

- **Preview deploys**: No ephemeral per-PR preview URLs in v1 — a single VPS runs one environment. `main` auto-deploys to production on merge (matches the `ci_default_flow: auto-deploy-on-merge` hint already recorded in `tech-stack.md`); feature branches are verified locally with `dotnet run` before merging. Lassie is exposed at `kododo.dev/lassie` (path-based routing, matching the two existing apps on this VPS) rather than its own subdomain.
- **Secrets**: The SSH deploy key and GHCR credentials live in GitHub Secrets, scoped to the deploy workflow. Runtime secrets (DB connection string, license-key signing material) live in a `.env` file on the VPS itself, outside the repo, referenced by `docker-compose.yml`. Rotation is a manual SSH step — edit `.env`, `docker compose up -d` to reload.
- **Rollback**: Images are tagged with the git commit SHA and pushed to GHCR — never `latest`. Rollback is `docker compose pull <service>@<previous-sha> && docker compose up -d`, run either manually over SSH or via a second `workflow_dispatch` GitHub Actions job with a tag input. Database migrations do not roll back automatically — a failed migration needs a manual forward-fix or a restore from the nightly backup, same caveat as every PaaS candidate researched.
- **Approval**: Production redeploy on merge to `main` is unattended, matching the recorded auto-deploy-on-merge flow. Anything touching the VPS's root access, firewall rules, DNS, the shared Caddyfile (used by other apps), or the Postgres backup/restore path requires a human at the keyboard — never delegate destructive database operations to CI or an agent without explicit confirmation.
- **Logs**: Runtime logs via `docker compose logs -f lassie` over SSH. CI/deploy pipeline logs via `gh run view --log` (GitHub CLI) or the GitHub MCP server, which is the one piece of this stack with genuine structured agent tooling.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| Disk fills silently (Docker layers + Postgres WAL) | Pre-mortem / Unknown unknowns | M | H | Scheduled `docker system prune -af` + a disk-usage alert (cron + a free uptime-check service) before first production traffic |
| TLS renewal fails silently, falls back to self-signed | Devil's advocate / Pre-mortem | L | H | Certificate-expiry monitoring hitting the public HTTPS endpoint (e.g. free-tier UptimeRobot or a cron `openssl s_client` check) |
| No automatic off-box database backup | Devil's advocate / Unknown unknowns | M | H | Nightly `pg_dump` cron piped to off-box object storage; test one real restore before go-live |
| OS/Docker security patching is entirely manual | Devil's advocate | M | M | Enable unattended-upgrades for OS security patches; monthly manual `docker compose pull` + restart for base image updates |
| No platform failover if the VPS has a hardware/network incident | Devil's advocate | L | H | Accepted for MVP scale (single admin, small user count per PRD); document a rebuild-from-scratch runbook (compose file + `.env` template + backup restore) as the DR plan instead of paying for HA |
| Zero-downtime deploy isn't automatic on a single VPS | Unknown unknowns | M | L | Accept brief downtime during `docker compose up -d` for MVP; revisit if it collides with the 500ms verification-latency guardrail |
| No platform MCP for agent-driven ops on a bare VPS | Devil's advocate | H (by design) | L | Use the GitHub MCP server for the CI/repo side; wrap SSH+Docker VPS operations in a small documented script the agent can run via Bash |
| Single point of failure: VPS provider account access | Unknown unknowns | L | H | Store provider account recovery info and SSH keys in a password manager with backup 2FA codes; document account-recovery steps in the runbook |

## Getting Started

1. Multi-stage `Dockerfile` at the repo root building with `mcr.microsoft.com/dotnet/sdk:10.0` and running on `mcr.microsoft.com/dotnet/aspnet:10.0`; `ASPNETCORE_URLS=http://+:8080` and `EXPOSE 8080` to match `src/lassie.csproj`'s target framework (`net10.0`). (Done.)
2. `deploy/docker-compose.yml` defines only the `lassie` service, joining the VPS's existing external `web` and `internal` Docker networks — no own `postgres` or `caddy` service, since the VPS already runs both for other apps under `/opt/docker/kododo`. (Done — see `deploy/README.md` for the full VPS-side setup steps.)
3. `.github/workflows/deploy.yml`: on push to `main`, build the image, tag it with the commit SHA, push to GHCR, then SSH into the VPS (`DEPLOY_SSH_KEY` GitHub Secret) to run `docker compose pull && docker compose up -d` in `/opt/docker/lassie`. (Done.)
4. Configure a nightly `pg_dump` cron job on the VPS (`deploy/backup.sh`, dumping only the `lassie` database from the shared Postgres container) piping to off-box storage, and manually verify one restore before pointing any real client at the API. (Script done; off-box shipping and the cron entry itself are still manual VPS steps — see `deploy/README.md`.)
5. No DNS step needed — `kododo.dev` already resolves to the VPS and already has a valid certificate; Lassie is reached at `kododo.dev/lassie` via a new `handle_path` block added to the existing shared Caddyfile (`deploy/README.md` step 2).

## Out of Scope

The following were not evaluated or produced in this research:
- Actually writing the Dockerfile, `docker-compose.yml`, or GitHub Actions workflow files (deferred to `/10x-implement`)
- Production-scale architecture (multi-region, HA, dedicated DR site)
- Choosing a specific VPS provider or region (the developer already owns the VPS)
