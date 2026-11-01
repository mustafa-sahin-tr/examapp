# Yerel geliştirme — kurulum ve geçiş notları

Eskiden `.claude/rules/local-dev.md` içindeydi (her session yükleniyordu); 2026-11-01'de buraya taşındı.
Çoğu bölüm "mevcut `.env` / çalışan Keycloak / RabbitMQ'yu güncelle" notudur. Port tablosu ve başlatma: `.claude/rules/local-dev.md`.

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
| `exam_outbox_pub` | exam-outbox-publisher (publisher only, no queue) | worksheet-DB outbox event exchanges (`AnswerSubmittedEvent`, `QuestionCreatedEvent`, `WorksheetReminderDueEvent`, `WorksheetAccessRequested/Approved/RejectedEvent`, `TeacherApplicationSubmitted/DecidedEvent`, `IndependentTeacherRegisteredEvent`, `BookingRequestCreated/DecisionEvent`, `BookingTeacherUnavailableEvent` (#298), `UserPreferredLocaleChangedEvent`, `WorksheetCommentCreated/Replied/HiddenEvent` (#105, #326 D4), `DirectMessageSentEvent`, `DirectMessageReportedEvent` (#106 dilim b), `ParentLinkedEvent`, `ParentUnlinkedEvent` (#419; consumer #423), `ParentHomeworkOverdueEvent`, `ParentChildTestCompletedEvent` (#423), `BookingParentApprovalRequestedEvent`, `BookingParentDecisionEvent` (#439), `ParentInvitationRequestedEvent` (#440), `StudentParentGraceReminderEvent` (#441), `BookingLifecycleEvent` (#315), `ActivityCompletedEvent` (#572), `SchoolChangedEvent` (#542)) | same as `configure` | nothing (`^$`) |
| `identity_outbox_pub` | identity-outbox-publisher (publisher only) | `LoginAttemptedEvent`, `UserPreferredLocaleChangedEvent` | same | nothing (`^$`) |
| `badge_outbox_pub` | badge-outbox-publisher (publisher only) | `StudentPointsChangedEvent`, `StudentBadgeEarnedEvent` (#422) — the **only** user allowed to declare/publish them | same | nothing (`^$`) |
| `badge_service` | exam-badge-api (BadgeService, consumer) | own `badge-service`(`_error`\|`_skipped`) queue/exchange **+** every event exchange except `StudentPointsChangedEvent`/`StudentBadgeEarnedEvent` (to declare/bind them) | own `badge-service`(`_error`\|`_skipped`) **only** — no message exchange | own queue/exchange/error/skipped **+** every event exchange except `StudentPointsChangedEvent`/`StudentBadgeEarnedEvent` (bind source + consume) |
| `exam_api` | exam-dotnet-api (consumer) | own `exam-api`(`_error`\|`_skipped`) queue/exchange **+** `StudentPointsChangedEvent`, `StudentBadgeEarnedEvent` (#422) | own `exam-api`(`_error`\|`_skipped`) **only** — no message exchange | own queue/exchange/error/skipped **+** `StudentPointsChangedEvent`, `StudentBadgeEarnedEvent` |

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

**Issue #402 (O1) — MinIO presign account.** Image URLs are presigned with a
dedicated MinIO user that only has `s3:GetObject` on the image prefixes (its
access key is visible in `X-Amz-Credential`), never the root user. It is
created by the one-shot `minio-presign-init` container
(`deploy/scripts/minio-presign-init.sh`, idempotent) that exam-dotnet-api waits for.
- **Existing local `.env`:** add the two new lines (values from `.env.example`),
  otherwise `docker-compose up` refuses to start:
  ```bash
  echo 'MINIO_PRESIGN_ACCESS_KEY=exam-presign' >> .env
  echo 'MINIO_PRESIGN_SECRET_KEY=devOnlyMinioPresignSecretChangeMe123' >> .env
  ```
  The access key must differ from `MINIO_ROOT_USER`.
- **Aspire:** defaults live in `AppHost/appsettings.json`'s `Parameters`
  (`minio-presign-access-key`, `minio-presign-secret-key`); **restart the AppHost**
  so the new `minio-presign-init` resource runs before the exam API starts.
- Without the presign credentials (Development only) the API logs a warning and
  emits unsigned image URLs; outside Development it fails at startup.

**Issue #419 — parent invite code pepper.** Parent invite codes are stored only as
HMAC-SHA256(pepper, code); the exam API reads `ParentLinks__InviteCodePepper`
(docker-compose: `PARENT_INVITE_CODE_PEPPER` in `.env`; Aspire: `AppHost/appsettings.json`
`Parameters:parent-invite-code-pepper`; prod: `deploy/.env.prod.example` / k8s
`examapp-secrets.PARENT_INVITE_CODE_PEPPER`). Outside Development a missing, < 32 char or
`devOnly` value fails startup (`ValidateOnStart`). Rotating it only invalidates unused codes.
- **Existing local `.env`:** add the line, otherwise `docker-compose up` refuses to start:
  ```bash
  echo 'PARENT_INVITE_CODE_PEPPER=devOnlyParentInviteCodePepperChangeMe0123456789' >> .env
  ```
- **Aspire:** restart the AppHost to pick up the new parameter.

**Issue #409 — brand name "Hedef Okul".** The dev realm export now sets
`displayName` and `smtpServer.fromDisplayName` to `Hedef Okul` (Keycloak's own
verification/reset emails use the realm display name). `--import-realm` does not
re-import an existing realm, so on an **already-running Keycloak** set them by hand:
admin console (http://localhost:8081, or `:8082/admin/` under Aspire) → `exam-realm` →
Realm settings → **General** → *Display name* = `Hedef Okul`, and → **Email** →
*From display name* = `Hedef Okul`. The login theme title comes from the theme's
`brandName` message (refresh the page; restart Keycloak if theme caching is on).

**Issue #316 / #333 — UI Content-Security-Policy zorlanıyor.** `ui` ve `auth-ui` (dev-server: `angular.json` `serve.options.headers`;
prod: `deploy/nginx/angular-spa.conf.template`) artık `Content-Security-Policy` gönderiyor (Report-Only değil). Satır içi betik,
`on*` öznitelikleri, dış origin'den betik, görsel veya bağlantı tarayıcıda **engellenir**. Yeni bir dış kaynak gerekirse (CDN, embed)
üç yerdeki politikayı birlikte güncelle; `UiContentSecurityPolicyTests` eşitliği denetler. `angular.json` başlığı değişince
dev-server'ı yeniden başlat (`aspire resource angular-app restart` / `auth-ui`). İhlaller `report-uri /csp-report` ile gateway'e gelir
(`Services/Gateway/CspReportExtensions.cs`). Bu uç anonim, IP başına `RateLimiting:CspReport:PermitLimit`/`WindowSeconds`
(varsayılan 60/dk) ve ortak `GlobalPermitLimit` (600/pencere) ile sınırlı ve Ocelot'a gitmez. Raporlar gateway log'una `CSP violation: directive=... blocked=...` biçiminde
Warning olarak düşer; URL'ler sorgu ve fragment atılarak yazılır. `report-to` bilerek kullanılmıyor: varsa Chrome `report-uri`'yi
yok sayıyor, Reporting API ise yerel http'de raporu teslim etmedi.

**Issue #488 — Angular 19.2: dev-server host doğruluyor.** `ui` ve `auth-ui` `start` script'lerinden
`--disable-host-check` kalktı. Dev-server yalnız `angular.json` → `serve.options.allowedHosts` listesindeki host'lara cevap
veriyor: ui `localhost`, `127.0.0.1`, `angular-app`; auth-ui `localhost`, `127.0.0.1`, `auth-ui`. ui'da aynı liste ayrıca
`build.options.security.allowedHosts`'ta duruyor (`server.ts` / `serve:ssr` manifest'i için; dev-server onu değil serve listesini
kullanıyor). Liste dışı Host 403 alır (Vite), liste dışı `X-Forwarded-Host` 400 alır (Angular SSR). LAN IP'leri Vite'ta her zaman
serbest; özel bir hostname'den (tünel, `/etc/hosts` adı) açacaksan listeye ekle ya da gateway'i (`:5678`) kullan. `*` ekleme.
Gateway arkasında ui dev-server SSR'dan CSR'a düşer: host/proto dışındaki `X-Forwarded-*` başlıklarına güvenilmiyor
(`trustProxyHeaders` varsayılan) ve log'da `Received "X-Forwarded-..." header but "trustProxyHeaders" was not set up` uyarısı
çıkar. Bu beklenen davranış, sayfa normal açılır.
- **Merge sonrası:** Aspire'da `aspire resource angular-app restart` ve `aspire resource auth-ui restart`;
  paket kurulumu ve kilit dosyası için bkz. "Yalnız npm".

**Issue #545 — Angular 22.** `ui` ve `auth-ui` Angular 22.2'de (19 → 20 → 21 → 22, her major ayrı commit).
- **Node:** Angular 22 CLI `^22.22.3 || ^24.15.0` ister; CI, `deploy/dockerfiles/angular-spa.Dockerfile` ve
  `dockerfiles/{ui,auth-ui}` `node:24`. Yerelde eski Node varsa `ng` açılmaz — Node 24 kur.
- **Merge sonrası:** Aspire'da `aspire resource angular-app restart` ve `aspire resource auth-ui restart`;
  paket kurulumu için bkz. "Yalnız npm".
- **Proxy başlıkları:** Angular 22'de `AngularNodeAppEngine` varsayılan olarak hiçbir `X-Forwarded-*` başlığına güvenmiyor
  (#488'deki "host/proto'ya güvenilir" davranışı bitti). `trust-proxy-headers` migration'ı `src/server.ts`'e opt-in
  eklemedi (yalnız `main.server.ts`'e bakıyor) ve bilerek eklenmedi: prod statik nginx, dev-server'da SSR gateway
  arkasında zaten CSR'a düşüyor.
- **Davranış korumaları (migration'lar):** bileşenlerde `ChangeDetectionStrategy.Eager` (22'nin varsayılanı OnPush;
  yeni bileşen yazarken stratejiyi bilinçli seç), `provideHttpClient(withXhr(), …)` (22'nin varsayılanı fetch),
  `provideClientHydration(withNoIncrementalHydration())`, uygulama config'inde `provideZoneChangeDetection` (21'den beri
  sağlayıcısız uygulama zoneless). Şablonda `a?.b` artık `null` yerine `undefined` döndürüyor; `null` bekleyen yerde
  `a?.b ?? null` yaz. Hepsi `src/app/angular-upgrade.spec.ts`'te testli.
- **CDK 21+ overlay:** bağlı paneller (mat-select, autocomplete) `OverlayContainer` yerine tetikleyicinin yanında native
  popover olarak açılır; testte paneli `document` üzerinden ara.

**Issue #553 — dev UI imajlarında TLS doğrulaması açık.** `dockerfiles/{ui,auth-ui}` artık
`npm set strict-ssl false` çalıştırmıyor. Kurumsal proxy TLS'i kendi sertifikasıyla açıyorsa (npm `UNABLE_TO_GET_ISSUER_CERT_LOCALLY` /
`SELF_SIGNED_CERT_IN_CHAIN`), proxy'nin kök CA'sını BuildKit secret'ı olarak ver; repoya girmez:
`docker build --secret id=corp_ca,src=/yol/kurum-ca.pem -f dockerfiles/ui/Dockerfile.ui dockerfiles/ui -t exam-angular:latest`
(auth-ui için `auth-ui:latest`), sonra `docker-compose up -d` — **`--build` verme**: compose secret'sız yeniden derler ve CA düşer. Secret verilmezse adım atlanır.

**Yalnız npm — yarn kaldırıldı.** `ui` ve `auth-ui` tek paket yöneticisi olarak npm + `package-lock.json` kullanır;
`yarn.lock` dosyaları silindi (`ui/.gitignore` ve `auth-ui/.gitignore` `yarn.lock`'u yok sayar). Neden: Windows'ta çalıştırılan
`npm install` mevcut `yarn.lock`'u sessizce yeniden yazıp yalnız o makinenin `win32-x64` isteğe bağlı native paketlerini
bırakıyordu; yarn v1 `--frozen-lockfile` mac/linux'ta bunu yakalamadan kilitsiz kuruyordu (#488'e aykırı). CI (`ui-tests`),
docker-compose dev (`npm ci && npm start`), Aspire ve prod imajı artık aynı kilidi kullanır; `resolutions` blokları kalktı,
sabitlemeler yalnız `overrides`'ta.
- **Merge sonrası:** `ui` ve `auth-ui` dizinlerinde `npm ci` (eski `node_modules` ile yarn kalıntısı varsa önce sil), sonra
  `aspire resource angular-app restart` ve `aspire resource auth-ui restart`.
- Kilidi yalnız `npm install --package-lock-only` ile güncelle; diff'te win32/darwin/linux paketlerinden biri kayboluyorsa commit etme.
- docker-compose dev'de `./ui` ve `./auth-ui` bind mount'tur, ama `/app/node_modules` konteynere özel anonim volume'dur:
  konteynerdeki `npm ci` kendi (Linux) `node_modules`'unu kurar, host etkilenmez. Volume'u sıfırlamak için
  `docker-compose rm -sv angular-app auth-ui` sonra `up -d`.

**Dev sıfırlama — soru bankası.** `deploy/scripts/dev-reset-questions.sh` (dev-only) tüm soruları ve FK ile bağlı
her şeyi (test-soru bağları, öğrenci cevapları, yorumlar, LabelingPages/Results), öğrencilerin test oturumlarını
(`TestInstances` — Questions'a FK'sı yok, CASCADE ulaşmaz, açıkça silinir; aksi halde "boş test yapılmış" gibi kalır),
bu cevaplardan türeyen puan/rozet verisini **her iki DB'de** (worksheet: `StudentPoints`, `StudentPointHistories`,
`StudentBadges`, `StudentRewards`, `StudentSpecialEvents`, `Leaderboards`, `StudentBadgeProjections`, `StudentDailyXps`,
`Students.ProgressResetAtUtc`=NULL; badge: `UserResetService`'in sildiği set — `BadgeEarned`, `StudentBadgeProgresses`,
`StudentDailyActivities`, `Student{Question,Subject}Aggregates`, `ProcessedAnswerSubmissions`, `AnswerPointAwards`,
`UserResetMarkers`, `StudentActivityCompletions` (#572), `Type='BadgeEarned'` bildirimleri — tüm öğrenciler için sert silme; uygulamadaki
`POST api/student/me/reset` öğrenci başına ve soft-delete'tir), MinIO `exam-questions`'daki
`questions/ answers/ passages/ question-transfer/` nesnelerini ve bekleyen QuestionCreated / ActivityCompleted / StudentPointsChanged /
StudentBadgeEarned outbox mesajlarını siler; `Worksheets`, taksonomi, kullanıcılar, `BadgeDefinitions` kalır. `BADGE_DB` (varsayılan `badge`) ile badge DB adı seçilir;
`--wipe-dataset` question-detector eğitim verisini de (`dataset/`, Drive arşivine dokunmaz) boşaltır. İsteğe bağlı gruplar:
- `--wipe-tests`: `Worksheets` + `DailyQuestionSets` + `PracticeSessions` (CASCADE: atamalar, hatırlatıcılar, erişim
  istekleri, test yorumları) ve MinIO `worksheets`/`exams` bucket'ları. `Books`/`BookTests` kalır: `StudyItems` onlara FK
  ile bağlı, silmek çalışma sayfalarını ve program takvimlerini götürürdü.
- `--wipe-social`: mesajlaşma tabloları, `WorksheetCommentModerationEvents`, badge DB'de tüm bildirimler.
- `--wipe-logs`: üç DB'de işlenmiş outbox (bekleyen/hatalı kalır), `LoginEvents`, `Admin*Logs`, `QuestionTransfer*`,
  `ProcessedLoginAttempts`, Hangfire'da yalnız bitmiş işler + stats sayaçları (recurring/zamanlanmış işler kalır).
- `--all`: üçü birden (dataset hariç). Veli bağları, kullanıcılar, randevular, çalışma sayfaları, programlar hiçbir
  bayrakta yok (veli bağı silinirse öğrenciler #441 grace süresine düşer).

Önce `--dry-run` (seçilen grupların sayılarını da gösterir); birden fazla Postgres/MinIO konteyneri varsa
`PG_CONTAINER`/`MINIO_CONTAINER` ile seç.
