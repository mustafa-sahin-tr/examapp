# Service-to-service auth hardening

Tracks the cleanup of the "`exam-admin` god client" pattern flagged in the architecture review.

## Done (code + realm export)

- **`ServicePrincipal.IsService(...)`** now lives in `ExamApp.Foundation/Security/` and is shared by the exam API and BadgeService. It is the single decision point for "is this caller a trusted service", replacing the scattered `preferred_username == "exam-admin"` string checks.
  It accepts, in order: realm role `exam-service` → `azp`/`client_id` in `Keycloak:ServiceClients` (default `["exam-admin"]`) → legacy `preferred_username` match.
- **`exam-service` realm role** added to `deploy/keycloak/import/realm-export.json` and assigned to `service-account-exam-admin`. New environments get it on realm import; existing ones need it added manually (see below).
- **Audience validation** in both `api/ExamApp.Api` and `Services/BadgeService` is now `ValidateAudience = true` with `ValidAudiences` from `Keycloak:ValidAudiences` (defaults to `["account"]`, so no behavior change yet).
- **`BadgeService` `ResetController`** (`DELETE /api/reset/users/{userId}` — wipes any user's badge/activity data) was gated only by `[Authorize]`, i.e. any authenticated realm user could reset any other user (IDOR). Now `[Authorize(Policy = "Service")]` — only the exam API's student-reset job (a client-credentials call) can reach it.

## Apply to a running environment (existing Keycloak volume)

Realm import does not re-run over an existing realm. In Keycloak admin console (`http://localhost:8082/admin/`), exam-realm:

1. **Realm roles** → create `exam-service`.
2. **Clients → exam-admin → Service account roles** → assign realm role `exam-service`.

Until this is done, `ServicePrincipal.IsService` still passes via the `azp` check, so nothing breaks.

## Remaining (not done — needs deliberate realm work + testing)

1. **API-specific audience.** Add an audience client scope (hardcoded `aud: exam-api`) to `exam-client` and `exam-admin`, then set `Keycloak:ValidAudiences = ["exam-api"]` in the exam API and BadgeService. Both already read that config key — this is a realm change plus a per-service config value.
2. **Narrow the service account.** `service-account-exam-admin` currently holds `manage-realm`. `KeycloakService` only lists realm roles, creates users, and assigns roles — `view-realm` + `manage-users` (+ `query-users`) should be enough. Trim after verifying every admin call path.
3. **Realm-role claims transformer for BadgeService.** BadgeService has no `IClaimsTransformation` mapping `realm_access.roles` → `ClaimTypes.Role`, so `ServicePrincipal`'s role check (step 1) can't fire there yet — it currently passes via the `azp` check. Add the transformer (copy `KeycloakRoleTransformer`) when BadgeService gains a role-gated endpoint.
4. **Drop the legacy fallbacks** in `ServicePrincipal` once (1)–(3) above are in place and verified.
