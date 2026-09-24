#!/bin/sh
# Issue #279 item 1 (prod) — creates/updates the 5 per-service RabbitMQ users
# and their least-privilege permissions via the management HTTP API. Run as
# the `rabbitmq-init` one-shot container in deploy/docker-compose.prod.yml
# (and the equivalent Kubernetes Job in deploy/gcp/k8s/), after the RabbitMQ
# node is healthy and before any application service starts.
#
# Idempotent: PUT /api/users/{user} and PUT /api/permissions/{vhost}/{user}
# both upsert, so re-running this (every deploy) is safe and converges to the
# same state — including picking up a rotated *_PASSWORD secret.
#
# IMPORTANT: the permission regexes below are the prod mirror of
# rabbitmq/definitions.json's dev `permissions` block (issue #279's matrix —
# see .claude/rules/local-dev.md). There is no shared/generated source
# between the two files; if you add/remove an outbox event or a consumer's
# subscribed event list, update BOTH by hand.
set -eu

: "${RABBITMQ_USER:?RABBITMQ_USER not set}"
: "${RABBITMQ_PASS:?RABBITMQ_PASS not set}"
: "${RABBITMQ_EXAM_OUTBOX_PASSWORD:?RABBITMQ_EXAM_OUTBOX_PASSWORD not set}"
: "${RABBITMQ_IDENTITY_OUTBOX_PASSWORD:?RABBITMQ_IDENTITY_OUTBOX_PASSWORD not set}"
: "${RABBITMQ_BADGE_OUTBOX_PASSWORD:?RABBITMQ_BADGE_OUTBOX_PASSWORD not set}"
: "${RABBITMQ_BADGE_SERVICE_PASSWORD:?RABBITMQ_BADGE_SERVICE_PASSWORD not set}"
: "${RABBITMQ_EXAM_API_PASSWORD:?RABBITMQ_EXAM_API_PASSWORD not set}"
: "${RABBITMQ_AUTH_API_PASSWORD:?RABBITMQ_AUTH_API_PASSWORD not set}"

RABBIT_HOST="${RABBITMQ_HOST:-rabbitmq}"
RABBIT_URL="http://${RABBIT_HOST}:15672"
ADMIN_AUTH="${RABBITMQ_USER}:${RABBITMQ_PASS}"
VHOST="%2F" # "/" URL-encoded

# Wait for the management API (not just the AMQP port) to answer, since we
# only talk to :15672 below. docker-compose.prod.yml's rabbitmq-init already
# waits on `rabbitmq: condition: service_healthy`, but Kubernetes has no
# equivalent Job-level dependency wait, so this loop is the only thing that
# makes the Job safe to start immediately alongside the StatefulSet.
echo "rabbitmq-init: waiting for ${RABBIT_URL}/api/overview ..."
i=0
until curl -sf -u "${ADMIN_AUTH}" "${RABBIT_URL}/api/overview" >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then
    echo "rabbitmq-init: gave up waiting for RabbitMQ management API after 60 attempts" >&2
    exit 1
  fi
  sleep 2
done
echo "rabbitmq-init: RabbitMQ management API is up"

NS='ExamApp\.Foundation\.Contracts'

# Publisher event lists (must match OutboxEventRegistry entries actually
# written by each DB's outbox — see the _comment fields in
# rabbitmq/definitions.json for the grep-verified source of truth).
EXAM_OUTBOX_EVENTS="AnswerSubmittedEvent|QuestionCreatedEvent|WorksheetReminderDueEvent|WorksheetAccessRequestedEvent|WorksheetAccessRequestApprovedEvent|WorksheetAccessRequestRejectedEvent|TeacherApplicationSubmittedEvent|TeacherApplicationDecidedEvent|TeacherSchoolRequestSubmittedEvent|IndependentTeacherRegisteredEvent|BookingRequestCreatedEvent|BookingDecisionEvent|UserPreferredLocaleChangedEvent|UserRoleChangedEvent"
IDENTITY_OUTBOX_EVENTS="LoginAttemptedEvent|UserPreferredLocaleChangedEvent"
BADGE_OUTBOX_EVENTS="StudentPointsChangedEvent"
# badge_service consumes everything except StudentPointsChangedEvent and UserRoleChangedEvent
# (the latter is auth-api's own, issue #277 item 4 — its data lives in auth-api's DB).
BADGE_SERVICE_EVENTS="AnswerSubmittedEvent|QuestionCreatedEvent|WorksheetReminderDueEvent|WorksheetAccessRequestedEvent|WorksheetAccessRequestApprovedEvent|WorksheetAccessRequestRejectedEvent|LoginAttemptedEvent|TeacherApplicationSubmittedEvent|TeacherApplicationDecidedEvent|TeacherSchoolRequestSubmittedEvent|IndependentTeacherRegisteredEvent|BookingRequestCreatedEvent|BookingDecisionEvent|UserPreferredLocaleChangedEvent"
EXAM_API_EVENTS="StudentPointsChangedEvent"
AUTH_API_EVENTS="UserRoleChangedEvent"

