---
description: Local development — port reference and how to start each service
alwaysApply: true
---

## Start everything (recommended)

```bash
cp .env.example .env   # one-time: infra credentials for docker-compose
docker-compose up -d
```

`.env` (git-ignored) holds the local Postgres/MinIO/RabbitMQ/Keycloak/Redis
credentials that `docker-compose.yml` reads via `${VAR}`. `.env.example` has the
dev defaults; never reuse them outside local dev.

**Issue #238**: `appsettings.json`/`appsettings.Development.json` no longer carry
real Keycloak `ClientSecret`/`AdminClientSecret` values (blanked to `""`), and
`Keycloak:ClientSecret`/`AdminClientSecret` reject empty **and** `devOnly`-prefixed
values outside `Development` (fail-fast at startup). The dev-only baseline realm
also moved from `deploy/keycloak/import/` (the prod import path — must stay
empty/gitignored, ops drop their own real realm export there) to
`deploy/keycloak/dev-import/realm-export.json` (tracked, dev-only secrets,
used by `docker-compose.override.yml` and `AppHost.cs` only).

`KEYCLOAK_CLIENT_SECRET`/`KEYCLOAK_ADMIN_CLIENT_SECRET` in `.env.example` must
match `deploy/keycloak/dev-import/realm-export.json`'s `exam-client`/`exam-admin`
`secret` fields — if you change one, change both, or login/service-to-service
tokens fail. Running Aspire instead of docker-compose? Same two dev-only values
live in `AppHost/appsettings.json`'s `Parameters` section
(`keycloak-client-secret`, `keycloak-admin-client-secret`).

**If you already have a local `.env`** (created before this change), add the two
new lines manually — `cp .env.example .env` again would overwrite the rest of
your `.env`:
```bash
echo 'KEYCLOAK_CLIENT_SECRET=devOnlyExamClientSecretChangeMe12345678' >> .env
echo 'KEYCLOAK_ADMIN_CLIENT_SECRET=devOnlyExamAdminClientSecretChangeMe12345678' >> .env
```

