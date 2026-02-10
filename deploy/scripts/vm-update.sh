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

# Pull latest images (tag is provided via IMAGE_TAG env)
if [ -n "${SERVICES_TO_DEPLOY:-}" ] && [ "${SERVICES_TO_DEPLOY}" != "all" ]; then
  docker compose --env-file "$ENV_FILE" \
    -f docker-compose.prod.yml \
    -f docker-compose.prod.images.yml \
    pull ${SERVICES_TO_DEPLOY}
else
  docker compose --env-file "$ENV_FILE" \
    -f docker-compose.prod.yml \
    -f docker-compose.prod.images.yml \
    pull
fi

# Restart without building
IMAGE_TAG="${IMAGE_TAG:-latest}" \
  if [ -n "${SERVICES_TO_DEPLOY:-}" ] && [ "${SERVICES_TO_DEPLOY}" != "all" ]; then
    docker compose --env-file "$ENV_FILE" \
      -f docker-compose.prod.yml \
      -f docker-compose.prod.images.yml \
      up -d --no-build ${SERVICES_TO_DEPLOY}
  else
    docker compose --env-file "$ENV_FILE" \
      -f docker-compose.prod.yml \
      -f docker-compose.prod.images.yml \
      up -d --no-build
  fi

# Optional cleanup
if [ "${PRUNE_IMAGES:-0}" = "1" ]; then
  docker image prune -f
fi