create_user() {
  user="$1"
  pass="$2"
  echo "rabbitmq-init: upserting user '${user}'"
  curl -sf -u "${ADMIN_AUTH}" -X PUT "${RABBIT_URL}/api/users/${user}" \
    -H 'content-type: application/json' \
    -d "{\"password\":\"${pass}\",\"tags\":\"\"}"
}

# Consumers must NEVER get "write" on message-type exchanges — that would let
# them publish/forge the events they're supposed to only consume (the exact
# risk issue #279 closes). Publishers get write==configure on their message
# exchanges and no queue (read is empty). Consumers get write scoped to only
# their own queue/exchange/_error/_skipped; configure+read additionally cover
# the message exchanges they bind from (RabbitMQ's exchange.bind/queue.bind
# need read-on-source + write-on-destination — see rabbitmq/definitions.json
# comments for the full reasoning). PublishFaults is off for every consumer
# (issue #279, C# side) so no MassTransit.Fault:* exchange access is granted.
set_perms() {
  user="$1"
  configure="$2"
  write="$3"
  read="$4"
  echo "rabbitmq-init: setting permissions for '${user}'"
  curl -sf -u "${ADMIN_AUTH}" -X PUT "${RABBIT_URL}/api/permissions/${VHOST}/${user}" \
    -H 'content-type: application/json' \
    -d "{\"configure\":\"${configure}\",\"write\":\"${write}\",\"read\":\"${read}\"}"
}

# --- Publishers: configure+write on their message exchanges only, no queue ---

create_user exam_outbox_pub "${RABBITMQ_EXAM_OUTBOX_PASSWORD}"
set_perms exam_outbox_pub \
  "^${NS}:(${EXAM_OUTBOX_EVENTS})\$" \
  "^${NS}:(${EXAM_OUTBOX_EVENTS})\$" \
  '^$'

create_user identity_outbox_pub "${RABBITMQ_IDENTITY_OUTBOX_PASSWORD}"
set_perms identity_outbox_pub \
  "^${NS}:(${IDENTITY_OUTBOX_EVENTS})\$" \
  "^${NS}:(${IDENTITY_OUTBOX_EVENTS})\$" \
  '^$'

create_user badge_outbox_pub "${RABBITMQ_BADGE_OUTBOX_PASSWORD}"
set_perms badge_outbox_pub \
  "^${NS}:(${BADGE_OUTBOX_EVENTS})\$" \
  "^${NS}:(${BADGE_OUTBOX_EVENTS})\$" \
  '^$'

# --- Consumers: write restricted to their own queue/exchange, never a message exchange ---

create_user badge_service "${RABBITMQ_BADGE_SERVICE_PASSWORD}"
set_perms badge_service \
  "^(badge-service(_error|_skipped)?|${NS}:(${BADGE_SERVICE_EVENTS}))\$" \
  '^badge-service(_error|_skipped)?$' \
  "^(badge-service(_error|_skipped)?|${NS}:(${BADGE_SERVICE_EVENTS}))\$"

create_user exam_api "${RABBITMQ_EXAM_API_PASSWORD}"
set_perms exam_api \
  "^(exam-api(_error|_skipped)?|${NS}:(${EXAM_API_EVENTS}))\$" \
  '^exam-api(_error|_skipped)?$' \
  "^(exam-api(_error|_skipped)?|${NS}:(${EXAM_API_EVENTS}))\$"

create_user auth_api "${RABBITMQ_AUTH_API_PASSWORD}"
set_perms auth_api \
  "^(auth-api(_error|_skipped)?|${NS}:(${AUTH_API_EVENTS}))\$" \
  '^auth-api(_error|_skipped)?$' \
  "^(auth-api(_error|_skipped)?|${NS}:(${AUTH_API_EVENTS}))\$"

echo 'rabbitmq-init: done'