**If you already have a running Keycloak container/volume** (created before this
change), its realm was imported with the *old* masked (`**********`) secret —
`--import-realm` does not re-import into an existing realm, so recreating `.env`
alone is not enough. Pick one:
- **(a) Update the running Keycloak** — admin console
  (http://localhost:8081, or `:8082/admin/` under Aspire) → Clients →
  `exam-client` / `exam-admin` → Credentials → regenerate/paste the new
  `devOnlyExamClientSecretChangeMe12345678` / `devOnlyExamAdminClientSecretChangeMe12345678`
  secret to match `.env`/`AppHost/appsettings.json`.
- **(b) Keep your old secret instead** — put your existing (pre-#238) client
  secret value in `.env` (`KEYCLOAK_CLIENT_SECRET`/`KEYCLOAK_ADMIN_CLIENT_SECRET`)
  or, for Aspire, override via `dotnet user-secrets set "Parameters:keycloak-client-secret" "<value>"`
  (and `...-admin-client-secret`) in `AppHost/`, instead of the new dev-only
  default — either way, don't let it fall through to the fail-fast `devOnly`
  check by leaving it unset.

Otherwise, drop the Keycloak container + its Postgres `keycloak` database/volume
and let it re-import fresh with the new dev-only secret.

**Issue #279 (item 1)**: RabbitMQ no longer has a single shared admin user
(`RABBITMQ_DEFAULT_USER`/`PASS`) that every consumer/publisher reused. Each
service now connects as its own least-privilege user, defined in
`rabbitmq/definitions.json` and loaded via `management.load_definitions`
(`rabbitmq/rabbitmq.conf`, bind-mounted by both `docker-compose.yml` and
`AppHost.cs`):

RabbitMQ permission semantics matter here: `basic.publish` (actually sending
a message to an exchange) requires **`write`** on that exchange; `configure`
only covers declaring/deleting it. Consumers *must not* get `write` on the
message-type exchanges they subscribe to — MassTransit's receive-endpoint
setup only needs `configure` (declare) + `read` (bind-as-source, matching
`exchange.bind`'s "read on source, write on destination" rule) on those
exchanges, and `write` only on the consumer's own endpoint queue/exchange
(the bind *destination*). Giving a consumer `write` on a message exchange
would let it publish (forge) that event, not just consume it — exactly the
risk this issue closes. Exchange names follow MassTransit's default RabbitMQ
formatter, `<Namespace>:<TypeName>` (colon, not dot) — e.g.
`ExamApp.Foundation.Contracts:AnswerSubmittedEvent` (verified: no
`SetEntityNameFormatter`/custom `MessageTopology` override anywhere in the
codebase).

| User | Service | `configure` (declare) | `write` (publish / bind-destination) | `read` (bind-source / consume) |
|---|---|---|---|---|
| `rabbituser` | (admin — management UI, `:15672`) | `.*` | `.*` | `.*` |
| `exam_outbox_pub` | exam-outbox-publisher (publisher only, no queue) | worksheet-DB outbox event exchanges (`AnswerSubmittedEvent`, `QuestionCreatedEvent`, `WorksheetReminderDueEvent`, `WorksheetAccessRequested/Approved/RejectedEvent`, `TeacherApplicationSubmitted/DecidedEvent`, `IndependentTeacherRegisteredEvent`, `BookingRequestCreated/DecisionEvent`, `UserPreferredLocaleChangedEvent`) | same as `configure` | nothing (`^$`) |
| `identity_outbox_pub` | identity-outbox-publisher (publisher only) | `LoginAttemptedEvent`, `UserPreferredLocaleChangedEvent` | same | nothing (`^$`) |
| `badge_outbox_pub` | badge-outbox-publisher (publisher only) | `StudentPointsChangedEvent` — the **only** user allowed to declare/publish it | same | nothing (`^$`) |
| `badge_service` | exam-badge-api (BadgeService, consumer) | own `badge-service`(`_error`\|`_skipped`) queue/exchange **+** every event exchange except `StudentPointsChangedEvent` (to declare/bind them) | own `badge-service`(`_error`\|`_skipped`) **only** — no message exchange | own queue/exchange/error/skipped **+** every event exchange except `StudentPointsChangedEvent` (bind source + consume) |
| `exam_api` | exam-dotnet-api (consumer) | own `exam-api`(`_error`\|`_skipped`) queue/exchange **+** `StudentPointsChangedEvent` | own `exam-api`(`_error`\|`_skipped`) **only** — no message exchange | own queue/exchange/error/skipped **+** `StudentPointsChangedEvent` |

This closes the "fake event" risk called out in issue #279 on two levels:
first, only `badge_outbox_pub`/`exam_outbox_pub`/`identity_outbox_pub` have
any exchange access at all (consumers never do beyond `read`+`configure`, no
consumer has `write` on a message exchange); second, within the publishers,
only `badge_outbox_pub` can publish `StudentPointsChangedEvent` and only
`exam_outbox_pub` can publish `AnswerSubmittedEvent`/`TeacherApplicationDecidedEvent`
— `badge_service`/`exam_api` cannot forge any of these even though they
consume them, since `basic.publish` needs `write`, which they don't have on
those exchanges.

Passwords are dev-only defaults in `.env.example`
(`RABBITMQ_EXAM_OUTBOX_PASSWORD`, `RABBITMQ_IDENTITY_OUTBOX_PASSWORD`,
`RABBITMQ_BADGE_OUTBOX_PASSWORD`, `RABBITMQ_BADGE_SERVICE_PASSWORD`,
`RABBITMQ_EXAM_API_PASSWORD`) and `AppHost/appsettings.json`'s `Parameters`
section (`rabbitmq-*-password`) — same pattern as the Keycloak secrets above:
if you change one, change both **and** regenerate that user's
`password_hash` in `rabbitmq/definitions.json` with
`python3 rabbitmq/generate-password-hashes.py` (usernames themselves are
fixed, non-secret literals, not `.env` values).

**`load_definitions` actually runs on every node boot, not just a fresh
node** (per RabbitMQ's own definitions-import docs; the `definitions.skip_if_unchanged`
option added in 3.10+ — which lets a node skip reprocessing an unchanged file
via checksum — only makes sense if import otherwise happens on every boot).
Boot-time import **defines any users/vhosts/permissions/etc. from the file
that don't already exist**; it does **not** delete objects that exist but
aren't in the file, and it does **not** overwrite an existing user's password
if that user already exists. So if you already have a local RabbitMQ
volume/container from before this change (created with just
`RABBITMQ_DEFAULT_USER`/`PASS`, no `definitions.json`):
- **A plain restart is enough for the 5 new per-service users** —
  `docker-compose restart rabbitmq` (or `docker-compose down` + `up -d`
  *without* removing the volume) re-runs boot-time import, which creates
  `exam_outbox_pub`/`identity_outbox_pub`/`badge_outbox_pub`/`badge_service`/
  `exam_api` (they don't exist yet) and their permissions, without touching
  existing queues/messages or the pre-existing `rabbituser`.
- **Rotating `rabbituser`'s own password is the one case a restart doesn't
  cover** — since it already exists, boot import won't update its password
  even if you change `RABBITMQ_DEFAULT_PASS`/`definitions.json`'s hash for
  it. Either delete that one user first (management UI → Admin → Users, or
  `rabbitmqctl delete_user rabbituser`) and let the next boot recreate it
  from the file, or update its password directly via the management UI.
- If you'd rather not rely on any of the above, wiping the volume (delete
  `./rabbitmq/data` / the Aspire RabbitMQ volume) and starting fresh always
  works too — just loses existing queued messages.

**The `rabbituser` admin password (management UI login, `:15672`) comes from
`rabbitmq/definitions.json`'s `password_hash`, not from `.env`/`AppHost`
directly** — changing `RABBITMQ_DEFAULT_PASS` in `.env` (or the
`rabbitmq-password` parameter in `AppHost/appsettings.json`) alone does
**not** change what you log in with, because `load_definitions` is what
actually sets it, and (per the previous paragraph) it won't even do that for
an already-existing user without deleting it first. To rotate it end-to-end:
update `.env`'s `RABBITMQ_DEFAULT_PASS` (and/or `AppHost`'s
`rabbitmq-password`), regenerate the hash with
`python3 rabbitmq/generate-password-hashes.py` and paste it into
`rabbitmq/definitions.json`'s `rabbituser` entry, **then** delete the
existing `rabbituser` (management UI or `rabbitmqctl delete_user rabbituser`)
so the next boot's import recreates it with the new hash.

## Port map (host → container)

| Service | Host port | Notes |
|---|---|---|
| exam-dotnet-api | 5079 (HTTP), 8005 (HTTPS) | ana backend API |
| ocelot-gateway | 5678 | tüm client trafiği buraya gelir |
| auth-api | 6079 (HTTP, yalnız `127.0.0.1`, #100) | Keycloak yönetim — client trafiği gateway üzerinden |
| exam-badge-api | 5080 (HTTP), 8006 (HTTPS) | BadgeService / event handler |
| exam-outbox-publisher | 5081 (HTTP), 8007 (HTTPS) | outbox → RabbitMQ (worksheet DB) |
| identity-outbox-publisher | 5082 (HTTP), 8008 (HTTPS) | outbox → RabbitMQ (identity/auth-api DB) |
| badge-outbox-publisher | 5083 (HTTP), 8009 (HTTPS) | outbox → RabbitMQ (badge DB; puan senkronu #225) |
| angular-app | 4200 | ana UI |
| auth-ui | 4201 | Keycloak login akışı |
| keycloak | 8081 | admin console: http://localhost:8081 |
| PostgreSQL | 5433 | bağlantı: localhost:5433 |
| pgAdmin | 5051 | http://localhost:5051 |
| RabbitMQ AMQP | 5672 (`127.0.0.1` only, #279) | backend bağlantısı |
| RabbitMQ UI | 15672 (`127.0.0.1` only, #279) | http://localhost:15672 |
| Redis | 6379 | |
| MinIO API | 9000 | S3-compat storage |
| MinIO UI | 9001 | http://localhost:9001 |
| question-detector | 8080 | FastAPI (YOLO servisi) |
| jitsi-web | 8000 (HTTP, `127.0.0.1` only) | http://localhost:8000 — tarayıcı doğrudan buraya gider, gateway'den geçmez |
| jvb (Jitsi video bridge) | 10000/udp | medya trafiği |
| prosody / jicofo (Jitsi XMPP/focus) | dışa açık port yok | sadece `mynetwork` içinden erişilir |

## Run services locally (without Docker)

```bash
# Backend API
cd api/ExamApp.Api
dotnet run

# Angular UI
cd ui
ng serve

# question-detector (Python)
cd question-detector
python main.py
```
