# Service-to-service auth hardening

Tracks the cleanup of the "`exam-admin` god client" pattern flagged in the architecture review.

## Done (code + realm export)

- **`ServicePrincipal.IsService(...)`** (`api/ExamApp.Api/Helpers/`) is the single decision point for "is this caller a trusted service". The scattered `preferred_username == "exam-admin"` string checks in `Program.cs` and `BaseController` now call it.
  It accepts, in order: realm role `exam-service` → `azp`/`client_id` in `Keycloak:ServiceClients` (default `["exam-admin"]`) → legacy `preferred_username` match.
- **`exam-service` realm role** added to `deploy/keycloak/import/realm-export.json` and assigned to `service-account-exam-admin`. New environments get it on realm import; existing ones need it added manually (see below).
- **Audience validation** in `api/ExamApp.Api/Program.cs` is now `ValidateAudience = true` with `ValidAudiences` read from `Keycloak:ValidAudiences` (defaults to `["account"]`, so no behavior change yet).

## Apply to a running environment (existing Keycloak volume)

Realm import does not re-run over an existing realm. In Keycloak admin console (`http://localhost:8082/admin/`), exam-realm:

1. **Realm roles** → create `exam-service`.
2. **Clients → exam-admin → Service account roles** → assign realm role `exam-service`.

Until this is done, `ServicePrincipal.IsService` still passes via the `azp` check, so nothing breaks.

## Remaining (not done — needs deliberate realm work + testing)

1. **API-specific audience.** Add an audience client scope (hardcoded `aud: exam-api`) to `exam-client` and `exam-admin`, then set `Keycloak:ValidAudiences = ["exam-api"]` in each API. Do the same for BadgeService (`Services/BadgeService/Program.cs` still has `options.Audience = "account"` and no realm-role claims transformer).
2. **Narrow the service account.** `service-account-exam-admin` currently holds `manage-realm`. `KeycloakService` only lists realm roles, creates users, and assigns roles — `view-realm` + `manage-users` (+ `query-users`) should be enough. Trim after verifying every admin call path.
3. **Drop the legacy fallbacks** in `ServicePrincipal` once (1)–(2) above are in place and verified.
