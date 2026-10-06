# Service-to-service auth hardening

Tracks the cleanup of the "`exam-admin` god client" pattern flagged in the architecture review.

## Done (code + realm export)

- **`ServicePrincipal.IsService(...)`** now lives in `ExamApp.Foundation/Security/` and is shared by the exam API and BadgeService. It is the single decision point for "is this caller a trusted service", replacing the scattered `preferred_username == "exam-admin"` string checks.
  It accepts, in order: realm role `exam-service` → `azp`/`client_id` in the explicit `Keycloak:ServiceClients` list. **Issue #372:** the implicit `exam-admin` default and the legacy `preferred_username` match were removed.
- **Issue #372 — dedicated `exam-service` client.** Service-to-service tokens (BadgeService and exam API `ServiceTokenProvider`) are obtained with the `exam-service` client (`Keycloak:ServiceClientId`/`ServiceClientSecret`), whose service account holds only the `exam-service` realm role. `exam-admin` keeps only `view-realm`/`manage-users`/`view-users`/`query-users` (Keycloak admin REST for auth-api/`KeycloakService`); `manage-realm` and the `exam-service` role were removed from it. Steps for existing environments: `.claude/rules/local-dev.md` (#372 section) and `deploy/README.md`.
- **`exam-service` realm role** is defined in `deploy/keycloak/dev-import/realm-export.json` (the dev import; the prod import path `deploy/keycloak/import/` stays empty) and, since #372, assigned only to `service-account-exam-service` (the `exam-service` client). BadgeService maps realm roles via its own `KeycloakRoleTransformer`, so the role check works there too.
- **Audience validation** in both `api/ExamApp.Api` and `Services/BadgeService` is now `ValidateAudience = true` with `ValidAudiences` from `Keycloak:ValidAudiences` (defaults to `["account"]`, so no behavior change yet).
- **`BadgeService` `ResetController`** (`DELETE /api/reset/users/{userId}` — wipes any user's badge/activity data) was gated only by `[Authorize]`, i.e. any authenticated realm user could reset any other user (IDOR). Now `[Authorize(Policy = "Service")]` — only the exam API's student-reset job (a client-credentials call) can reach it.

## Apply to a running environment (existing Keycloak volume)

Realm import does not re-run over an existing realm. Follow the **Issue #372** section in `.claude/rules/local-dev.md` (local) and `deploy/README.md` (prod): create the `exam-service` client + realm role assignment first, restart exam API and BadgeService, and only then remove `manage-realm` and the `exam-service` role from `service-account-exam-admin`.

## Remaining (not done — needs deliberate realm work + testing)

1. **API-specific audience.** Add an audience client scope (hardcoded `aud: exam-api`) to `exam-client` and `exam-admin`, then set `Keycloak:ValidAudiences = ["exam-api"]` in the exam API and BadgeService. Both already read that config key — this is a realm change plus a per-service config value.
2. ~~**Narrow the service account.**~~ Done in #372 (`manage-realm` removed from `service-account-exam-admin`).
3. ~~**Realm-role claims transformer for BadgeService.**~~ BadgeService has `KeycloakRoleTransformer` registered, so the role check fires there.
4. ~~**Drop the legacy fallbacks** in `ServicePrincipal`~~ Done in #372.
5. **`exam-admin` can still grant privileged roles.** `exam-admin` keeps `manage-users`; without Keycloak fine-grained admin permissions (role-mapping policy) that lets a holder of its secret map `exam-service`/`Admin` to any user. Proper fix: fine-grained admin permissions / a role-mapping policy restricting which realm roles `exam-admin` may assign. Until then monitor with the #267 audit CLI (`audit-privileged-users`, lists everyone holding `Admin`/`exam-service`; after #372 the only expected service account is `service-account-exam-service`).
6. **`exam-service` token scope.** The `exam-service` client keeps `fullScopeAllowed=true`: with `false` and a scope mapping limited to the `exam-service` role, the account client roles (via `default-roles-exam-realm`) would not appear in the token, so the `aud=account` claim that every API still validates would disappear. The service account only holds `default-roles-exam-realm` + `exam-service`, so full scope adds nothing sensitive. Revisit together with service-specific `aud` mappers (item 1).
