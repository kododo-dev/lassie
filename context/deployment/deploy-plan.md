---
project: lassie
status: deployed
first_deployed_at: 2026-08-04
platform: Self-hosted VPS (Docker Compose, joining the shared kododo stack)
repo: https://github.com/kododo-dev/lassie
production_url: https://kododo.dev/lassie
---

## Summary

Lassie's admin panel/API scaffold is live at `kododo.dev/lassie`, deployed via GitHub Actions
(build → push to GHCR → SSH deploy) into the shared `kododo` VPS stack alongside `runway-demo-web`
and `configway-demo-web`. This is the audit trail of what was actually done to get there —
platform rationale lives in `context/foundation/infrastructure.md`, step-by-step VPS instructions
in `deploy/README.md`. This doc is the "what happened" record; re-run/rollback mechanics belong in
`deploy/README.md`, not here.

## What was done

### 1. GitHub repository

- Created `kododo-dev/lassie` (public — chosen over private since this is an early scaffold with
  no secrets in code; license API keys and runtime secrets never enter the repo per CLAUDE.md).
- Local `master` renamed to `main` to match the deploy workflow's trigger branch and the org's
  convention.
- `origin` remote added, initial scaffolding + infra commit pushed (`6bec3e9`).

### 2. VPS preparation (`vps-c2b93eb6.vps.ovh.net`, shared with other apps — see `deploy/README.md`)

- Verified the shared Postgres container's actual password (`postgres`) by inspecting the running
  container rather than trusting the value assumed in an earlier session.
- Created database `lassie` in the shared Postgres instance (done in an earlier session).
- Added a `handle_path /lassie* { reverse_proxy lassie:8080 }` block to the shared Caddyfile,
  before the fallback `handle` block; validated and reloaded (zero downtime for the other two
  apps) — done in an earlier session.
- Created `/opt/docker/lassie/`, copied `deploy/docker-compose.yml`, wrote `.env`:
  `GHCR_OWNER=kododo-dev`, `LASSIE_IMAGE_TAG=latest`, `POSTGRES_DB=lassie`,
  `POSTGRES_USER=postgres`, `POSTGRES_PASSWORD=postgres`. Confirmed the VPS's `web`/`internal`
  Docker networks already existed.

### 3. GitHub Actions secrets (`kododo-dev/lassie` → Settings → Secrets and variables → Actions)

`DEPLOY_HOST`, `DEPLOY_PORT=9022`, `DEPLOY_USER=debian`, `DEPLOY_PATH=/opt/docker/lassie`,
`DEPLOY_SSH_KEY` (private half of a dedicated `~/.ssh/lassie_deploy` ed25519 keypair, public half
already on the VPS's `authorized_keys`).

### 4. First deploy and the two bugs it surfaced

Pushed to `main`, which triggered `.github/workflows/deploy.yml`. Two real issues came up, both
fixed and pushed as follow-up commits on `main`:

1. **`cd: too many arguments` on the VPS.** `gh secret set DEPLOY_PATH -b "/opt/docker/lassie"`
   run from Git Bash on Windows — MSYS silently rewrites leading-`/` CLI arguments to a Windows
   path before handing them to the native `gh.exe`, corrupting the secret. Fixed by re-setting the
   secret via stdin (`printf '/opt/docker/lassie' | gh secret set DEPLOY_PATH`) instead of `-b`,
   which bypasses argument-level path conversion. Worth remembering for any future secret/argument
   on this machine that starts with `/`.
2. **`permission denied` connecting to the Docker socket.** The deploy script ran plain
   `docker compose ...`; `debian` has passwordless `sudo` but is not in the `docker` group on this
   VPS. Fixed in commit `6ec4355` — every `docker`/`docker compose` invocation in the deploy script
   now goes through `sudo`.
3. **`unauthorized` pulling `ghcr.io/kododo-dev/lassie`.** GHCR packages default to private
   regardless of the source repo's own visibility. The org has package-visibility changes to
   Public disabled at the org level (confirmed via GitHub UI: "Setting is disabled by organization
   administrators"), so the image could not be made public. Resolved instead with
   `sudo docker login ghcr.io` on the VPS using a **dedicated classic PAT scoped to `read:packages`
   only** (named `lassie-ghcr-vps-pull`), stored in `/root/.docker/config.json` on the VPS —
   matches the CLAUDE.md posture of scoped tokens over master keys. The PAT was pasted once into
   this session, piped directly into the SSH command, and not written to any file or committed.

Also discovered along the way: the original deploy script had no `set -e`, so the `unauthorized`
failure above still reported the GitHub Actions job as green — a silent failure that manual
verification (`curl`, `docker compose ps`) caught but CI did not.

### 5. Health check (commit `d5638cf`)

Added `set -e` to the deploy script and a post-deploy verification loop: after
`docker compose up -d`, poll `https://kododo.dev/lassie/weatherforecast` up to 10 times (3s apart)
and fail the job if it never returns something other than `502`/`000`. Verified working on the
next deploy — it correctly caught a transient `502` during container restart, retried, and passed
once the container came up (HTTP 200).

## Current state

- Container `lassie` running on the VPS, image `ghcr.io/kododo-dev/lassie:<git-sha>`, joined to
  the shared `web`/`internal` networks, reachable at `https://kododo.dev/lassie/weatherforecast`
  (200 OK — still the scaffold's default `WeatherForecast` endpoint; no domain code deployed yet).
- Every push to `main` now: builds → pushes image to GHCR (tagged `latest` and the commit SHA) →
  SSHes into the VPS → updates `.env` → `sudo docker compose pull && up -d` → prunes dangling
  images → verifies the public endpoint before the job goes green.

## Deviations from `infrastructure.md`'s "Getting Started"

All 5 steps listed there are now done (previously marked "manual VPS steps still open"). No
platform or architecture deviation from the recommendation — self-host on the existing VPS, joining
the shared Caddy/Postgres stack, exactly as researched.

## Still open (tracked in `infrastructure.md`'s risk register — unchanged by this deploy)

- Nightly `pg_dump` cron entry (`deploy/backup.sh` exists; the `crontab -e` line and off-box
  shipping are still manual, per `deploy/README.md` step 4).
- Disk-usage / cert-expiry monitoring, `unattended-upgrades` — not yet set up.
- No domain code deployed yet — `src/Program.cs` is still the ASP.NET Core scaffold template.
