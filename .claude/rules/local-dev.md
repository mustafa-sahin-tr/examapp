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

**Issue #372**: service-to-service tokens (BadgeService and exam API
`ServiceTokenProvider`) are no longer obtained with the `exam-admin` client. A new
confidential client `exam-service` (service account holds **only** the
`exam-service` realm role, no `realm-management` roles) is defined in
`deploy/keycloak/dev-import/realm-export.json`; `exam-admin` lost `manage-realm`
and the `exam-service` role (it keeps `view-realm`, `manage-users`, `view-users`,
`query-users` — Keycloak admin REST for auth-api/`KeycloakService`).
`ServicePrincipal.IsService` now accepts only the `exam-service` role or an
`azp` in the explicit `Keycloak:ServiceClients` list (no implicit `exam-admin`
default, no `preferred_username` fallback). There is **no fallback** to
`exam-admin`/`exam-client` credentials in the token providers.
`KEYCLOAK_SERVICE_CLIENT_SECRET` (`.env.example`, docker-compose) /
`keycloak-service-client-secret` (`AppHost/appsettings.json` `Parameters`) must
match `exam-service`'s `secret` in the realm export
(`devOnlyExamServiceClientSecretChangeMe12345678`); exam API and BadgeService
fail fast outside Development on an empty/`devOnly` `Keycloak:ServiceClientSecret`.

**If you already have a local `.env`**, add the new line manually:
```bash
echo 'KEYCLOAK_SERVICE_CLIENT_SECRET=devOnlyExamServiceClientSecretChangeMe12345678' >> .env
```

**If you already have a running Keycloak container/volume**, `--import-realm`
does not re-import, so the `exam-service` client does not exist yet and
service-to-service calls (BadgeService <-> exam API <-> auth-api) return
`invalid_client`. Pick one:
- **(a) Add it manually** — admin console (http://localhost:8081, or `:8082/admin/`
  under Aspire) → exam-realm → Clients → Create client: Client ID `exam-service`,
  Client authentication **On**, Service accounts roles **On** (Standard flow /
  Direct access grants Off) → Credentials → set Client secret to
  `devOnlyExamServiceClientSecretChangeMe12345678` → Service account roles →
  Assign role → realm role `exam-service` (and make sure no `realm-management`
  roles are assigned). Then, **last**, on `exam-admin` → Service account roles:
  unassign `manage-realm` and the `exam-service` realm role.
- **(b) Drop only the Keycloak database** and let it re-import fresh. Under
  Aspire the Keycloak data lives in the `keycloak` database inside the persistent
  Postgres volume (`examapp-postgres-data`) and in `examapp-keycloak-data`: drop
  just the `keycloak` DB (e.g. `DROP DATABASE keycloak;` from pgAdmin/psql, then
  restart the `keycloak` resource) and, if needed, the `examapp-keycloak-data`
  volume — **never** `examapp-postgres-data`, which also holds the worksheet,
  identity and badge databases.

Seed note (#372): `partialImport` needs `manage-realm`, which no service account
has any more, so the `partial-import` mode of `seed-teachers`/`seed-tutors` and of
auth-api's dev seed-users endpoint was removed (`--keycloak-mode partial-import`
fails with a clear message; `admin-api`, the default, covers the same ground via
`POST /users` + role mapping under `manage-users`). `exam-admin` still needs
`view-realm` (realm default role / realm role lookups).

Order matters: create `exam-service` and restart exam API + BadgeService first,
only then trim `exam-admin`; the other way round breaks service-to-service calls.

**Issue #347 — PKCE + state on the login flow.** auth-ui (`/app/login`)
now generates a random `state` and a PKCE `code_verifier` per login (kept in
`sessionStorage`, key `oidc_flow:<state>`), sends `state` + S256
`code_challenge` through the gateway's `/oidc-login` to Keycloak, verifies
`state` on `/app/callback` before exchanging the code, and posts
`{ code, codeVerifier }` to `/api/auth/exchange`; auth-api forwards it as
`code_verifier` (and rejects an exchange without one with 400). `/oidc-login`
without a valid `state`/`code_challenge` no longer goes to Keycloak — it
redirects to `/app/login` (landing "register as ..." links now point to
`/app/login?intent=...`). The dev realm export enforces PKCE on `exam-client`
(`attributes."pkce.code.challenge.method": "S256"`).
A Keycloak `?error=` return (e.g. the user cancelled) consumes the record and
shows a "Tekrar dene" button instead of auto-redirecting; going back to
`/app/callback` after a successful login redirects to the dashboard when the
stored token is still valid.

