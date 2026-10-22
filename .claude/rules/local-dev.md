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
| `exam_outbox_pub` | exam-outbox-publisher (publisher only, no queue) | worksheet-DB outbox event exchanges (`AnswerSubmittedEvent`, `QuestionCreatedEvent`, `WorksheetReminderDueEvent`, `WorksheetAccessRequested/Approved/RejectedEvent`, `TeacherApplicationSubmitted/DecidedEvent`, `IndependentTeacherRegisteredEvent`, `BookingRequestCreated/DecisionEvent`, `BookingTeacherUnavailableEvent` (#298), `UserPreferredLocaleChangedEvent`, `WorksheetCommentCreated/Replied/HiddenEvent` (#105, #326 D4), `DirectMessageSentEvent`, `DirectMessageReportedEvent` (#106 dilim b), `ParentLinkedEvent`, `ParentUnlinkedEvent` (#419; consumer #423), `ParentHomeworkOverdueEvent`, `ParentChildTestCompletedEvent` (#423), `BookingParentApprovalRequestedEvent`, `BookingParentDecisionEvent` (#439), `ParentInvitationRequestedEvent` (#440), `StudentParentGraceReminderEvent` (#441)) | same as `configure` | nothing (`^$`) |
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

**Issue #438 — parent-created child accounts (username login).** A parent opens a child account from
*Çocuklarım* (`POST /api/exam/parent/children` → auth-api `api/auth/child-accounts`, service token only,
blocked at the gateway). The Keycloak user has **no email**, so the realm needs
`registrationEmailAsUsername: false` (otherwise `error-user-attribute-required: email`) and the
`VERIFY_PROFILE` required action **disabled** (email is required for the `user` role in the user profile;
with it on, every child login fails with "Account is not fully set up"). The dev export carries both;
on an **already-running Keycloak** set them by hand: admin console → `exam-realm` → Realm settings → Login →
*Email as username* Off, and Authentication → Required actions → *Verify Profile* Off. With *Email as
username* off, Keycloak's own registration form also asks for a username. The login page label comes from
the theme (`usernameOrEmail` = "E-posta veya kullanıcı adı"); restart Keycloak if theme caching is on.

*VERIFY_PROFILE off — trade-off.* Keycloak no longer forces **any** user to complete a profile that is
missing a required attribute: a user created by an admin/IdP without email/name logs in as-is instead of
landing on the "update your profile" page. Email-registered users are unaffected (registration and auth-ui
`complete-profile` still collect email/name), and the exam API never relied on Keycloak's profile page —
so the only loss is that backstop. `registrationAllowed` stays **true**: the "register as …" links go to
Keycloak's own `registrations` endpoint (`OidcLoginRedirect`), so turning it off would break sign-up.

*Child role set (review fix).* auth-api strips the realm default composite (`default-roles-exam-realm`,
which carries `account`/`manage-account`) from child accounts and maps only `offline_access`,
`uma_authorization`, `Student` and `account`/`view-profile` (keeps `aud=account`). It also writes a
`child_request_id` attribute (idempotent create; compensation via the gateway-blocked
`DELETE api/auth/child-accounts/by-request/{requestId}`) — needs `unmanagedAttributePolicy=ADMIN_EDIT`
in the user profile (dev export has it; otherwise create fails with 502 and nothing is left behind).
**Already-running Keycloak:** nothing to change for new children; children created **before** this fix
still have `default-roles-exam-realm` — admin console → Users → *(child)* → Role mapping → unassign
`default-roles-exam-realm`, assign `offline_access`, `uma_authorization`, `Student` and client role
`account` → `view-profile` (or delete and recreate the local test child). Residual risk, by design:
Keycloak's app-initiated actions (`kc_action=UPDATE_PASSWORD` / `UPDATE_PROFILE`) do not check account
roles, so a child who crafts such a login URL can still change their own password/name/email. Worst
case chain: the child adds an email via `kc_action=UPDATE_PROFILE`, then (realm `resetPasswordAllowed=true`)
uses "Şifremi unuttum" to change the password without the parent knowing. Closing it (user-profile
`email` edit = admin only) affects every user, so it is deferred to #449 (parent adds an email to the
child); see `deploy/README.md` #438.

*Create/compensation lock.* auth-api's identity DB has a new table `ChildAccountRequests`
(migration `AddChildAccountRequests`, applied by auth-api on start): a compensation that arrives before or
during a create marks the request `Cancelled`, and the create then deletes its own Keycloak user instead of
finishing. *Admin reset:* `POST api/admin/students/{id}/reset-password` on a parent-created child sets a
**permanent** password (`permanent: true` in the response, no forced change) — hand it to the parent.

**Issue #440 / #441 — student-invited parent + grace period.** A self-registering student must give parent
name/email/phone (`student/register`, or later `POST /api/exam/student-parent/invitation`). The exam API asks
auth-api (`POST api/auth/parent-accounts/lookup`, read-only, service token only, gateway-blocked): an
existing parent approves in *Çocuklarım* (consent checkbox); otherwise **no account is created at invite
time** — a single-use 7-day link `/app/parent-activate#token=...` (token in the URL fragment, never sent to
servers) is sent via `ParentInvitationRequestedEvent` → BadgeService `IEmailSender`, and the Keycloak user
(email username, Parent role, email verified, permanent password) is created only when the parent sets the
password (`POST api/auth/parent-accounts/activate`). A target email gets at most 3 new invitations per 24 h.
Only a parent account with a **verified** email gets the in-app approval; an unverified (or never-logged-in)
parent account gets the token link too, and if any account exists for the email the anonymous activation
answers 409 `LoginRequired` — the parent logs in and accepts (`POST /api/exam/parent-activation/accept`,
authenticated; auth-api checks the account's server-side email and then marks it verified). The activation
rate limit trusts `X-Forwarded-For` only from loopback or `ForwardedHeaders:KnownNetworks`
(docker-compose sets `172.16.0.0/12`; k8s `REPLACE_POD_CIDR` in `deploy/gcp/k8s/apps.yaml`).
**No SMTP locally:** the activation URL is logged by BadgeService's dev sender and, **only in Development**,
returned to the student as `student.parentStatus.invitation.devActivationUrl` (process memory — lost on API
restart; use "Yeniden gönder" after 24 h or read the BadgeService log). Keycloak `execute-actions-email` is
not used. The link base is `ParentRequirement:ActivationBaseUrl`, else `Server:BaseUrl` (gateway).
Until the parent activates, booking, direct messages, public comments and the leaderboard return 403
`ParentActivationRequired`.

#441: students without an Active parent get `ParentRequirement:GraceDays` (default 30) from their **first
login** or their first invitation, whichever comes first (`Students.ParentRequirementStartedAt`, stamped by
`POST api/auth/refresh` and by starting an invitation). After grace ends an open invitation keeps the
restricted mode only while its token is valid; otherwise every non-allowlisted exam API endpoint returns 403
`ParentRequired` (data is kept; the expired invitation is still returned so the UI can offer resend/fix).
The status cache (`ParentRequirement:StatusCacheSeconds`, default 30) is per process. To simulate
locally: set `ParentRequirement__GraceDays=0` on exam-dotnet-api (docker-compose env / AppHost
`.WithEnvironment(...)`) → expired on the next request; or shorten one student by hand:
`UPDATE "Students" SET "ParentRequirementStartedAt" = now() - interval '24 days' WHERE "UserId" = <id>;`
(7 days left → final-warning modal; the daily `parent-grace-reminder` Hangfire job, triggerable from
`/hangfire`, then emits the 7-day reminder). Admin dashboard summary shows `studentsWithoutParent`,
`studentsInParentGrace`, `studentsParentGraceExpired`, `studentsAwaitingParentActivation`.

**Issues #440/#441 — parent activation links.** The parent invitation email (BadgeService `IEmailSender`; default
`LoggingEmailSender` logs the link only in Development, otherwise a "no email provider configured" warning) carries a
one-time activation link. Its URL is AES-256-GCM encrypted (`ActivationLinkProtector`, `ParentInvitationRequestedEvent.ProtectedActivationUrl`)
so the token never sits in plaintext in `OutboxMessages`/RabbitMQ/the error queue; exam API encrypts and BadgeService decrypts
with the same key `ParentInvitations__LinkEncryptionKey` (docker-compose: `PARENT_INVITATION_LINK_KEY` in `.env`; Aspire:
`AppHost/appsettings.json` `Parameters:parent-invitation-link-key`; prod: `deploy/.env.prod.example` / k8s
`examapp-secrets.PARENT_INVITATION_LINK_KEY`). It must be base64 of exactly 32 bytes; outside Development a missing or `devOnly`
(decoded) value fails startup. Generate one: `openssl rand -base64 32`.
- **Existing local `.env`:** add the line, otherwise `docker-compose up` refuses to start:
  ```bash
  echo 'PARENT_INVITATION_LINK_KEY=ZGV2T25seVBhcmVudEludml0YXRpb25MaW5rS2V5ISE=' >> .env
  ```
- **Aspire:** restart the AppHost to pick up the new parameter.
- `ParentRequired` (student account restriction until a parent is linked) is enforced ONLY in exam API; BadgeService
  notifications and auth-api stay reachable for restricted students — deliberate, so the reminder notifications still arrive.

**QuestionTrainer — soru tespit modeli eğitim verisi yetkisi.** Yeni realm rolü `QuestionTrainer`: soru
tuvalinde "Kaydet ve Düzelt" / "Eğit" ile question-detector'a eğitim verisi (`send-to-fix`) gönderebilen yetkili
öğretmen. Gateway (`Services/Gateway/RouteRoleAuthExtensions.cs`) `send-to-fix`'i yalnız `Admin` + `QuestionTrainer`'a,
`predict`/`headerlist`/`read-qr`'ı `Teacher` + `Admin`'e açar; UI düğmeleri token'daki realm rolüne bakar
(`hasRealmRole`). Düz "Kaydet" kimse için eğitime göndermez. Uygulama rolü değildir: auth-api `Users.Role`'e yazılmaz,
auth-ui `user_role`'ü Admin/Teacher/Student/Parent içinden seçer. Dev export rolü taşır; **çalışan Keycloak'ta** elle
oluştur: admin console → `exam-realm` → Realm roles → Create role `QuestionTrainer`; yetkili öğretmene Users →
*(öğretmen)* → Role mapping → Assign role. Rol yeni token'da gelir (çıkış/giriş). Öğretmen askıya alınınca rol
**otomatik kalkmaz** (gateway yalnız token rolüne bakar, onay/askı exam API'de) — elle Unassign et. Ayrıntı: `question-detector/EGITIM.md`.

**İşaretleme ekranı — Claude ön-etiketleme (feat/claude-labeling).** İşaretleme modunda (`/labeling`) araç çubuğunda
**Kaynak: Manuel | Model | Claude** seçimi var. Model = question-detector YOLO (her açılışta taze), Claude = exam API
`POST /api/exam/labeling/ai-detect` (Microsoft.Extensions.AI `IChatClient` + Anthropic C# SDK; sayfa → soru kutuları,
her soru kırpması → şık kutuları), Manuel = eğitime gönderilen son hal (send-to-fix başarılı olunca
`PUT /api/exam/labeling/pages/manual`; soru tuvalindeki "Kaydet ve Düzelt" de yazar). Sonuçlar exam DB'de
`LabelingPages`/`LabelingResults` (sayfa = ham görsel SHA-256; question-detector `content_id` = ilk 16 hane); aynı sayfa +
model için kayıtlı Claude sonucu varsa API'ye gidilmez ("Tekrar AI'a sor" zorlar, ücretli). Uçlar yalnız Admin +
QuestionTrainer (onaylı, askıda olmayan öğretmen; gateway'de de kural var).
- **API anahtarı repoya girmez.** Aspire: `dotnet user-secrets set "Parameters:anthropic-api-key" "<anahtar>" --project AppHost`
  (AppHost `anthropic-api-key` secret parametresi → exam API `Anthropic__ApiKey`); AppHost'u yeniden başlat. Anahtar
  yoksa model listesi boş döner, UI Claude seçeneğini kapatır, `ai-detect` 503. docker-compose'da tanımlı değil.
- **Prompt ekrandan düzenlenir:** işaretleme araç çubuğunda **Prompt** → iki aşamanın talimatı (`{width}`/`{height}`
  yer tutucuları), soru/şık kutusu payı (px) ve "soru kutusu şıkları kapsasın". Her kayıt yeni sürüm
  (`LabelingPromptVersions`, geçmiş silinmez; tablo boşsa koddaki varsayılanlar = sürüm 0). Koordinat kuralı ve JSON şeması
  kodda sabit. AI sonuçları `PromptVersion` (gösterim) ve `PromptHash` (önbellek anahtarı: talimatlar + paylar +
  `LabelingPromptSettings.SchemaRevision`) taşır; içerik değişince eski sonuç kayıttan dönmez, Claude yeniden çağrılır.
  Aynı içerikle Kaydet yeni sürüm açmaz. Koddaki sabit kısım (sistem mesajı, şema, kırpma) değişirse `SchemaRevision`'ı artır.
  Düzenleme: Admin + QuestionTrainer (`GET/PUT /api/exam/labeling/prompt`).
- Ayarlar `api/ExamApp.Api/appsettings.json` → `Anthropic`: `Models` (izinli id + 1M token fiyatı, maliyet tahmini
  için; varsayılan seçili model Haiku 5.5 — tarayıcıda daha önce başka model seçildiyse localStorage `labeling.aiModel` onu tutar), `Effort` (varsayılan `low`), `MaxImageEdge` (1568), `MaxParallelCrops` (4), `MaxQuestionsPerPage` (40),
  `LiveCallsPerHour` (kullanıcı başına, 60), `DailyBudgetUsdPerUser` (5) / `DailyBudgetUsdTotal` (25) — 24 saatlik
  tahmini harcama tavanı (Redis sayaç; dolunca 429 `LabelingBudgetExceeded`). Yalnız canlı Claude çağrıları sayılır.
- Kaba ölçüm (2026-10-22, A4 test sayfası, effort low): Opus 5.5 ~7-13 sn, ~$0.04-0.05/sayfa; Haiku 5.5 ~11 sn,
  ~$0.001/sayfa — Ocelot'un varsayılan 90 sn zaman aşımının altında.

**Issue #470 — işaretleme ekranından soru bankasına kayıt.** Araç çubuğunda **Bankaya kaydet** = bankaya kayıt + başarılıysa
**Eğit** ile aynı akış (send-to-fix → manuel kayıt → sonraki sayfa; eğitim başarısızsa sayfa açık kalır, Eğit ile yeniden
denenir; kayıt başarısızsa eğitime gönderilmez). Düz **Eğit** değişmedi (bankaya yazmaz). `POST /api/exam/labeling/pages/bank` sayfadaki MCQ soruları müfettişte
seçilen doğru şıkla, teste bağlamadan (`WorksheetQuestion` yok) mevcut toplu kayıtla (`SaveBulkQuestion`, `TestId=null`)
oluşturur — kırpma + MinIO + `QuestionCreatedEvent` (sınıflandırma otomatik), sahibi çağıran. Uç ek olarak Teacher|Admin
ister (QuestionTrainer tek başına yetmez); kullanıcı başına saatte 30 kayıt (429). Aynı sayfa (`LabelingPages` SHA-256;
`BankSavedAt`/`BankQuestionCount`, süren kayıt `BankSaveStartedAt` — 10 dk sonra bayat; migration `AddLabelingPageBankSave`)
ikinci kez kaydedilmek istenirse 409 `LabelingPageAlreadyInBank` → UI "Yine de kaydet" (force; sorular ikinci kez oluşur;
süren kayıt force ile de aşılmaz). Şık kutuları soru kutusuna kırpılır. Pasaj ve kitap adı (`BookName`) bu ekrandan kaydedilmez (açık soru).

**Issue #472 — test ekranında "Bankadan ekle".** Test detayında (`/test/:id`, düzenleyebilen öğretmen/admin) **Bankadan Ekle**
soru bankası panelini seçim modunda açar; seçilenler `POST /api/exam/questions/test/{testId}/bank-questions` `{questionIds}` (en çok 60)
ile teste **bağlanır** (kopyalanmaz — test kopyalamayla aynı model; soruyu düzenlemek yine yalnız sahibine/admine açık, puan soruda kalır),
testin sonuna istek sırasıyla; testte olanlar atlanır ve panelde pasif görünür. Bağlanabilir soru: kendi sorusu ya da görebildiği teste
bağlı, sahibi damgalı soru (`CanLink`). Başkasının sahipsiz (legacy, `CreateUserId` 0/null) sorusu bağlanamaz — legacy sahiplik "soruyu
içeren kopya olmayan testin sahibi" kuralıyla türetildiği için bağlamak düzenleme yetkisi verirdi (admin başkasının orijinal testine
eklerken de). Bir soru bile bağlanamazsa hiçbir şey yazılmaz (404). Test başına advisory lock; kullanıcı başına dakikada 30 istek
(`RateLimiting:QuestionBankLink`, 429 `questions.bankLink.rateLimited`). Migration yok.

**Issue #469 — Claude soru sınıflandırıcısı.** BadgeService'te `QuestionAnalyzer:Provider` = `Gemini` (varsayılan) |
`Claude`; geçersiz değer açılışta patlar. Claude seçiliyken (`QuestionAnalyzer__Provider=Claude`) BadgeService
taksonomiyi exam API `GET /api/questions/classifier-taxonomy`'den (Admin/servis) alır (`Anthropic:TaxonomyCacheSeconds`,
varsayılan 300 sn bellek önbelleği) ve sistem mesajının sonuna prompt-cache breakpoint'iyle koyar (`Anthropic:CacheTtl`
`1h`|`5m`); yanıt JSON şemalı (structured output). Taksonomi dışı id dönerse sonuç yazılmaz, loglanır ve mesaj
`badge-service_error` kuyruğuna düşer (Gemini/exam API reddiyle aynı). Her çağrıda input / cacheWrite / cacheRead /
output token'ı ve tahmini USD loglanır (`Anthropic:InputUsdPerMTok`/`OutputUsdPerMTok`, varsayılan model Haiku 5.5).
- **Anahtar:** Aspire'da işaretleme ekranıyla aynı `anthropic-api-key` parametresi BadgeService'e de `Anthropic__ApiKey`
  olarak gider (AppHost'u yeniden başlat). Anahtar yoksa sınıflandırma uyarıyla atlanır. docker-compose'da tanımlı değil.
- `QuestionAnalyzer:AIActive=false` iken sağlayıcıdan bağımsız hiçbir sınıflandırma yapılmaz.

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
