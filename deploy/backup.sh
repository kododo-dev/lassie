#!/usr/bin/env bash
# Nightly backup of the `lassie` database. Run from the deploy/ directory via cron (see README.md).
# Postgres is the VPS's shared `postgres` container (the `kododo` stack), not part of this compose project.
set -euo pipefail

cd "$(dirname "$0")"
set -a; source .env; set +a

BACKUP_DIR="./backups"
STAMP="$(date +%Y%m%d-%H%M%S)"
DUMP_FILE="${BACKUP_DIR}/lassie-${STAMP}.sql.gz"

mkdir -p "${BACKUP_DIR}"

docker exec -i postgres pg_dump -U "${POSTGRES_USER}" "${POSTGRES_DB}" | gzip > "${DUMP_FILE}"

# TODO: ship ${DUMP_FILE} off-box before local disk fills — e.g. with rclone:
#   rclone copy "${DUMP_FILE}" remote:lassie-backups/

# Keep 14 days of local dumps regardless of off-box upload status.
find "${BACKUP_DIR}" -name 'lassie-*.sql.gz' -mtime +14 -delete

echo "Backup written to ${DUMP_FILE}"
