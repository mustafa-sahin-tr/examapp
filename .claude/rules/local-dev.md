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

## Port map (host → container)

| Service | Host port | Notes |
|---|---|---|
| exam-dotnet-api | 5079 (HTTP), 8005 (HTTPS) | ana backend API |
| ocelot-gateway | 5678 | tüm client trafiği buraya gelir |
| auth-api | 6079 (HTTP), 9005 (HTTPS) | Keycloak yönetim |
| exam-badge-api | 5080 (HTTP), 8006 (HTTPS) | BadgeService / event handler |
| exam-outbox-publisher | 5081 (HTTP), 8007 (HTTPS) | outbox → RabbitMQ (worksheet DB) |
| identity-outbox-publisher | 5082 (HTTP), 8008 (HTTPS) | outbox → RabbitMQ (identity/auth-api DB) |
| badge-outbox-publisher | 5083 (HTTP), 8009 (HTTPS) | outbox → RabbitMQ (badge DB; puan senkronu #225) |
| angular-app | 4200 | ana UI |
| auth-ui | 4201 | Keycloak login akışı |
| keycloak | 8081 | admin console: http://localhost:8081 |
| PostgreSQL | 5433 | bağlantı: localhost:5433 |
| pgAdmin | 5051 | http://localhost:5051 |
| RabbitMQ AMQP | 5672 | backend bağlantısı |
| RabbitMQ UI | 15672 | http://localhost:15672 |
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
