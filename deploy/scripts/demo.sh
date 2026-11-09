#!/usr/bin/env bash
# Closed demo on the Mac behind a Cloudflare Tunnel (#1065). Usage: deploy/scripts/demo.sh <command>
#   init            create .env.demo (random secrets) and the clean realm
#   build           build the images
#   up [--tunnel]   start the stack, optionally public through the tunnel
#   down            stop it, data stays
#   status | logs [service]
#   backup          database dump and files to the external disk, keeps the last 14
set -euo pipefail

DEPLOY="$(cd "$(dirname "$0")/.." && pwd)"
BACKUP_ROOT="${DEMO_BACKUP_DIR:-/Volumes/Mustafas HDD/Builds/examapp/demo-backups}"

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
  *) sed -n 2,8p "$0"; exit 1 ;;
esac
