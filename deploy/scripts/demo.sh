#!/usr/bin/env bash
# Closed demo on the Mac behind a Cloudflare Tunnel (#1065). Usage: deploy/scripts/demo.sh <command>
#   init            create .env.demo (random secrets) and the clean realm
#   build           build the images
#   up [--tunnel]   start the stack, optionally public through the tunnel
#   down            stop it, data stays
#   status | logs [service]
#   backup          database dump and files to the external disk, keeps the last 14
#   restore <dir> --yes   replace demo data with a backup (takes a backup of the current data first)
#   seed-schools [--dry-run]   public and private schools for all provinces
set -euo pipefail

DEPLOY="$(cd "$(dirname "$0")/.." && pwd)"
BACKUP_ROOT="${DEMO_BACKUP_DIR:-/Volumes/Mustafas HDD/Builds/examapp/demo-backups}"
PRIVATE_SCHOOLS_CSV="${DEMO_PRIVATE_SCHOOLS_CSV:-/Volumes/Mustafas HDD/Builds/examapp/private-schools/ozel-okullar.csv}"

compose() {
  docker compose --env-file "$DEPLOY/.env.demo" -f "$DEPLOY/docker-compose.prod.yml" -f "$DEPLOY/docker-compose.demo.yml" "$@"
}

case "${1:-}" in
  init) python3 "$DEPLOY/scripts/demo-init.py" ;;
  build) compose build ;;
  up)
    if [[ "${2:-}" == "--tunnel" ]]; then compose --profile tunnel up -d; else compose up -d; fi
    ;;
  down) compose --profile tunnel down ;;
  status) compose --profile tunnel ps ;;
  logs) compose --profile tunnel logs --tail=100 "${@:2}" ;;
  backup)
    target="$BACKUP_ROOT/$(date +%Y%m%d-%H%M%S)"
    mkdir -p "$target"
    compose exec -T postgres sh -c 'pg_dumpall -U "$POSTGRES_USER"' | gzip > "$target/postgres.sql.gz"
    # The MinIO image has no tar, so a small helper reads the volume.
    docker run --rm -v hedefokul-demo_minio_data:/data:ro postgres:18.3 tar -C /data -czf - . > "$target/minio.tar.gz"
    ls -1dt "$BACKUP_ROOT"/*/ | tail -n +15 | while read -r old; do rm -rf "$old"; done
    echo "Backup: $target"
    ;;
  restore)
    src="${2:-}"
    if [[ ! -f "$src/postgres.sql.gz" || ! -f "$src/minio.tar.gz" || "${3:-}" != "--yes" ]]; then
      echo "Usage: demo.sh restore <backup dir> --yes  (replaces all demo data)"; exit 1
    fi
    if [[ -n "$(compose ps -q postgres)" ]]; then "$0" backup; fi
    compose --profile tunnel down
    docker volume rm hedefokul-demo_postgres_data hedefokul-demo_minio_data hedefokul-demo_redis_data 2>/dev/null || true
    compose create postgres minio
    docker run --rm -i -v hedefokul-demo_minio_data:/data postgres:18.3 tar -C /data -xzf - < "$src/minio.tar.gz"
    compose start postgres
    until compose exec -T postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d postgres -q'; do sleep 2; done
    # The init script has just created the app databases empty; the dump creates them itself.
    for db in $(gunzip -c "$src/postgres.sql.gz" | sed -n 's/^CREATE DATABASE \([a-z_0-9]*\) .*/\1/p'); do
      compose exec -T postgres sh -c "psql -U \"\$POSTGRES_USER\" -d postgres -qc 'DROP DATABASE IF EXISTS $db'"
    done
    # The only expected error is the superuser role that already exists.
    errors=$(gunzip -c "$src/postgres.sql.gz" | compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d postgres -q' 2>&1 >/dev/null \
      | grep ERROR | grep -v 'role ".*" already exists' || true)
    if [[ -n "$errors" ]]; then echo "$errors"; echo "Restore had errors; the stack is left stopped."; exit 1; fi
    echo "Restored $src. Start with: demo.sh up --tunnel"
    ;;
  seed-schools)
    # The image's entrypoint drops arguments, so without this override a whole API would start.
    seed() {
      compose run --rm --no-deps -T -v "$PRIVATE_SCHOOLS_CSV:/seed/ozel-okullar.csv:ro" --entrypoint sh exam-dotnet-api \
        -c 'exec dotnet "$APP_DLL" "$@"' seed seed-schools --allow-production --all-provinces --no-migrate "$@"
    }
    seed --types ilkokul,ortaokul,lise --include-imam-hatip "${@:2}"
    seed --types ilkokul,ortaokul,lise --source-file /seed/ozel-okullar.csv "${@:2}"
    ;;
  *) sed -n 2,10p "$0"; exit 1 ;;
esac
