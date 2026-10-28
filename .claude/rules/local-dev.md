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
| `exam_outbox_pub` | exam-outbox-publisher (publisher only, no queue) | worksheet-DB outbox event exchanges (`AnswerSubmittedEvent`, `QuestionCreatedEvent`, `WorksheetReminderDueEvent`, `WorksheetAccessRequested/Approved/RejectedEvent`, `TeacherApplicationSubmitted/DecidedEvent`, `IndependentTeacherRegisteredEvent`, `BookingRequestCreated/DecisionEvent`, `BookingTeacherUnavailableEvent` (#298), `UserPreferredLocaleChangedEvent`, `WorksheetCommentCreated/Replied/HiddenEvent` (#105, #326 D4), `DirectMessageSentEvent`, `DirectMessageReportedEvent` (#106 dilim b), `ParentLinkedEvent`, `ParentUnlinkedEvent` (#419; consumer #423), `ParentHomeworkOverdueEvent`, `ParentChildTestCompletedEvent` (#423), `BookingParentApprovalRequestedEvent`, `BookingParentDecisionEvent` (#439), `ParentInvitationRequestedEvent` (#440), `StudentParentGraceReminderEvent` (#441), `BookingLifecycleEvent` (#315), `ActivityCompletedEvent` (#572)) | same as `configure` | nothing (`^$`) |
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
- **Blok türleri (#578, `SchemaRevision` 5).** Claude 1. aşaması sayfadaki bütün blokları **tek çağrıda** türüyle döner:
  `kind` = `mcq_question` | `activity` | `info_box` (anlatım, "Bilgi"/"Hatırlayalım", "Örnek" çözümü) | `answer_key` | `noise`
  (optik form, karekod, başlık/altlık, logo); `activity` için `component` (epic #569 ailesi `A`–`I`) ve `interaction_type`
  (sunucu bileşenden türetir: A → `matrixChoice`, B → `slotFill`, C → `dragPlace`|`match`, D → `textSelect` (#582), diğerleri null). Şık aşaması yalnız
  `mcq_question` için çalışır; diğer bloklar şıksız, yalnız soru payı + sayfa kırpması. Güvenlik ağı (revizyon 3): bileşensiz
  `activity`'ye de şık aşaması sorulur, >= 2 şık bulunursa `mcq_question` olur (şıklarla), aksi halde etkinlik kalır; bileşenli
  etkinlik şık aşamasına girmez. Bilinmeyen tür `noise` sayılır (log). Sınırlar (revizyon 4): `MaxQuestionsPerPage` yalnız şık
  çağrısı alabilecek blokları (`mcq_question` + bileşensiz `activity`) sayar, tüm bloklar en çok `MaxQuestionsPerPage * 3`;
  1. aşama çıktı sınırı 12000 token. Kesik / reddedilmiş / şema dışı ama yanıt almış çağrının token'ı da bütçeye sayılır.
  Alanlar `PredictionDto`'da isteğe bağlı (null = `mcq_question`, YOLO/eski kayıt) ve `PUT pages/manual` ile gidip gelir;
  geçersiz değer 400 `labeling.invalidBlockKind`. Revizyon 2–5 eski Claude sonuçlarının önbellek anahtarını geçersiz kılar (5, #582: D tanımı metindeki `( )` noktalama
  boşluklarını, yazım yanlışı bulmayı ve düzeltmeyi kapsar; B'den ayrımı prompt'ta).
  **Kaydedilmiş özel prompt sürümü yeni varsayılanı ezer** (eski "yalnız MCQ" talimatıyla Claude yalnız `mcq_question` döner):
  yeni talimatı almak için Prompt penceresinde varsayılanı yükleyip kaydedin.
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

**Issue #579 — Claude ile etkinlik yapısı (blok → taslak etkileşim planı).** `POST /api/exam/labeling/activity-structure`
`{imageBase64 (bütün sayfa), model, force, blocks: [{key, x, y, width, height (sayfa pikseli), interactionType}]}` (1–20 blok;
tipler `matrixChoice` | `slotFill` | `dragPlace` | `match` | `textSelect` (#582)). Blok başına **bir** Claude çağrısı; talimat + JSON şeması bloğun
tipinden seçilir, çağrılar paralel (`Anthropic:MaxParallelCrops`). Kırpma = blok + 16 px pay (sayfaya kırpılır, `MaxImageEdge`'e
küçültülür). Yanıt istek sırasıyla blok başına `{key, interactionType, status (ok|error), origin (live|cached|null), plan,
instruction, valid, validationErrors, promptVersion, inputTokens, outputTokens, estimatedCostUsd, error}`: `plan` tipin
InteractionPlan v1'i (id'ler sunucuda: `c1`/`r1`, `s1` (+ seçenek `s1o1`), `t1`/`k1`, `l1`/`r1`, textSelect `t1`/`g1`), kutular **blok kutusuna göre
göreli** sayfa pikseli ve blok kutusuna kırpılmış; çözüm yok (#580). `valid` = kontrolcünün `ValidateDefinition`'ı (taslak geçersiz
olabilir, yine döner; `validationErrors` yerelleştirilmiş ipuçları). Tek bloğun Claude hatası yalnız o bloğu `status: error` yapar.
- **Önbellek** `LabelingActivityResults`: anahtar (kırpma SHA-256, tip, model); kırpma hash'i #577 `QuestionCropHash` tanımı (bankaya
  kayıttaki yolla, aynı blok bankaya kaydedilince `QuestionSources.CropSha256` ile aynı). Satır yalnız **o tipin** prompt parmak izi
  (tip + talimat + `ActivityStructureSettings.SchemaRevision` + tipin şema revizyonu) tutarsa döner (`origin: cached`, ücretsiz;
  token/maliyet ilk çağrınındır). Bir tipin talimatı değişince yalnız o tip yeniden çıkarılır; tip değiştirilen blok yeni tipin
  talimatıyla yeniden çıkarılır (eski tipin kaydı durur). `force` kaydı atlar ve üzerine yazar. Aynı istekte aynı kırpma + tip tek çağrı.
- **Kota:** #578 ile ortak sayaçlar — kayıtta olmayan her blok `LiveCallsPerHour`'dan 1 düşer (istek başında toplu; yetmezse istek
  bütünüyle 429 `LabelingRateLimited` ve reddedilen istek kotayı yemez). Çağrılardan önce en kötü durum maliyeti (blok başına kırpma
  görseli + ~3000 talimat token'ı girdi, 8000 çıktı, modelin fiyatıyla) kullanıcı ve toplam günlük bütçeden **atomik olarak ayrılır**;
  sığmazsa 429 `LabelingBudgetExceeded` (ayrılan geri alınır), çağrılar bitince gerçek maliyete düzeltilir (ödenmiş başarısız çağrı
  dahil). Pahalı modelde (Opus) tek istekteki blok sayısı bütçeyle sınırlanabilir. `ai-detect` bu rezervasyonu kullanmaz (en kötü
  durum sayfa + 40 şık çağrısı fazla karamsar); eski "harcanan ≥ sınır" kontrolüyle kalır. Kayıttan dönenler dahil kullanıcı
  başına dakikada `Anthropic:ActivityStructureRequestsPerMinute` (30) istek, aşınca 429 `LabelingRateLimited`. Canlı çağrı
  gerekiyor ve anahtar yoksa 503 (tamamı kayıtlıysa anahtarsız da döner).
- **İstemci giderse:** kota alındıktan sonra çağrılar istek iptalinden bağımsız koşar (yalnız 3 dk sunucu zaman aşımı ve uygulama
  kapanışı keser; hiç başlamamış çağrının saatlik kotası iade edilir); başarılı sonuçlar yine önbelleğe yazılır, bütçe düzeltilir.
  Tek bloğun önbellek kaydı başarısız olursa loglanır, yanıt yine döner (o blok sonraki istekte yeniden çıkarılır).
- **Tip başına talimat:** `GET /api/exam/labeling/activity-prompts` (5 tip: etkin, varsayılan = sürüm 0, son 20 sürüm),
  `PUT /api/exam/labeling/activity-prompts/{interactionType}` `{instruction}` (≤ 4000, `{width}`/`{height}`) → yeni etkin sürüm
  (`LabelingActivityPromptVersions`, tip başına numara; etkinle aynı içerik yeni sürüm açmaz). Sistem mesajı, koordinat kuralı ve
  şemalar kodda (`ActivityStructureSettings`, `ActivityDrafts`): ortak kısım değişirse `SchemaRevision`'ı, tek tipin şeması/eşlemesi
  değişirse o tipin revizyonunu artır. Yetki işaretlemeyle aynı (Admin + QuestionTrainer, onaylı öğretmen; gateway kuralı kapsıyor).
  **Bilinçli kabul (review L2):** talimatlar #578 sayfa prompt'u gibi **genel**dir — herhangi bir QuestionTrainer bir tipin
  talimatını değiştirince herkesin sonraki çağrıları onu kullanır (sürüm geçmişi kimin kaydettiğini tutar, eski sürüme dönülebilir).
- Migration `AddLabelingActivityStructure` exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).

**Issue #582 — `textSelect` (bileşen D, metin üzerinde işaretleme).** Modlar `select` (altını çiz / yuvarlak / yanlışı bul),
`punctuation` (`( )` boşluklarına palet sembolü), `correct` (yanlışı seç + düzeltmesini yaz; `SlotAnswerNormalizer` metin kuralı).
Plan token listesidir (`{id, text, line, boxes (1–3), gap?}`, en çok 600 token / 100 gap); kontrol bulunan / eksik / fazla sayar,
geri bildirim yalnız seçilen token'ları ve sayıları taşır. Claude (`activity-structure`, tip `textSelect`) yalnız **satır** döner
(metin aynen, hatalar düzeltilmez); token'lar ve kutuları sunucuda `TextSelectTokenizer` ile (satır sonu tiresi birleşir:
`dolaş-` + `mış` → `dolaşmış`, iki kutu). Aynı kural TS'de; ikisi `shared/fixtures/text-tokenize.v1.json`'u koşar — kuralı
değiştirirsen fixture'ı ve `textSelect` tip revizyonunu (`ActivityStructureSettings`) birlikte güncelle. İşaretleme ekranında
bileşen D artık `textSelect` türetir; #582'den önce kaydedilmiş Claude sayfa sonuçlarında D bloklarının tipi null kalır
("Tekrar AI'a sor" ya da elle seçim; sayfa prompt'u revizyon 5 olduğundan eski sonuçlar zaten kayıttan dönmez). Migration yok.

**Issue #470 — işaretleme ekranından soru bankasına kayıt.** Araç çubuğunda **Havuza kaydet** = bankaya kayıt + başarılıysa
**Eğit** ile aynı akış (send-to-fix → manuel kayıt → sonraki sayfa; eğitim başarısızsa sayfa açık kalır, Eğit ile yeniden
denenir; kayıt başarısızsa eğitime gönderilmez). Düz **Eğit** değişmedi (bankaya yazmaz). `POST /api/exam/labeling/pages/bank` sayfadaki MCQ soruları müfettişte
seçilen doğru şıkla, teste bağlamadan (`WorksheetQuestion` yok) mevcut toplu kayıtla (`SaveBulkQuestion`, `TestId=null`)
oluşturur — kırpma + MinIO + `QuestionCreatedEvent` (sınıflandırma otomatik), sahibi çağıran. Uç ek olarak Teacher|Admin
ister (QuestionTrainer tek başına yetmez); kullanıcı başına saatte 30 kayıt (429). Aynı sayfa (`LabelingPages` SHA-256;
`BankSavedAt`/`BankQuestionCount`, süren kayıt `BankSaveStartedAt` — 10 dk sonra bayat; migration `AddLabelingPageBankSave`)
ikinci kez kaydedilmek istenirse 409 `LabelingPageAlreadyInBank` → UI "Yine de kaydet" (force; sorular ikinci kez oluşur;
süren kayıt force ile de aşılmaz). Şık kutuları soru kutusuna kırpılır. Pasaj ve kitap adı (`BookName`) bu ekrandan kaydedilmez (açık soru). **Sınıf zorunlu (#565):** araç çubuğundaki **Sınıf** açılır kutusu (localStorage `labeling.gradeId`) seçilmeden
Havuza kaydet pasiftir; istek `gradeId` taşır (eksik/bilinmeyen → 400) ve `QuestionCreatedEvent.GradeId` olarak sınıflandırıcıya gider.

**Issue #472 — test ekranında "Havuzdan ekle".** Test detayında (`/test/:id`, düzenleyebilen öğretmen/admin) **Havuzdan Ekle**
soru bankası panelini seçim modunda açar; seçilenler `POST /api/exam/questions/test/{testId}/bank-questions` `{questionIds}` (en çok 60)
ile teste **bağlanır** (kopyalanmaz — test kopyalamayla aynı model; soruyu düzenlemek yine yalnız sahibine/admine açık, puan soruda kalır),
testin sonuna istek sırasıyla; testte olanlar atlanır ve panelde pasif görünür. Bağlanabilir soru: kendi sorusu ya da görebildiği teste
bağlı, sahibi damgalı soru (`CanLink`). Başkasının sahipsiz (legacy, `CreateUserId` 0/null) sorusu bağlanamaz — legacy sahiplik "soruyu
içeren kopya olmayan testin sahibi" kuralıyla türetildiği için bağlamak düzenleme yetkisi verirdi (admin başkasının orijinal testine
eklerken de). Bir soru bile bağlanamazsa hiçbir şey yazılmaz (404). Test başına advisory lock; kullanıcı başına dakikada 30 istek
(`RateLimiting:QuestionBankLink`, 429 `questions.bankLink.rateLimited`). Migration yok.

**Issue #473 — soru tuvalinde Claude ile tespit.** Soru tuvalinin (`/questioncanvas`) araç çubuğunda **Kaynak: Manuel | Model | Claude**
(işaretleme ekranındakiyle aynı uçlar: `ai-models`, `pages/{sha256}`, `ai-detect` force=false — kayıtlı sonuç ücretsiz döner). Yalnız
Admin + QuestionTrainer ve `ai-models` boş değilse görünür; diğerlerinde tuval eskisi gibi YOLO ile açılır. Varsayılan Model (YOLO);
seçim localStorage `questionCanvas.source` / `questionCanvas.aiModel` (işaretleme ekranından ayrı). Claude seçiliyken bir sonraki sayfa
arka planda Claude'a sorulur (önceden getirme; otomatik ilerle açıkken yok; tek seferde bir istek). Sayfa değişince açık sayfanın
isteği iptal edilir, önceden getirme edilmez. Claude hatasında sunucu mesajı araç çubuğunda kalır ve sayfa YOLO ile açılır; 403/429/503'te
(bütçe/kota) Claude o oturumda askıya alınır (sayfalar YOLO ile açılır), Claude yeniden seçilince bir kez daha denenir. Manuel kayıt otomatik
uygulanmaz, yalnız seçilince. Yetki / bütçe değişmedi (`Anthropic:LiveCallsPerHour`, `DailyBudgetUsd*`).

**Issue #577 — kitap kaydı.** Admin + QuestionTrainer için **Kitaplar** sayfası (`/books`): kitap, sayfa ofsetleri, cevap
anahtarı aralıkları ve içindekiler (dosya sayfa aralığı → sınıf / ders / konu / alt konu) girilir. Uçlar
`/api/exam/labeling/books` altında (işaretlemeyle aynı yetki, gateway kuralı da): `GET`/`POST`, `GET`/`PUT`/`DELETE /{id}` (PUT kümeleri
bütün olarak değiştirir; kullanımdaki kitap silinmez, 409 `LabelingBookInUse`), `GET /{id}/pages/{filePage}` (bastırılmış sayfa, cevap
anahtarı mı, içindekiler satırı), `GET /{id}/printed/{printedPage}?near=` (dosya sayfaları). Sayfalar **dosya** sayfasıdır (1'den);
**bastırılmış = dosya + ofset** (ofset negatif olabilir); fasiküllü kitapta (TT MatLab) her fasikül ayrı aralık, bastırılmış numara
tekrar edebilir → birden çok dosya sayfası, `near`'a en yakın önce. Aralıklar küme içinde çakışmaz. İşaretleme ekranında kitap + sayfa
seçilince `pages/bank` `bookId`/`filePage` taşır: sınıf (istekte yoksa) ve ders/konu içindekilerden gelir (`ClassificationSource=Book`,
sınıflandırıcı davranışı `QuestionAnalyzer:BookTocMode`, bkz. architecture.md), `BookName` kitap adı olur, soru başına `QuestionSources`
(bastırılmış sayfa, etkinlik/madde etiketi, yüklenen kırpmanın SHA-256'sı) yazılır. Aynı kırpma bankada varsa kayıttan önce 409
`LabelingCropAlreadyInBank` (+ mevcut kayıtlar; başkasının sorusu yalnız `{questionIndex, otherOwner: true}`, Admin hepsini görür) →
UI "Yine de kaydet" (`force`); cevap anahtarı sayfası 409 `LabelingAnswerKeyPage` (force ile kaydedilir); yanıtta `tocApplied`. Silinmiş
taksonomiye işaret eden içindekiler satırı uygulanmaz (`pages/{filePage}` → `toc: null`, `tocStale: true`). Öğretmen içeriğinde
kullanılan kitabın (kitap testi, çalışma öğesi, başka kullanıcının aynı adlı sorusu) adını / asıl kaydını yalnız Admin değiştirir
(403 `LabelingBookRenameForbidden`). Migration `AddBookRegistry` exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).
**Yayın:** exam API ile BadgeService birlikte deploy edilir; eski BadgeService `Book` kaynağını tanımaz ve kitap sorularını AI ile
sınıflandırıp içindekilerin konusunun üzerine yazar. Ayrı yayın gerekiyorsa önce BadgeService'i `QuestionAnalyzer__BookTocMode=Skip`
ile çıkar, exam API'den sonra `Hybrid`'e al.

**Issue #469 — Claude soru sınıflandırıcısı.** BadgeService'te `QuestionAnalyzer:Provider` = `Claude` (varsayılan, boşken de) |
`Gemini`; geçersiz değer açılışta patlar. Claude seçiliyken BadgeService
taksonomiyi exam API `GET /api/questions/classifier-taxonomy`'den (Admin/servis) alır (`Anthropic:TaxonomyCacheSeconds`,
varsayılan 300 sn bellek önbelleği) ve sistem mesajının sonuna prompt-cache breakpoint'iyle koyar (`Anthropic:CacheTtl`
`1h`|`5m`); yanıt JSON şemalı (structured output). Taksonomi dışı id dönerse sonuç yazılmaz, loglanır ve mesaj
`badge-service_error` kuyruğuna düşer (Gemini/exam API reddiyle aynı). Her çağrıda input / cacheWrite / cacheRead /
output token'ı ve tahmini USD loglanır (`Anthropic:InputUsdPerMTok`/`OutputUsdPerMTok`, varsayılan model Haiku 5.5).
- **Anahtar:** Aspire'da işaretleme ekranıyla aynı `anthropic-api-key` parametresi BadgeService'e de `Anthropic__ApiKey`
  olarak gider (AppHost'u yeniden başlat). Anahtar yoksa sınıflandırma uyarıyla atlanır. docker-compose'da tanımlı değil.
- `QuestionAnalyzer:AIActive=false` iken sağlayıcıdan bağımsız hiçbir sınıflandırma yapılmaz.
- **Sınıf bağlamı (#565).** `QuestionCreatedEvent.GradeId` doluysa Claude sınıflandırıcı taksonomiyi
  `GET /api/questions/classifier-taxonomy?gradeId=` ile yalnız o sınıfa bağlı ders/konu/alt konularla alır ve sınıf başına
  önbellekler (prompt daha küçük, başka sınıfın aynı adlı konusu seçilemez; taksonomi dışı id reddi başka sınıfın id'sini de
  reddeder). Sınıfın taksonomisi boşsa sınıflandırma uyarıyla **atlanır** (tüm taksonomiye düşülmez). `GradeId` null ise
  tüm taksonomi. Exam API `GradeId`'yi şöyle çözer: istekte açıkça verilen sınıf (işaretleme ekranı) → teste bağlı kayıtta
  testin `Worksheet.GradeId`'si → null. Gemini sağlayıcısı sınıfı yoksayar. `Question` tablosunda sınıf kolonu yok; sınıf
  konu (`Topic.GradeId`) üzerinden gelir.

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

**Issue #609 — tamamlanmış etkinliği yeniden çöz.** `POST /api/exam/test-instances/{id}/retake` (gövdesiz; yalnız oturumun sahibi,
yalnız `Kind=Activity`, yalnız Completed; start-test ile aynı erişim kararı — atama / sınıf + görünürlük) **aynı oturumu** sıfırlar,
yeni oturum açılmaz (#367 unique index'i değişmedi): cevaplar / kontrol sonuçları silinir, `Status=Started`, `EndTime`/`SolutionsRevealedAt`
boş, `CheckCount=0`, `AttemptStartedAt=now`, `RetakeCount+1`; ilk kontrol özeti (`FirstCheck*`), `StartTime` (atama penceresi),
`ActiveSeconds` (yeniden tamamlamada birikir; deneme penceresi `AttemptStartedAt`'ten) ve `FirstCompletedAt` korunur. Hata: 404 (yok /
başkasının; silinmiş etkinlik `WorksheetNotFound`), 403 `AccessDenied`, 409 `NotAnActivity` (test davranışı aynı) / `ActivityNotCompleted`
(Started ya da Expired), 429 `RateLimited` (`RateLimiting:ActivityRetake`, kullanıcı başına dakikada 5); zaten yeniden çözülüyorsa 200
`alreadyRetaking: true` (hiçbir şey silinmez). Öğretmen/veli takibi, öğrencinin atama durumu (`isCompleted`/`isFinished`) ve takvim yeniden
çözülen oturumu tamamlandı sayar (`WorksheetInstanceStatusRules.TrackingStatus`; DTO'larda `retaking`, öğretmende `retakeCount`).
Bilinçli kabuller: (1) öğrencinin kendi "Tamamlanan" filtresi gerçek durumu kullanır — yeniden çözülen etkinlik "devam eden"de görünür;
(2) veli yeniden çözüm sürerken sonuç göremez (önceki cevaplar silindi; liste sonuç bağlantısı vermez, sonuç ucu 404), yeniden
tamamlanınca **son denemeyi** görür (etkinlik puansız, pratik); (3) yeniden tamamlama aynı `WorksheetInstanceId` ile ikinci
`ActivityCompletedEvent` yazar ve BadgeService (UserId, WorksheetInstanceId) ile yok sayar → rozet sayacı / çalışma süresi şişmez; ilk
tamamlama rozet şartını sağlamadıysa (`CountsTowardBadges=false`) yeniden tamamlama da sayılmaz (aynı tekillik). Migration
`AddTestInstanceRetakeTracking` (`FirstCompletedAt`, `AttemptStartedAt`, `RetakeCount`; mevcut Completed satırları `EndTime` ile doldurur)
exam API açılışında uygulanır (Aspire'da exam API'yi yeniden başlat).

**Issue #583 — `openText` (bileşen H, açık uçlu cevap; AI'sız ilk sürüm).** Plan `{"version":1,"type":"openText","minWords?","maxWords?","lines?"}`
(sözcük 1..2000, min ≤ max, satır 1..30), çözüm `{"sampleAnswer?","rubric?"}` (düz metin ≤ 4000; boş geçerli). Öğrenci payload'u
`{"version":1,"type":"openText","text"}` (≤ 8000 karakter; boş = cevapsız); sözcük sınırı zorlanmaz, yalnız `maxWords × 2`'yi aşan
metin 400. Otomatik kontrol yok: satır 0/0 (doğru/yanlış ve ilk kontrol özetine girmez), geri bildirim
`{"rows":[{"id":"text","status":"pending"|"sufficient"|"partial"|"insufficient"}]}`; açık uçlu cevap `AnswerSubmittedEvent` yazmaz
(puan yok). **Yalnız etkinlikte:** havuzdan ekle, tekli/toplu soru kaydı, test kopyalama 400, Activity → Test tür değişimi 409
`OpenTextNotAllowedInTest`; skor kuralı (`WorksheetScoring`) savunma olarak açık uçlu satırı toplamdan düşer, "yanlışlarım" testine
almaz. Öğrenciye çözümden yalnız `sampleAnswer` gider. Kontrol edilen (ya da oturumu etkin durumda bitmiş) cevap öğretmen kuyruğuna
düşer: `GET /api/exam/open-text-reviews?status=pending|reviewed|all&worksheetId=&page=&pageSize=` (page ≤ 10000, pageSize ≤ 50;
kullanıcı başına dakikada 60, `RateLimiting:OpenTextReviewRead`) ve `PUT /api/exam/open-text-reviews/{instanceQuestionId}`
`{evaluation, feedback?, answerHash}` (dakikada 60, `RateLimiting:OpenTextReview`). Yetki: gateway Teacher + Admin
(`RouteRoleAuthExtensions`), backend onaylı öğretmen + #573 takip kuralı — öğretmen etkinliği **kendi atamasıyla** hedeflediği, okul
kapsamındaki öğrencinin **atama penceresinde sayılan** oturumunu görür; Admin hepsini. Başkasının verdiği değerlendirmeyi yalnız o
kullanıcı ve Admin düzeltir (diğerine 403 `OpenTextReviewForbidden`, satırda `canEdit:false`). **Rubrik yalnız sorunun sahibine ve
Admin'e** döner (diğer değerlendirenlere `rubric: null`); örnek cevap herkese. Öğrenci değerlendirmeyi oturum ve kontrol yanıtlarında
`openTextReview` (`pending` / `reviewed` / `outdated` + değerlendirme, geri bildirim) olarak görür; metni değişince değerlendirme eskir
(`outdated`, gönderilince yeniden kuyrukta), cevap boşaltılınca gösterilmez, yeniden çözme (#609) siler. Düz metin süzgeci
(`PlainTextAttribute`; öğrenci metni, geri bildirim, çözüm, pasaj yönergesi) C0 (\n \r \t hariç), DEL + C1 (U+007F–U+009F) ve bidi
(U+202A–U+202E, U+2066–U+2069) karakterlerini reddeder. İşaretleme: H → `openText` (sayfa prompt'u `SchemaRevision` 6; kayıtlı özel
prompt sürümü yeni varsayılanı ezer). Değerlendirme bildirimi (BadgeService) bu sürümde yok. Migration `AddOpenTextReviews` (tablo +
`IX_Questions_InteractionType_OpenText` kısmi, `IX_WorksheetAssignments_CreateUserId_WorksheetId`) exam API açılışında uygulanır
(Aspire'da exam API'yi yeniden başlat).

**Issue #584 — öğretmen etkinlik analizi ve veli özeti.** Etkinlik sayfası detayında (öğretmen, "Çalışma takibi"nin altında)
**Analizi göster** → `GET /api/exam/worksheet/{id}/activity-report` (onaylı öğretmen; kapsam takip ekranıyla aynı — kendi atamaları,
okul, atama penceresi; `GetWorksheetAssignmentsForTeacherAsync`'ten alınır). Analize yalnız tamamlanmış ve şu an yeniden çözülmeyen
oturumlar girer, cevaplar oturumun **son** hâlidir (ilk kontrol özeti değil) ve sunucuda kontrolcüyle yeniden değerlendirilir. Yanıt:
öğrenci × madde ısı haritası (doğru satır / toplam satır) ve madde başına öğe dağılımı — doğru oranı, boş oranı, en sık 3 yanlış cevap.
Öğe tanımı kontrolcünün `IInteractionGrader.Analyze`'ında: dragPlace = kart (cevap = konduğu hedef; "kemik → kaslar"), çeldirici kart
doğru ⇔ kullanılmamış; matrixChoice = satır → kolon; match = sol → sağ; slotFill = kutu → seçenek / yazılan metin (Türkçe küçük harf,
boşluk birleşik gruplanır); textSelect = hedef (+ seçilen hedef dışı token çeldirici olur), punctuation = gap → sembol;
dragDropLabeling = çözümdeki hedef → kart; MCQ = şık etiketi (`Tag`, yoksa A, B…). openText ve çözümsüz madde puanlanamaz (oran yok).
Analiz öğrenciye gitmez (çözümü ele verir). Takip tablosu (#573) bilerek yüzde göstermez; analiz ayrı ve istenince yüklenir.
Veli özeti (`/api/exam/parent/children/{id}/summary`) `activities` taşır: bu hafta / toplam tamamlanan etkinlik (`FirstCompletedAt`;
yeniden çözme ikinci kez saymaz) ve ilk kontrol doğru oranı ortalaması (`FirstCheck*`, kontrol yoksa null). Migration yok.

**Issue #590 — görselsiz etkinlik maddesi (dilim a).** Etkinlik sayfasında (öğretmen) **Görselsiz madde ekle** → `/activity-editor?testId=&gradeId=`
(`&questionId=` düzenleme; editör o etkinliğin formla düzenlenebilen maddelerini listeler). Türler: matris seçim (D/Y şablonu,
çoklu seçim, `shuffleRows`), sınıflandırma (`dragPlace` place, kutusuz kovalar; kovasız kart çeldirici, kova kapasitesi kart
sayısı — ipucu vermesin), sıralama (`dragPlace` order), eşleştirme (`match`, çeldirici sağ öğeler). Aynı tipler/kontrolcüler;
matris satırında `box` artık isteğe bağlı (kutusuz satırda boş olmayan `label` zorunlu, `cells` yasak). Yeni madde toplu kayıtla
(`questions/save`, `imageData` yok ya da tek etkileşimsiz görsel; kapsam kutusu 0×0 kırpılmaz), düzenleme tekli kayıtla. Yönerge düz
metin (#573). Seçilen konu kilitlenir (bkz. architecture.md); seçilmezse madde metinle sınıflandırılır. Migration yok; Aspire'da exam
API ve BadgeService'i yeniden başlat.

**Issue #590 dilim b — boşluk türleri (görselsiz düzen).** Plana isteğe bağlı `layout` geldi (`InteractionLayout`, TS
`interaction-layout.ts`): `{kind:"list"}` (her öğe etiketiyle satır), `{kind:"text",text}` (satır içi metin, `[[id]]` öğenin yeri),
`{kind:"table",header?,rows}` (hücreler düz metin, `[[id]]` içeren hücre cevap kutusu; satırlar eşit genişlikte, başlıkta yer tutucu
yok). Kayıtta her öğe düzende **tam bir kez** geçer, bilinmeyen id'li yer tutucu yok, düz metin süzgeci (`PlainTextAttribute` kuralı),
metin ≤ 4000, tablo ≤ 30 satır × 8 kolon, hücre ≤ 200. Düzende çözüm yoktur. `slotFill`: `layout` varsa kutularda `box` **yasak**
(yoksa eskisi gibi zorunlu), `list`'te her kutunun boş olmayan `label`'ı (soru) zorunlu. `dragPlace`: yalnız `text` düzeni, yalnız
`place`, hedefler kutusuz ve kapasite 1, kartlar kutusuz (boşluklu metin + kelime havuzu); kayıttaki kart karıştırma hedef id'lerine
dokunmaz, metin geçerli kalır. Editörde 4 yeni tür: **Boşluk doldurma** (metinde `[[cevap|alternatif]]`; "Seçili metni boşluk yap"),
**Soru + kısa cevap** (liste), **Tablo** (hücrede `[[cevap]]`), **Kelime havuzu** (`[[kelime]]` + çeldiriciler; aynı kelime birden
çok boşlukta tek kart + `reuse`). Tek cevap birimsiz sayıysa (`parseTrNumber`) sayı kutusu (kesir / tam sayılı kesir, eşdeğerlik açık),
değilse metin kutusu (#575 normalleştirmesi). Seçim/palet kutusu, birim ve sonek editörde yok (tuvalde var); öyle bir plan editörde
açılmaz. Oynatıcılar düzeni HTML olarak çizer (görsel varsa etkileşimsiz üstte kalır). Sınıflandırma metnine düzen metni yer
tutucular `____` olarak girer. Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #585 — `mazeTree` (bileşen G, yol oyunları).** Tek motor, dört varyant: `tree` (D/Y dallanan ağaç), `rooms` (odalı
labirent, kapılar iki yönlü), `board` (ışıklı pano, satır başına bir doğru kutu, alttan üste), `stones` (ırmak taşları, basılmaması
gerekenleri işaretle; yolsuz). Plan `{"version":1,"type":"mazeTree","variant","start?","nodes":[{id,label?,end?,row?,col?}],"edges":[{id,from,to,label?}]}`;
düğüm = ifade / oda / pano satırı / taş, kenar = seçenek (dal, kapı, kutu). Çözüm yol varyantlarında `{"choices":{"düğüm":"kenar"}}`
(rota = başlangıçtan seçimleri izleyip bir `end` düğümüne döngüsüz varan yol; ağaçta rota dışı ifadelerin doğrusu da saklanır),
taşlarda `{"marked":[...]}`. Öğrenci payload'u `{"path":[kenarlar]}` (kesintisiz, bitişten sonra adım yok, ≤ 128) ya da `{"marked":[...]}`.
Kayıt kuralları (`MazeTreeGrader.ValidateDefinition`): ağaç gerçek ağaç (2–4 dal, derinlik ≤ 5), pano zinciri (2–6 kutu, ≤ 12 satır,
satır adı isteğe bağlı, bitişten kenar çıkmaz), odalar tekil `row`/`col` (0–7), yalnız komşu (çapraz dahil) odalar arasında tek kapı ve bir
dörtlüde tek çapraz kapı, taşlarda en az bir işaretli taş, her düğüm başlangıçtan
erişilebilir, bir düğümün seçenek etiketleri tekil. Kontrol **ilk sapmada durur**: satırlar öğrencinin geçtiği karar düğümleri, toplam =
rota uzunluğu, geri bildirim `{"rows","status":"reached|deviated|incomplete","at"}` (rota ve doğru dal yazılmaz); taşlarda da satırlar
ilk yanlış taşta durur (taş ikili: hepsi çözümü verirdi), boş cevap hepsi yanlış. Oynatıcı yolu 128 adımda keser. Analiz (#584): öğe = rotadaki karar düğümü (cevap = seçilen dal; sapmadan sonrası cevapsız), taşlarda taş.
Yazarlık görselsiz editörde (`/activity-editor`) dört şablonla: tam ikili D/Y ağacı (1–4 düzey, ifadeler genişlik öncelikli), pano
satırları, R×C oda ızgarası (2–4; etiketsiz kapı = duvar, bir dörtlüde tek çapraz kapı), taşlar. Oynatıcı oyunu HTML olarak çizer (görsel
varsa etkileşimsiz üstte); Claude yapı çıkarımı yok (G bloğu tipsiz kalır). Testte de çözülebilir (puanlanır). Migration yok; Aspire'da
exam API'yi yeniden başlat (yeni kontrolcü).

**Issue #586 dilim a — `crossword` (bileşen G, çengel bulmaca).** Plan `{"version":1,"type":"crossword","rows","cols","clues":[{id,dir:"across"|"down",row,col,length,number?,text}]}`
(ızgara 2–20, 1–80 ipucu, uzunluk ≥ 2, ipucu metni zorunlu düz metin ≤ 300); beyaz hücreler ipuçlarının birleşimi, harfler planda yok.
Çözüm `{"answers":{"k1":"KALP"}}` (kayıtta tr-TR büyük harfe çevrilir; her ipucunun cevabı, uzunluk tutar, yalnız harf/rakam, kesişen
hücrede aynı harf). Kayıt kuralları (`CrosswordGrader.ValidateDefinition`): aynı yönde iki ipucu hücre paylaşmaz ve uç uca değmez, aynı
başlangıçtaki ipuçlarının numarası aynı. Öğrenci payload'u `{"grid":["KA  ",...]}` (satır başına `cols` karakter, boş hücre boşluk, siyah
hücrede harf yok; harfsiz ızgara = cevap yok). Kontrol ipucu başına (tr-TR büyük harfle karşılaştırma), geri bildirim `{"rows":[{id,correct}]}`
(harf / yanlış hücre yazılmaz). Analiz (#584): öğe = ipucu, cevap = yazılan kelime (`·` boş hücre). Yazarlık görselsiz editörde
(`/activity-editor`, tür **Çengel bulmaca**): kelime + ipucu, yön ve başlangıç hücresi; **Otomatik yerleştir** kelimeleri kesiştirir (40
tohumlu deneme, kesişemeyen kelime ayrı satıra) ve ızgarayı kırpar. Oynatıcı ızgarayı HTML olarak çizer (yazınca ilerler, Backspace, ok
tuşları, boşluk / aynı hücreye ikinci tık yön değiştirir; görsel varsa etkileşimsiz üstte). Testte de çözülebilir (puanlanır). Claude ızgara
çıkarımı dilim e'de (aşağıda). Migration yok; Aspire'da exam API'yi yeniden başlat
(yeni kontrolcü).

**Issue #586 dilim b — `wordSearch` (bileşen G, kelime avı).** Plan `{"version":1,"type":"wordSearch","rows","cols","letters":["KALPX",...],"words":[{id,label}]}`
(ızgara 4–20, her hücre harf/rakam, 1–40 kelime, `label` listede görünen metin — kelime ya da ipucu, düz metin ≤ 100). Harf ızgarası
öğrenciye gider; çözüm `{"lines":{"w1":{"from":[r,c],"to":[r,c]}}}` (düz çizgi: yatay, dikey, 45° çapraz; kelimeler tekil çizgi).
Öğrenci payload'u `{"lines":[{from,to}]}` (kelimeye bağlı değil, en çok `max(10, 2 × kelime)` çizgi; çizgisiz = cevap yok). Kontrol
**harflere** bakar: kelime, öğrencinin bir çizgisinden iki yönden biriyle okunuyorsa bulundu (ızgarada başka yerde geçmesi de sayılır,
tr-TR büyük harf). Geri bildirim `{"rows":[{id,correct}],"lines":[bool]}` (çizgi başına kelime mi; çözüm çizgisi yazılmaz). Analiz: öğe =
kelime, cevap = bulundu. Yazarlık görselsiz editörde (tür **Kelime avı**): kelimeler + isteğe bağlı ipucu, boyut, çapraz / ters
seçenekleri; ızgara bunların ve bir tohumun saf fonksiyonu (**Karıştır** tohumu değiştirir, boş hücreler Türk alfabesinden), kayıtlı
madde içerik değişmedikçe kayıtlı ızgarayla açılır. Oynatıcıda sürükleyerek ya da iki dokunuşla (ilk harf → son harf) seçim. Migration
yok; Aspire'da exam API'yi yeniden başlat.

**Issue #586 dilim c — `logicGrid` (bileşen G, sözel mantık tablosu).** Plan
`{"version":1,"type":"logicGrid","subject":{label,items:[{id,label}]},"categories":[{id,label,items:[{id,label}]}],"clues":[{id,text}]}`
(2–8 konu öğesi = satırlar, 1–4 kategori, her kategoride konu öğesi kadar seçenek, plan içinde tüm id'ler tekil, etiket ≤ 60, en çok 20
ipucu ≤ 300). Çözüm `{"assign":{"p1":{"c1":"o2"}}}` (her satıra her kategoriden bir seçenek, kategoride birebir). Öğrenci payload'u
`{"cells":[{row,col,mark:"yes"|"no"}]}` (✗ öğrencinin notu, kontrolde yalnız ✓ sayılır; işaretsiz = cevap yok). Kontrol (satır, kategori)
çifti başına (`p1:c1`): o kategoride tek ✓ ve doğru seçenek. Geri bildirim `{"rows":[{id,correct}]}`. Analiz: öğe = çift, cevap = ✓ konan
seçenek. Oynatıcı: hücre döngüsü boş → ✓ → ✗; "✓ koyunca satırı ve sütunu ✗ ile doldur" (varsayılan açık); satır/sütun tekliği anında
denetlenir (iki ✓ ya da hepsi ✗ → kırmızı çerçeve). Yazarlık görselsiz editörde (tür **Mantık tablosu**). Migration yok; Aspire'da exam
API'yi yeniden başlat.

**Issue #586 dilim d — şifre katmanı (`slotFill` + `cipher`).** Yeni tip yok: `slotFill` planına isteğe bağlı
`cipher:{cells:["s2",null,"s1",...]}` (gizli kelimenin hücreleri: kutu id'si ya da `null` = boşluk; 2–40 hücre, en az 2 harf hücresi,
başta / sonda boşluk yok, aynı kutu birden çok hücrede geçebilir; görselli ve görselsiz planda). Harfler **çözümde**: şifrede geçen her
kutunun çözüm girdisinde `letter` (tek harf / rakam) zorunlu, şifresiz planda ya da şifre dışı kutuda yasak (`SlotFillGrader.ValidateDefinition`).
Kontrol değişmedi (kutu başına); geri bildirim ek olarak `cipher` dizisi taşır — hücre başına **doğru cevaplanmış** kutunun harfi (tr-TR büyük
harf), diğerleri `null`; yani yalnız öğrencinin bulduğu harfler açılır. Oynatıcı kutuların altında şifre şeridi çizer (hücre altında sorunun
numarası; örnek soruda istemci işareti, çözüm açıkken bütün kelime). Yazarlık görselsiz editörde **Soru + kısa cevap** formunda: satır başına
harf + gizli kelime; kelimenin her harfi o harfi taşıyan soruya bağlanır (aynı harf birden çok soruda varsa sırayla). Tuvalde (görselli) şifre
yazarlığı yok. Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #586 dilim e — Claude ile bulmaca ızgarası çıkarma.** `activity-structure` (#579) yeni tip `crossword` taşır (talimat
`Prompt` penceresinde **Çengel bulmaca** sekmesinde, tip revizyonu 1). Claude **hücre** uzayında döner (piksel değil): ızgara
`rows`/`cols` ve kelime yerleri `{dir, row, col, length, number, clue}`; cevap / harf istenmez (#580). Sunucu (`ActivityDrafts.Map(CrosswordDraft)`)
20×20'ye sığmayan, yönü bilinmeyen, < 2 hücrelik ve aynı yön + başlangıçta yinelenen kelimeyi atar; ızgarayı Claude'un boyutu ile
kelimelerin bittiği hücrenin büyüğüne (2..20) getirir, aynı hücreden başlayanlara ilk numarayı verir, boş ipucunu `""` bırakır
(taslak geçersiz: `labeling.activity.invalid.clueText` / `noClues`). Kullanım görselsiz editörde (`/activity-editor`, tür
**Çengel bulmaca**): görsel eklenince **Görselden çıkar (Claude)** bütün görseli tek blok olarak gönderir, form ızgara + kelime yerleri
+ ipuçlarıyla dolar (cevaplar boş, her kelimenin uzunluğu cevap yazılana dek tutulur; farklı uzunlukta cevap uyarı gösterir), yönerge
boşsa basılı yönergeyle dolar; formda yazılı içerik varsa önce onay. Düğme yalnız Admin + QuestionTrainer'da ve `ai-models` boş
değilse görünür (uçların yetkisi, kota ve bütçe #579 ile aynı; kayıtlı sonuç aynı görsel + model için ücretsiz). Düzenlemede kayıtlı
görsel istemcide olmadığından yalnız yeni seçilen görsel gönderilir. Sayfa işaretlemede G bloğu tipsiz kalır (blok tipi değil).
Migration yok; Aspire'da exam API'yi yeniden başlat.

**Issue #587 dilim a — `regionPaint` (bileşen E, boyama; ızgara modu).** Plan
`{"version":1,"type":"regionPaint","mode":"grid","rows","cols","palette":[{id,color,label?}]}` (ızgara 2–20 × 2–20, 1–8 renk; `color`
sabit adlardan: red, orange, yellow, green, blue, purple, pink, brown, black, gray — istemcide `styles.scss` `--paint-<ad>` token'ı, serbest
CSS rengi alınmaz; renkler ve id'ler tekil, `label` isteğe bağlı düz metin ≤ 40). Çözüm `{"counts":{"p1":40,"p2":35}}` (her palet rengine
0..hücre sayısı; 0 = çeldirici renk, kullanılmamalı; toplam ≤ hücre, en az biri > 0); hücrelerin **yeri serbest**. Öğrenci payload'u
`{"grid":["..1..",...]}` (satır başına `cols` karakter, `.` boş, `1`..`8` palet sırası; boyasız = cevap yok). Kontrol renk başına sayı
(`RegionPaintGrader`), geri bildirim `{"rows":[{id,correct}]}` (hedef sayı yazılmaz); analiz: öğe = renk, cevap = boyanan kare sayısı.
Oynatıcı: palet + silgi, dokun / sürükle boyar, aynı renkli kareden başlayan sürükleme siler, renk başına sayaç canlı (hedef gösterilmez);
çözüm açıkken hedef sayılar satır öncelikli örnek dolguyla. Yazarlık görselsiz editörde (tür **Boyama (ızgara)**): boyut + renkler, hedef
kitaptaki gibi kesir (`2/5`), ondalık (`0,35`), yüzde (`%25`) ya da kare sayısı; tam kareye inmeyen hedef reddedilir (kayda yalnız sayı
gider). Testte de çözülebilir (puanlanır). Sonraki dilimler: b) görsel üzerinde bölgeler, c) MCQ → resim boyama. Migration yok; Aspire'da
exam API'yi yeniden başlat (yeni kontrolcü).

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
