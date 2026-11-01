# Veli hesapları — çocuk hesabı, davet, aktivasyon, grace süresi

Eskiden `.claude/rules/local-dev.md` içindeydi; 2026-11-01'de taşındı.

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