`exam-client`'s `redirectUris` in the dev export were narrowed from `/*`
wildcards to the exact callback actually used: `http://localhost:5678/app/callback`
(docker-compose **and** Aspire — the gateway is pinned to :5678 and
`Keycloak:RedirectUri`/`Server__BaseUrl` point there) plus the
`https://{,www.,staging.}hedefokul.com/app/callback` entries. A running Keycloak
keeps the old wildcards until changed by hand (Clients → `exam-client` →
Settings → *Valid redirect URIs*). If you open the app on another origin (LAN IP,
another port), add that origin's `/app/callback` there, or login fails with
`Invalid parameter: redirect_uri`.

Known behaviour — email verification in another tab: the record lives in
`sessionStorage`, which is per tab. If Keycloak's verification link is opened
in a new tab and that tab lands on `/app/callback`, there is no record there,
so the user sees "Oturum doğrulanamadı" and is sent to `/login`; the new login
completes silently through the Keycloak SSO session, but the register intent
is lost (complete-profile opens without a pre-selected role). This is
deliberate: sharing state/verifier across tabs (localStorage) would weaken the
login-CSRF protection.

**If you already have a running Keycloak container/volume**, `--import-realm`
does not re-import, so enforcement is **not** on yet (the new code still works
without it — Keycloak verifies the verifier whenever a challenge was sent).
Enable it **only after** the new auth-ui/gateway/auth-api are running (the
client must send PKCE before enforcement, otherwise every login fails with
`Missing parameter: code_challenge_method`): admin console
(http://localhost:8081, or `:8082/admin/` under Aspire) → `exam-realm` →
Clients → `exam-client` → **Advanced** tab → *Advanced settings* →
**Proof Key for Code Exchange Code Challenge Method** = `S256` → Save. Prod
realms (`deploy/keycloak/import/`, ops-managed) need the same setting, see
`deploy/README.md` §5.

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
| `exam_outbox_pub` | exam-outbox-publisher (publisher only, no queue) | worksheet-DB outbox event exchanges (`AnswerSubmittedEvent`, `QuestionCreatedEvent`, `WorksheetReminderDueEvent`, `WorksheetAccessRequested/Approved/RejectedEvent`, `TeacherApplicationSubmitted/DecidedEvent`, `IndependentTeacherRegisteredEvent`, `BookingRequestCreated/DecisionEvent`, `BookingTeacherUnavailableEvent` (#298), `UserPreferredLocaleChangedEvent`, `WorksheetCommentCreated/Replied/HiddenEvent` (#105, #326 D4), `DirectMessageSentEvent`, `DirectMessageReportedEvent` (#106 dilim b)) | same as `configure` | nothing (`^$`) |
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

**Issue #328 — new event exchange permissions.** `load_definitions` runs on
every node boot and upserts the users/permissions from `definitions.json`
(verified: a narrowed permission is restored by a restart). The problem is that
a **running** RabbitMQ does not re-read the file: when you add an event to
`definitions.json`, `docker-compose up -d` does not recreate/restart the
`rabbitmq` container (bind-mount content changed, config did not), so
BadgeService keeps getting `ACCESS_REFUSED - configure access to exchange ...`.
- **docker-compose:** a one-shot `rabbitmq-permission-sync` container
  (`rabbitmq/sync-permissions.sh`, `alpine:3.20`) re-runs on every `up -d`,
  waits for the management API and `POST`s **only the `permissions` block** of
  `definitions.json` (minus the admin user) to `/api/definitions` (an upsert).
  The six RabbitMQ consumers/publishers depend on it
  (`service_completed_successfully`). `definitions.json` stays the single source
  of truth. Users, passwords, queues and exchanges are not touched. Admin
  credentials are `RABBITMQ_DEFAULT_USER`/`PASS` from `.env` and must match the
  existing `rabbituser` in the volume. Network access (`apk add curl jq`) is
  needed only when the sync container is first created.
- **Already-running service that got ACCESS_REFUSED:** it keeps retrying/holding
  the old failure; `depends_on` is not evaluated on restart, so restart it
  explicitly after the sync: `docker-compose restart exam-badge-api`
  (or whichever service).
- **Aspire:** the RabbitMQ resource has no persistent data volume (anonymous
  volume, fresh on every AppHost start), so boot import applies the current
  `definitions.json` each time and no sync is needed. If you edit
  `definitions.json` while the AppHost is running, restart the `rabbitmq`
  resource (`aspire resource rabbitmq restart`).
- **Removals are not reverted:** a permission/user deleted from
  `definitions.json` stays in an existing volume (neither boot import nor the
  sync deletes). Wipe the volume (`./rabbitmq/data`) to get a clean state; this
  loses queued messages.
- **Password rotation is still manual** (not touched by the sync): for a service
  user, change `.env`/`AppHost` parameter **and** `definitions.json`'s
  `password_hash` (`python3 rabbitmq/generate-password-hashes.py`), then delete
  that user (management UI → Admin → Users, or `rabbitmqctl delete_user <name>`)
  and restart so the next boot recreates it. Same for `rabbituser` (next
  paragraph).

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
