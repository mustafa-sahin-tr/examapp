#!/usr/bin/env sh
set -eu

# Expected layout on VM:
#   /opt/examapp/deploy
# with:
#   .env.prod
#   docker-compose.prod.yml
#   docker-compose.prod.images.yml

cd "${DEPLOY_DIR:-/opt/examapp/deploy}"

ENV_FILE="${ENV_FILE:-.env.prod}"

if [ -n "${ACR_LOGIN_SERVER:-}" ] && [ -n "${ACR_USERNAME:-}" ] && [ -n "${ACR_PASSWORD:-}" ]; then
  echo "$ACR_PASSWORD" | docker login "$ACR_LOGIN_SERVER" -u "$ACR_USERNAME" --password-stdin
fi

# Ensure tag exists in env for compose substitution
export IMAGE_TAG="${IMAGE_TAG:-latest}"

compose() {
  docker compose --env-file "$ENV_FILE" \
    -f docker-compose.prod.yml \
    -f docker-compose.prod.images.yml \
    "$@"
}

trim_ws() {
  # trims leading/trailing whitespace and squeezes internal whitespace
  # shellcheck disable=SC2001
  printf '%s' "$1" | tr '\r\n\t' ' ' | sed -E 's/^ +//; s/ +$//; s/ +/ /g'
}

SERVICES_TO_DEPLOY_NORM="$(trim_ws "${SERVICES_TO_DEPLOY:-}")"

# Allow caller to override services via CLI args (e.g. vm-update.sh auth-api)
if [ "$#" -gt 0 ]; then
  # shellcheck disable=SC2124
  SERVICES_TO_DEPLOY_NORM="$(trim_ws "$*")"
fi

echo "SERVICES_TO_DEPLOY=${SERVICES_TO_DEPLOY_NORM:-<all>}"

# Pull latest images (tag is provided via IMAGE_TAG env)
if [ -n "$SERVICES_TO_DEPLOY_NORM" ] && [ "$SERVICES_TO_DEPLOY_NORM" != "all" ]; then
  # split on spaces into positional args
  # shellcheck disable=SC2086
  set -- $SERVICES_TO_DEPLOY_NORM
  compose pull "$@"
else
  compose pull
fi

# Restart without building
if [ -n "$SERVICES_TO_DEPLOY_NORM" ] && [ "$SERVICES_TO_DEPLOY_NORM" != "all" ]; then
  # shellcheck disable=SC2086
  set -- $SERVICES_TO_DEPLOY_NORM
  # Don't implicitly start other app services; dependencies (postgres/keycloak/etc.) are assumed running.
  compose up -d --no-build --no-deps "$@"
else
  compose up -d --no-build
fi

# Optional cleanup
if [ "${PRUNE_IMAGES:-0}" = "1" ]; then
  docker image prune -f
fi
