# Local development with Aspire

An Aspire AppHost now orchestrates local development for this repository, replacing manual `docker-compose up` for day-to-day work. `docker-compose.yml`/`docker-compose.override.yml` are left untouched and still work — see [Fallback: docker-compose](#fallback-docker-compose).

Aspire is a **development-time tool only** here. It is never used to publish or deploy (`aspire publish`/`aspire deploy` are out of scope); the existing `deploy/` pipeline is unaffected.

## Prerequisites

| Tool | Needed for | Notes |
|---|---|---|
| .NET SDK 10 | AppHost, all .NET services | `dotnet --version` should report `10.0.x` |
| A container runtime (Docker Desktop) | Postgres, Redis, RabbitMQ, MinIO, Keycloak | Aspire's DCP auto-detects Docker or Podman |
| Node.js (LTS) + npm | `ui/`, `auth-ui/` | Aspire runs `npm`/`ng serve` **directly on the host**, unlike docker-compose which runs it inside a container — Node must actually be installed locally |
| Python 3.12 | `question-detector/` | Same reasoning as Node — Aspire's `AddUvicornApp` runs `pip`/`uvicorn` on the host |

If Node or Python aren't installed, everything except the Angular apps and `question-detector` still starts fine — Aspire just shows those resources as failed/exited, the rest of the graph is unaffected.

## Starting everything

```bash
cd AppHost
dotnet run
```

The first run:
- Installs pip dependencies for `question-detector/` (can take a while).
- Pulls container images for Postgres, Redis, RabbitMQ, MinIO, Keycloak if not already cached.
- Creates fresh named Docker volumes for Postgres/Redis/MinIO/Keycloak data — these persist across restarts, so subsequent runs are much faster and keep your data.

**npm install is manual, not automatic.** Aspire's own npm-install step was found to hang indefinitely on Windows (a known class of Aspire/DCP npm-spawn issue) and is disabled in `AppHost.cs` (`WithNpm(install: false)`). Whenever `ui/`'s or `auth-ui/`'s `package.json` changes, run `npm install` in that folder yourself before starting the AppHost:
```bash
cd ui && npm install
cd ../auth-ui && npm install
```

**First-time-only certificate trust:** the first `dotnet run` may prompt to trust the local HTTPS dev certificate — accept it. In a non-interactive/headless shell this step hangs waiting for a dialog that never appears; running from a normal terminal (VS Code, PowerShell) avoids that.

The console prints a dashboard URL and a one-time login link, e.g.:

```
Login to the dashboard at http://localhost:15224/login?t=...
```

Open that link. The dashboard shows every resource's state, structured logs, and distributed traces (including HTTP → database spans).

## Reaching each service/UI

| Resource | URL | Notes |
|---|---|---|
| Aspire dashboard | printed on startup (e.g. `http://localhost:15224`) | Resource graph, logs, traces, metrics |
| Gateway (public entry point) | `http://localhost:5678` | Same port as docker-compose; all API/UI traffic should go through here |
| Main Angular app | `http://localhost:4200` (also reachable via the gateway's catch-all route) | |
| Auth UI | `http://localhost:4201` | |
| Keycloak admin console | via the Keycloak resource's endpoint link in the dashboard (Aspire-assigned port) | Realm `exam-realm` is imported automatically from `deploy/keycloak/import/realm-export.json` |
| pgAdmin | via the Postgres resource's link in the dashboard | |
| Redis Insight | via the Redis resource's link in the dashboard | |
| RabbitMQ management UI | via the RabbitMQ resource's link in the dashboard (AMQP itself is pinned to `localhost:5672`) | |
| MinIO API / console | `http://localhost:9000` / `http://localhost:9001` | Same ports as docker-compose |
| question-detector | via its resource's link in the dashboard (Aspire-assigned port) | |

ExamDotnetApi (`:5079`), auth-api (`:6079`), and BadgeService (`:8006`) are also directly reachable at their pinned ports for debugging, though normal traffic should go through the gateway.

## Running a single service standalone (no AppHost)

Every service still runs on its own, exactly as before — this was verified for each phase of the migration, not assumed. Example for ExamDotnetApi against locally-running infrastructure:

```bash
cd api/ExamApp.Api
ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=worksheet;Username=examuser;Password=exampass" \
Redis__Configuration="localhost:6379,password=MyStrongRedisPassword" \
MinioConfig__Endpoint="localhost:9000" \
dotnet run
```

(Use whatever local addresses your infrastructure is actually reachable at — the point is that no AppHost-specific code exists in any service; every config key AppHost injects has exactly the same name as what already lived in `appsettings.json`/`appsettings.Development.json`.)

The Gateway is a partial exception: when run standalone, its `ocelot*.json` files are read completely unmodified (see the decision log's Ocelot section) — no AppHost env vars are present, so nothing about its routing changes from how it worked before this migration.

## Fallback: docker-compose

`docker-compose.yml` + `docker-compose.override.yml` are unchanged and still work:

```bash
docker-compose up -d
```

The two setups can't run side by side for the pieces whose ports Aspire pinned to the same values (Postgres 5433 external / RabbitMQ 5672, MinIO 9000/9001) — stop one before starting the other for those. See [aspire-migration-decisions.md](aspire-migration-decisions.md) for exactly which ports are pinned and why.
