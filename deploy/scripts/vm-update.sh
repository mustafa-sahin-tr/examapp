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

# IMPORTANT: keep application deployments isolated from the minimal/infra stack.
# The minimal stack is managed via the dedicated workflow mode (bootstrap_minimal)
# and should not be implicitly restarted/pulled during normal app deploys.
APP_SERVICES="question-detector exam-dotnet-api auth-api exam-badge-api exam-outbox-publisher angular-app auth-ui ocelot-gateway"

# Allow caller to override services via CLI args (e.g. vm-update.sh auth-api)
if [ "$#" -gt 0 ]; then
  # shellcheck disable=SC2124
  SERVICES_TO_DEPLOY_NORM="$(trim_ws "$*")"
fi

echo "SERVICES_TO_DEPLOY=${SERVICES_TO_DEPLOY_NORM:-<all>}"

if [ -z "$SERVICES_TO_DEPLOY_NORM" ] || [ "$SERVICES_TO_DEPLOY_NORM" = "all" ]; then
  SERVICES_TO_DEPLOY_NORM="$APP_SERVICES"
  echo "Resolved services for deploy (app-only): $SERVICES_TO_DEPLOY_NORM"
fi

# Pull latest images (tag is provided via IMAGE_TAG env)
if [ -n "$SERVICES_TO_DEPLOY_NORM" ]; then
  # split on spaces into positional args
  # shellcheck disable=SC2086
  set -- $SERVICES_TO_DEPLOY_NORM
  compose pull "$@"
else
  compose pull
fi

# Restart without building
if [ -n "$SERVICES_TO_DEPLOY_NORM" ]; then
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
