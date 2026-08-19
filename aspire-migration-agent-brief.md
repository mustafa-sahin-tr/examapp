# Aspire Migration Brief — Agent Instructions

## 0. Context and scope

You are adding **Aspire** as a local development orchestration layer to an existing education platform. The platform already runs in production. Your changes must be **additive and reversible**.

### Hard constraints

1. **Do not run `aspire publish` or `aspire deploy`.** Do not generate Compose or Kubernetes artifacts. The existing deployment pipeline stays untouched. Aspire is a development-time tool in this repository.
2. **Do not modify existing production configuration files** (`appsettings.Production.json`, existing CI/CD YAML, existing Dockerfiles used by the release pipeline). Add new `appsettings.Development.json` values or new files instead.
3. **Every service must still be runnable standalone**, without the AppHost, exactly as it is today. If a change breaks `dotnet run` on a single project in isolation, revert it.
4. **Work in phases. Stop at each gate and report.** Do not start Phase N+1 before the human confirms Phase N.
5. Verify current Aspire API surface against the official docs (https://aspire.dev) before writing AppHost code. The API has changed across versions; do not rely on memory of pre-13.x samples. Report the version you targeted.

### System inventory

**.NET services (4):**
| Service | Type | Notes |
|---|---|---|
| `OcelotGateway` | ASP.NET Core, Ocelot reverse proxy | Public entry point; routes to the other services |
| `ExamDotnetApi` | ASP.NET Core Web API | Primary domain API |
| `BadgeService` | ASP.NET Core Web API | |
| `OutboxPublisher` | Worker / background service | Likely no HTTP endpoint — confirm before modelling |

**Non-.NET applications:**
- 2 × Angular frontends (identify their folder names and npm scripts from the repo)
- 1 × Python backend service (identify entrypoint and dependency manager: `requirements.txt`, `uv`, or `poetry`)

**Infrastructure (Aspire-managed containers):**
- PostgreSQL
- Redis
- RabbitMQ
- MinIO (S3-compatible object storage)

**Peripheral containers (used, not developed):**
- Keycloak (identity, users and roles)
- n8n (workflow automation)

---

## 1. Discovery — do this before writing any code

Produce a written report covering:

1. **Solution layout.** Path of every `.csproj`, target framework, and which are runnable entrypoints vs libraries.
2. **Current configuration inventory.** For each .NET service, list every place a connection string or downstream service address is read from: `appsettings*.json`, environment variables, `.env` files, user secrets, existing `docker-compose*.yml`. Record the exact configuration keys. This inventory is the input to Phase 2 and 3 — you cannot wire references correctly without it.
3. **Ocelot routing table.** Extract `ocelot.json` (or the split route files). List every `DownstreamHostAndPorts` entry, which upstream path it serves, and which service it targets. Note whether any service-discovery provider is already configured.
4. **Existing local dev workflow.** Is there a `docker-compose.yml` for local development today? If yes, capture image tags, ports, volumes, and environment variables for Postgres, Redis, RabbitMQ, MinIO, Keycloak, and n8n. **These are your source of truth for the Aspire container definitions** — match image tags and volume paths exactly so no data or realm config is lost.
5. **Keycloak setup.** Realm name, client IDs, and how each .NET service currently obtains the issuer/authority URL. Note whether realms are provisioned by import file or manually.
6. **Auth flow shape.** Which services validate JWTs, which use introspection, and whether the Angular apps use the Keycloak JS adapter or a BFF pattern.

Do not proceed until this report is reviewed.

---

## 2. Phase 1 — Scaffold + infrastructure + one service

**Goal:** prove the loop works end to end on the smallest possible surface.

### Steps

1. Create two new projects at the solution root: an AppHost project and a `ServiceDefaults` shared project. Use the official Aspire templates rather than hand-rolling them. Add both to the existing solution file.
2. In the AppHost, define the four infrastructure resources. Add a persistent data volume to each so state survives restarts, and enable the management/console UIs where the integration offers them:
   - PostgreSQL — with a data volume; add pgAdmin or the equivalent management tool
   - Redis — with a data volume; add Redis Insight or the equivalent
   - RabbitMQ — with the management plugin enabled
   - MinIO — check whether a first-party or Community Toolkit hosting integration exists for MinIO in the current Aspire version. If one exists, use it. If not, model it as a plain container resource with the `minio/minio` image, a data volume, and both the API and console endpoints exposed. Report which route you took.
3. Declare the application databases on the Postgres resource, one per service that owns one, using the names found in the discovery report.
4. Add `ServiceDefaults` as a project reference to **`ExamDotnetApi` only**, and call the service-defaults registration in its startup. Do not touch the other three services yet.
5. Add `ExamDotnetApi` to the AppHost as a project resource and wire references to Postgres, Redis, RabbitMQ, and MinIO.
6. Update `ExamDotnetApi` to consume the injected connection strings via the corresponding Aspire client integrations, keeping the configuration key names identical to what the discovery report found so standalone runs still work.

### Gate 1 — verify before reporting

- `ExamDotnetApi` starts from the AppHost and connects to all four backing services.
- The Aspire dashboard shows structured logs, metrics, and at least one distributed trace spanning an HTTP request into a database call.
- `dotnet run` on `ExamDotnetApi` **alone** still works against locally running infrastructure. This is the reversibility check — do not skip it.
- MinIO console and RabbitMQ management UI are reachable from the dashboard's resource links.

---

## 3. Phase 2 — Remaining .NET services and the gateway

**Goal:** all four .NET services under the AppHost. This phase contains the hardest problem in the migration.

### Steps

1. Add `ServiceDefaults` to `BadgeService` and `OutboxPublisher`. Add them to the AppHost with the appropriate infrastructure references. For `OutboxPublisher`, first confirm whether it exposes an HTTP endpoint; if it is a pure worker, model it accordingly and expect no external endpoint.
2. Add `OcelotGateway` to the AppHost with references to `ExamDotnetApi` and `BadgeService`, and mark it as the externally reachable entry point.

### The Ocelot problem — read carefully

Aspire injects downstream addresses as environment variables in a structured naming convention. Ocelot reads `DownstreamHostAndPorts` from `ocelot.json` as static host/port pairs. **These two mechanisms do not meet by default.** Aspire will assign dynamic ports to the backend services while Ocelot keeps dialling hardcoded ones.

Evaluate these options in order and implement exactly one:

- **(a) Templated configuration.** Rewrite `ocelot.json` so downstream hosts and ports are read from configuration placeholders that the AppHost populates via environment variables. Lowest-risk option; keeps Ocelot.
- **(b) Ocelot service discovery provider.** Configure Ocelot's service-discovery integration to resolve backends from the Aspire-injected values. Verify this works against the current Ocelot version before committing to it.
- **(c) Replace Ocelot with YARP.** YARP has first-class service-discovery support and integrates cleanly with Aspire. **Do not do this in this phase.** If discovery shows Ocelot is a poor fit, write up the case and hand it to the human as a separate decision. Mixing a gateway replacement into an orchestration migration makes both harder to debug.

Default to (a) unless there is a concrete reason not to.

### Gate 2

- A request through the gateway reaches both downstream APIs successfully.
- A distributed trace in the dashboard spans gateway → downstream API → database as a single correlated trace.
- Restarting the solution assigns new backend ports and routing **still works** — this is what proves the wiring is dynamic rather than accidentally hardcoded.
- All four services still run standalone.

---

## 4. Phase 3 — Keycloak

**Goal:** identity under the AppHost without breaking token validation. Isolate this phase; do not combine it with anything else.

### Steps

1. Add Keycloak as an AppHost resource. Prefer the dedicated Keycloak hosting integration if present in the current version. Pin the image tag to whatever is in use today. Attach a data volume, and if realms are provisioned from an import file, wire that import in so the realm is reproducible from a clean start.
2. Reference Keycloak from every service that validates tokens.

### The issuer URL trap — expect this to be the failure

The browser reaches Keycloak on a `localhost` address. Backend services on the container network reach it on an internal hostname. If the issuer in the token does not match the issuer the API expects, **token validation fails with an error that looks unrelated to networking**. This is the single most likely thing to break in this migration.

Set the Keycloak hostname configuration explicitly and deliberately, and make backend token-validation configuration agree with the issuer that will actually appear in tokens minted for browser clients. Do not resolve this by disabling issuer validation — that hides the problem and creates a habit that leaks into other environments. Document the final URL topology in a comment in the AppHost.

### Gate 3

- Full login flow works: browser → Keycloak → token → gateway → downstream API, with the API accepting the token.
- Roles and claims resolve correctly on a protected endpoint.
- After deleting the Keycloak volume and restarting, the realm is restored automatically (if import is configured) or the manual recovery steps are documented.

---

## 5. Phase 4 — Angular, Python, n8n

**Goal:** one-command full environment. These are conveniences; none of them may block the phases above.

### Angular (2 apps)

Add each as an npm application resource pointing at its folder and dev script, referencing the gateway.

**Known limitation:** environment variables injected by Aspire reach the Node dev server process, **not the browser bundle**. Angular code cannot read them directly. Resolve the API base URL through either a dev-server proxy configuration or a build-time environment file generated from the injected values. Pick one, apply it consistently to both apps, and document it.

### Python service

Add it as a Python application resource matching the dependency manager found in discovery. Reference the database and message broker. Expose its HTTP port through the port environment variable Aspire provides rather than hardcoding it.

### n8n

Model as a plain container resource using the current image tag. Mount a persistent volume for its data directory so existing workflows and credentials survive restarts — **verify the volume path against the running production/dev instance before first start**, because a wrong path silently creates an empty instance and can look like data loss.

### Gate 4

- A single AppHost run brings up: 4 .NET services, 2 Angular apps, 1 Python service, Postgres, Redis, RabbitMQ, MinIO, Keycloak, n8n.
- Both Angular apps reach the gateway and complete a login.
- n8n workflows are intact after restart.

---

## 6. Deliverables

1. The AppHost and `ServiceDefaults` projects, committed in **one branch per phase** so any phase can be reverted independently.
2. `docs/local-development.md` — how to start the environment, prerequisites (container runtime, .NET SDK, Node, Python), how to reach each UI, and how to run a single service standalone.
3. A decision log recording: the Ocelot approach chosen and why, the MinIO integration route taken, the Keycloak URL topology, and the Angular configuration strategy.
4. A list of anything you could not resolve, stated plainly rather than worked around.

## 7. Anti-patterns — do not do these

- Do not disable JWT issuer or audience validation to make auth work.
- Do not hardcode ports in AppHost references to make Ocelot cooperate; that defeats the purpose of the migration.
- Do not delete or rewrite the existing `docker-compose.yml` if one exists. Leave it in place until the human decides.
- Do not move secrets into the AppHost in plain text. Use parameters and user secrets.
- Do not add `ServiceDefaults` to a service and simultaneously refactor its logging, telemetry, or resilience code by hand — the whole point is that the shared project owns those concerns.
- Do not proceed past a failing gate by disabling the check.
