#!/bin/sh
# Issue #328 - re-applies ONLY the `permissions` block of definitions.json to a
# running RabbitMQ, so permissions for a newly added event exchange take effect
# without recreating the node.
#
# Why: management.load_definitions upserts permissions on every node boot, but a
# RUNNING rabbitmq container does not restart when definitions.json changes
# (docker-compose `up -d` does not recreate it for a bind-mount content change),
# so the node keeps the old permissions -> ACCESS_REFUSED on the new exchange.
# POST /api/definitions with a payload containing ONLY "permissions" upserts each
# user's permission (== set_permissions); users, passwords, queues and exchanges
# are not in the payload and stay untouched. Idempotent. definitions.json stays
# the single source of truth. Removed users/permissions are NOT reverted.
#
# Used by docker-compose.yml (rabbitmq-permission-sync, reruns on every `up -d`).
# Aspire does not need it (its RabbitMQ is fresh each AppHost start). Local dev
# only; prod uses deploy/scripts/rabbitmq-init.sh.
#
# Env: RABBITMQ_HOST (default rabbitmq), RABBITMQ_USER, RABBITMQ_PASS (admin
# credentials; passed to curl via a 0600 config file, never argv/stdout),
# DEFINITIONS_FILE (default /definitions.json).
set -eu

: "${RABBITMQ_USER:?RABBITMQ_USER not set}"
: "${RABBITMQ_PASS:?RABBITMQ_PASS not set}"
HOST="${RABBITMQ_HOST:-rabbitmq}"
FILE="${DEFINITIONS_FILE:-/definitions.json}"
URL="http://${HOST}:15672"

if ! command -v jq >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1; then
  apk add --no-cache curl jq >/dev/null
fi

umask 077
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT INT TERM
printf 'user = "%s:%s"\n' "$RABBITMQ_USER" "$RABBITMQ_PASS" > "$WORK/curlrc"
CURL_OPTS="-K $WORK/curlrc --connect-timeout 3 --max-time 10"

# The admin (the user we authenticate as) is skipped on purpose: it is already
# .* and we must never lock ourselves out.
PAYLOAD="$(jq -c --arg admin "$RABBITMQ_USER" '{permissions: [.permissions[] | select(.user != $admin) | del(._comment)]}' "$FILE")"
COUNT="$(printf '%s' "$PAYLOAD" | jq '.permissions | length')"

echo "rabbitmq-permission-sync: waiting for ${URL} ..."
i=0
# shellcheck disable=SC2086
until curl -sf $CURL_OPTS "${URL}/api/overview" >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then
    echo "rabbitmq-permission-sync: management API not reachable/authorized after 60 attempts (admin password must match the existing '${RABBITMQ_USER}' in the volume)" >&2
    exit 1
  fi
  sleep 2
done

# Retry: right after boot the user entries may not exist yet (4xx) .
i=0
while :; do
  # shellcheck disable=SC2086
  CODE="$(printf '%s' "$PAYLOAD" | curl -s -o "$WORK/resp" -w '%{http_code}' $CURL_OPTS \
    -X POST -H 'content-type: application/json' --data-binary @- "${URL}/api/definitions" || true)"
  CODE="${CODE:-000}"
  case "$CODE" in 2*) break ;; esac
  i=$((i + 1))
  if [ "$i" -ge 15 ]; then
    echo "rabbitmq-permission-sync: POST /api/definitions failed (HTTP ${CODE})" >&2
    [ -s "$WORK/resp" ] && cat "$WORK/resp" >&2 && echo >&2
    exit 1
  fi
  sleep 2
done
echo "rabbitmq-permission-sync: applied ${COUNT} permission entries (HTTP ${CODE})"
