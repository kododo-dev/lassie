# Deploying Lassie

Self-hosted under `kododo.dev/lassie`, on a VPS that already runs a shared `caddy` (reverse
proxy, ports 80/443) and a shared `postgres` container for other apps (`runway-demo-web`,
`configway-demo-web`) under the `kododo` Docker Compose stack at `/opt/docker/kododo`. Lassie
does **not** get its own Caddy or Postgres container — it joins the existing `web` and
`internal` Docker networks and gets its own database inside the existing Postgres instance,
matching how the other two apps are already set up. Decision and rationale:
`context/foundation/infrastructure.md`.

The steps below are one-time manual setup — they need VPS/GitHub access this session doesn't
have, so run them yourself (or walk through them with the agent step by step, confirming each
one, since this VPS also runs other people's apps).

## 1. One-time VPS setup

Create the database inside the existing shared Postgres container:

```bash
docker exec -it postgres psql -U postgres -c "CREATE DATABASE lassie;"
```

Set up the deploy directory (separate from `/opt/docker/kododo`, which belongs to the other apps):

```bash
mkdir -p /opt/docker/lassie && cd /opt/docker/lassie
# copy deploy/docker-compose.yml and deploy/.env.example here (scp, or git sparse-checkout)
cp .env.example .env
```

Edit `.env`:
- `GHCR_OWNER` — your GitHub username/org (the image is `ghcr.io/<owner>/lassie`)
- `POSTGRES_PASSWORD` — must match the shared Postgres container's actual password

Bring it up for the first time:

```bash
cd /opt/docker/lassie
docker compose pull
docker compose up -d
```

The `lassie` container joins the existing external `web` and `internal` networks — no ports
are published; Caddy reaches it by container name on `web`, same as the other two apps.

## 2. Route `kododo.dev/lassie` through the existing Caddy

Edit `/opt/docker/kododo/Caddyfile` (owned by the `deploy` user — this is the **shared** proxy
config for every app on this VPS, be careful) and add a `handle_path` block **before** the
fallback `handle` block in the `kododo.dev` site:

```caddyfile
kododo.dev {
    handle_path /configway/demo* {
        reverse_proxy configway-demo-web:8080
    }

    handle_path /runway/demo* {
        reverse_proxy runway-demo-web:8080
    }

    handle_path /lassie* {
        reverse_proxy lassie:8080
    }

    handle {
        root * /var/www/kododo
        file_server
    }
}
```

Reload (not restart) so the other apps see zero downtime:

```bash
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

No DNS step needed — `kododo.dev` already resolves to this VPS and already has a valid
certificate covering it.

## 3. GitHub repository secrets

`Settings → Secrets and variables → Actions` on the GitHub repo:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | VPS hostname |
| `DEPLOY_PORT` | VPS SSH port (non-default — check with whoever manages the VPS) |
| `DEPLOY_USER` | the SSH user with access to `/opt/docker/lassie` and the `docker` group/sudo |
| `DEPLOY_SSH_KEY` | private key for a dedicated deploy keypair (`ssh-keygen -t ed25519 -f deploy_key -N ""`) — add the matching public key to the VPS user's `~/.ssh/authorized_keys` |
| `DEPLOY_PATH` | `/opt/docker/lassie` |

`GITHUB_TOKEN` (for pushing to GHCR) is provided automatically — no secret to add.

Once these are set, every push to `main` builds the image, pushes it to `ghcr.io/<owner>/lassie`,
and redeploys via SSH (`.github/workflows/deploy.yml`). This does **not** touch the Caddy route
from step 2 — that only needs to be done once (or again if the path prefix ever changes).

## 4. Nightly backups

The `lassie` database lives in the VPS's shared Postgres instance — back up just that database,
not the whole instance (the other apps' backups, if any, are not this project's concern):

```bash
chmod +x /opt/docker/lassie/backup.sh
crontab -e
# add:
0 3 * * * /opt/docker/lassie/backup.sh >> /var/log/lassie-backup.log 2>&1
```

`backup.sh` dumps the `lassie` database to `deploy/backups/`, keeps 14 days locally, and has a
`TODO` for shipping dumps off-box (e.g. via `rclone`) — wire that up before treating backups as
a real safety net, and test one restore. See the risk register in
`context/foundation/infrastructure.md` for why this matters.

## Rollback

```bash
ssh -p <port> <user>@<host>
cd /opt/docker/lassie
sed -i 's/^LASSIE_IMAGE_TAG=.*/LASSIE_IMAGE_TAG=<previous-git-sha>/' .env
docker compose pull lassie
docker compose up -d
```

Database migrations do not roll back automatically — restore from a `deploy/backups/` dump if a
bad deploy also changed the schema.

## Logs

```bash
docker compose logs -f lassie          # app (from /opt/docker/lassie)
docker logs -f caddy                   # reverse proxy / TLS (shared, from anywhere)
docker exec -it postgres psql -U postgres -d lassie   # database (shared, from anywhere)
```

## Not done here

- Disk-usage / cert-expiry monitoring and OS security patching (`unattended-upgrades`) — set
  these up per the risk register in `context/foundation/infrastructure.md` before pointing real
  traffic at this. Some of this may already be handled for the shared VPS — check before
  duplicating it.
- The app itself has no database code yet (`src/Program.cs` is still the scaffold template) —
  `ConnectionStrings__DefaultConnection` is wired through but unused until domain code lands.
- The app needs to respect `ASPNETCORE_PATHBASE=/lassie` for correct URL generation behind the
  path-based proxy — same convention already used by `runway-demo-web` and `configway-demo-web`.
