var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Infrastructure resources
// Image tags / credentials / volume names mirror docker-compose.yml so this
// AppHost can be treated as a drop-in replacement for local dev without
// losing data. See aspire-migration-agent-brief.md for the full rationale.
// ---------------------------------------------------------------------------

// Explicit user/password (matching docker-compose.yml's examuser/exampass)
// rather than Postgres's own random-password default, so they can be handed
// to Keycloak's KC_DB_USERNAME/KC_DB_PASSWORD below without guessing at
// whether UserNameParameter/PasswordParameter would be non-null.
var postgresUser = builder.AddParameter("postgres-user");
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", userName: postgresUser, password: postgresPassword)
    .WithDataVolume("examapp-postgres-data")
    .WithPgAdmin();
var postgresEndpoint = postgres.GetEndpoint("tcp");

// One database per service that owns one, per the discovery report.
// Resource names are AppHost-internal; databaseName is the real Postgres DB name.
var examDb = postgres.AddDatabase("examdb", databaseName: "worksheet");
var identityDb = postgres.AddDatabase("identitydb", databaseName: "identity");
var badgeDb = postgres.AddDatabase("badgedb", databaseName: "badge");
var financeDb = postgres.AddDatabase("financedb", databaseName: "finance_db");
// Keycloak's own storage — replaces docker-compose's reliance on a
// postgres/init-scripts entry to pre-create this database.
var keycloakDb = postgres.AddDatabase("keycloakdb", databaseName: "keycloak");

var redis = builder.AddRedis("redis")
    .WithDataVolume("examapp-redis-data")
    .WithRedisInsight();

// Explicit user/password parameters (matching docker-compose.yml's
// rabbituser/rabbitpass) so rabbitmq.Resource.UserNameParameter/
// PasswordParameter are guaranteed non-null for the WithEnvironment calls
// below, instead of relying on RabbitMQ's own random-password default.
var rabbitUser = builder.AddParameter("rabbitmq-user");
var rabbitPassword = builder.AddParameter("rabbitmq-password", secret: true);

// Pinned to the standard AMQP port 5672 (matching docker-compose.yml) because
// BadgeService/OutboxPublisher's MassTransit setup (cfg.Host(host, "/", ...))
// only reads RabbitMQ:Host, not a port — it always assumes 5672. A dynamic
// Aspire-assigned port would silently connect to the wrong place instead of
// failing loudly, so this is fixed rather than left to random allocation.
var rabbitmq = builder.AddRabbitMQ("rabbitmq", userName: rabbitUser, password: rabbitPassword, port: 5672)
    .WithManagementPlugin();
var rabbitmqEndpoint = rabbitmq.GetEndpoint("tcp");

// No first-party or Community Toolkit MinIO hosting integration is used here:
// CommunityToolkit.Aspire.Hosting.Minio exists but is marked deprecated
// (upstream MinIO OSS is archived and the integration will be removed in a
// future Aspire version), so MinIO is modelled as a plain container resource
// instead, matching the credentials/volume already used in docker-compose.yml.
// Values come from appsettings.json's "Parameters" section (kept fixed at
// "minioadmin"/"minioadmin", matching docker-compose.yml, so existing local
// MinIO volume data stays accessible across restarts, and so a fresh clone
// gets a working default without extra setup — appsettings.Development.json
// is gitignored repo-wide, so it can't hold the checked-in default) rather
// than a literal in this file, and are still redacted in dashboard logs.
var minioRootUser = builder.AddParameter("minio-root-user");
var minioRootPassword = builder.AddParameter("minio-root-password", secret: true);

var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithEnvironment("MINIO_ROOT_USER", minioRootUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioRootPassword)
    .WithVolume("examapp-minio-data", "/data")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console");

var minioApiEndpoint = minio.GetEndpoint("api");

// Aspire.Hosting.Keycloak is a preview-only package at 13.5.0 (no stable
// release yet) — flagged in the migration decision log.
// Realm (exam-realm: clients, roles, Google IdP broker config) is imported
// from deploy/keycloak/import/realm-export.json on every clean start, same
// file docker-compose.yml's --import-realm flag already uses, so the realm
// is reproducible without manual Keycloak admin console setup.
var keycloakAdminUsername = builder.AddParameter("keycloak-admin-username");
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);

// Not pinned to docker-compose.yml's 24.0.1: AddKeycloak enables the
// "opentelemetry" KC_FEATURES flag by default (for dashboard OTLP export),
// which doesn't exist before Keycloak 26 and makes 24.0.1 fail validation
// and exit immediately on startup. Rather than disabling that feature to
// match the older pin, upgraded to the current stable release (26.7.0) so
// Keycloak's own traces show up in the dashboard too — a deliberate
// deviation from "pin to what's in prod today," worth reviewing before this
// migration ships, since deploy/keycloak/import/realm-export.json (exported
// against 24.x) hasn't been verified against 26.x's realm import format.
var keycloak = builder.AddKeycloak("keycloak", port: 8081, adminUsername: keycloakAdminUsername, adminPassword: keycloakAdminPassword)
    .WithImageTag("26.7.0")
    .WithDataVolume("examapp-keycloak-data")
    .WithRealmImport("../deploy/keycloak/import")
    // Own storage in the "keycloak" database on the same Postgres instance —
    // matches docker-compose.yml's KC_DB/KC_DB_URL_*/KC_DB_USERNAME/PASSWORD
    // env vars exactly, just with an Aspire-managed dynamic host/port instead
    // of the fixed docker-compose network hostname.
    .WithReference(keycloakDb)
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["KC_DB_URL_HOST"] = postgresEndpoint.Property(EndpointProperty.Host);
        context.EnvironmentVariables["KC_DB_URL_PORT"] = postgresEndpoint.Property(EndpointProperty.Port);
    })
    .WithEnvironment("KC_DB_URL_DATABASE", "keycloak")
    .WithEnvironment("KC_DB_USERNAME", postgresUser)
    .WithEnvironment("KC_DB_PASSWORD", postgresPassword)
    .WaitFor(postgres);

var keycloakHttp = keycloak.GetEndpoint("http");

// ---------------------------------------------------------------------------
// ExamDotnetApi (api/ExamApp.Api)
// ---------------------------------------------------------------------------

var examDotnetApi = builder.AddProject<Projects.ExamApp_Api>("exam-dotnet-api")
    // Pinned to 5079: Program.cs manually calls
    // ConfigureKestrel(...ListenAnyIP(Kestrel:Port ?? 5079)), which ignores
    // whatever port Aspire would otherwise assign via ASPNETCORE_URLS. Both
    // the endpoint declaration and the Kestrel__Port override are needed —
    // one so Aspire's bookkeeping (dashboard, GetEndpoint("http") used by
    // BadgeService/Gateway below) reflects reality, the other so the app
    // actually binds there instead of colliding with auth-api (which has
    // the exact same 5079-default pattern in its own Program.cs).
    .WithHttpEndpoint(port: 5079, name: "http")
    .WithEnvironment("Kestrel__Port", "5079")
    // connectionName "DefaultConnection" keeps the injected env var named
    // ConnectionStrings__DefaultConnection, matching the existing
    // ConnectionStrings:DefaultConnection key so standalone `dotnet run`
    // (using appsettings.json) keeps working unchanged.
    .WithReference(examDb, connectionName: "DefaultConnection")
    .WithReference(redis)
    .WithReference(rabbitmq)
    // Redis/MinIO are consumed today via bespoke config sections
    // (Redis:Configuration, MinioConfig:*) rather than the ConnectionStrings
    // convention, so their AppHost-assigned values are mapped onto those
    // exact existing keys instead of introducing new ones.
    .WithEnvironment("Redis__Configuration", redis.Resource.ConnectionStringExpression)
    .WithEnvironment("MinioConfig__AccessKey", minioRootUser)
    .WithEnvironment("MinioConfig__SecretKey", minioRootPassword)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["MinioConfig__Endpoint"] = ReferenceExpression.Create(
            $"{minioApiEndpoint.Property(EndpointProperty.Host)}:{minioApiEndpoint.Property(EndpointProperty.Port)}");
    })
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(rabbitmq)
    .WaitFor(minio);

var examDotnetApiHttp = examDotnetApi.GetEndpoint("http");

// ---------------------------------------------------------------------------
// BadgeService (Services/BadgeService)
// ---------------------------------------------------------------------------

var badgeService = builder.AddProject<Projects.BadgeService>("exam-badge-api")
    // Same reasoning as ExamDotnetApi above — Program.cs hardcodes its own
    // Kestrel:Port default (8006), so it's pinned explicitly rather than
    // left to coincide with whatever Aspire would otherwise assign.
    .WithHttpEndpoint(port: 8006, name: "http")
    .WithEnvironment("Kestrel__Port", "8006")
    .WithReference(badgeDb, connectionName: "DefaultConnection")
    .WithReference(rabbitmq)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["RabbitMQ__Host"] = rabbitmqEndpoint.Property(EndpointProperty.Host);
    })
    .WithEnvironment("RabbitMQ__Username", rabbitUser)
    .WithEnvironment("RabbitMQ__Password", rabbitPassword)
    .WithEnvironment("MinioConfig__AccessKey", minioRootUser)
    .WithEnvironment("MinioConfig__SecretKey", minioRootPassword)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["MinioConfig__Endpoint"] = ReferenceExpression.Create(
            $"{minioApiEndpoint.Property(EndpointProperty.Host)}:{minioApiEndpoint.Property(EndpointProperty.Port)}");
    })
    // ExamApi:BaseUrl is a plain HTTP client base address (not a
    // ConnectionStrings-style key), so it's mapped directly onto its
    // existing key rather than left to service-discovery conventions.
    .WithEnvironment("ExamApi__BaseUrl", examDotnetApiHttp)
    .WaitFor(postgres)
    .WaitFor(rabbitmq)
    .WaitFor(minio)
    .WaitFor(examDotnetApi);

// ---------------------------------------------------------------------------
// OutboxPublisher (Services/OutboxPublisher) — pure worker, no HTTP endpoint.
// Shares ExamDotnetApi's database: outbox rows are written there in the same
// transaction as the business data, then relayed to RabbitMQ from here
// (confirmed against deploy/docker-compose.prod.yml, which points both
// exam-dotnet-api and exam-outbox-publisher at the same database).
// ---------------------------------------------------------------------------

var outboxPublisher = builder.AddProject<Projects.OutboxPublisherService>("exam-outbox-publisher")
    .WithReference(examDb, connectionName: "DefaultConnection")
    .WithReference(rabbitmq)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["RabbitMQ__Host"] = rabbitmqEndpoint.Property(EndpointProperty.Host);
    })
    .WithEnvironment("RabbitMQ__Username", rabbitUser)
    .WithEnvironment("RabbitMQ__Password", rabbitPassword)
    .WaitFor(postgres)
    .WaitFor(rabbitmq);

// ---------------------------------------------------------------------------
// auth-api — same-named ExamApp.Api.csproj as api/ExamApp.Api, disambiguated
// in ExamApp.AppHost.csproj via AspireProjectMetadataTypeName="AuthApi".
// Not in the original migration brief's inventory (a Phase 0 discovery gap:
// the brief only listed 4 .NET services), added here because it's the
// service that actually performs the Keycloak login (password grant) for
// the Angular apps' BFF pattern — Phase 3's login-flow gate can't be
// meaningfully tested without it.
// ---------------------------------------------------------------------------

var authApi = builder.AddProject<Projects.AuthApi>("auth-api")
    // Pinned to 6079 (docker-compose.yml's exposed host port for auth-api):
    // its Program.cs has the identical hardcoded-Kestrel-default pattern as
    // ExamDotnetApi, defaulting to 5079 too — without this override both
    // processes fight over the same port when run side by side as AppHost
    // host processes (they don't collide in docker-compose because each
    // gets its own container network namespace).
    .WithHttpEndpoint(port: 6079, name: "http")
    .WithEnvironment("Kestrel__Port", "6079")
    .WithReference(identityDb, connectionName: "DefaultConnection")
    .WithReference(redis)
    .WithEnvironment("Redis__Configuration", redis.Resource.ConnectionStringExpression)
    .WithEnvironment("MinioConfig__AccessKey", minioRootUser)
    .WithEnvironment("MinioConfig__SecretKey", minioRootPassword)
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["MinioConfig__Endpoint"] = ReferenceExpression.Create(
            $"{minioApiEndpoint.Property(EndpointProperty.Host)}:{minioApiEndpoint.Property(EndpointProperty.Port)}");
    })
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(minio);

// ---------------------------------------------------------------------------
// OcelotGateway (Services/Gateway) — externally reachable entry point.
// Downstream host/port overrides for exam-dotnet-api and exam-badge-api are
// read by Program.cs from EXAM_DOTNET_API_HOST/PORT and EXAM_BADGE_API_HOST/
// PORT; see the comment there for why (the Ocelot problem — static
// DownstreamHostAndPorts vs. Aspire's dynamic project ports).
//
// Pinned to port 5678 (matching docker-compose.yml): Gateway's own Program.cs
// calls ConfigureKestrel(...ListenAnyIP(kestrelPort))` with Kestrel:Port
// defaulting to 5678, which overrides whatever endpoint Aspire would
// otherwise assign — so this pin just makes the AppHost's bookkeeping (and
// its "external endpoint" dashboard link) match what Gateway actually binds
// to. It also matters for Keycloak: every service's Server:BaseUrl and the
// token issuer it produces are built from this exact public URL — see the
// comment below on the Keycloak wiring for the full URL topology.
// ---------------------------------------------------------------------------

var badgeServiceHttp = badgeService.GetEndpoint("http");

var ocelotGateway = builder.AddProject<Projects.Gateway>("ocelot-gateway")
    .WithReference(examDotnetApi)
    .WithReference(badgeService)
    .WithHttpEndpoint(port: 5678, name: "http")
    .WithEnvironment("Kestrel__Port", "5678")
    .WithExternalHttpEndpoints()
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["EXAM_DOTNET_API_HOST"] = examDotnetApiHttp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["EXAM_DOTNET_API_PORT"] = examDotnetApiHttp.Property(EndpointProperty.Port);
        context.EnvironmentVariables["EXAM_BADGE_API_HOST"] = badgeServiceHttp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["EXAM_BADGE_API_PORT"] = badgeServiceHttp.Property(EndpointProperty.Port);
    })
    .WaitFor(examDotnetApi)
    .WaitFor(badgeService);

// ---------------------------------------------------------------------------
// Keycloak wiring — the URL topology, spelled out:
//
// Every service builds its JWT Authority/issuer as
// {Server:BaseUrl}/realms/{Keycloak:Realm} — Server:BaseUrl is the PUBLIC
// gateway URL browsers use, so it must be identical everywhere a token gets
// minted (auth-api) or validated (ExamDotnetApi, BadgeService, Gateway).
// Pinning the gateway to :5678 above means this public URL never changes
// across restarts, so Server:BaseUrl is set to the gateway's own endpoint
// rather than a literal — if the pin above ever moves, this stays correct.
//
// Keycloak:Host, by contrast, is only used server-side to fetch the OIDC
// metadata/signing keys (MetadataAddress) — it doesn't need to match the
// issuer, only to be reachable, so it's set to Keycloak's actual
// Aspire-assigned endpoint instead of a fixed value.
//
// Mixing these two up is exactly the failure mode the migration brief warns
// about: it produces a JWT error that looks unrelated to networking.
// ---------------------------------------------------------------------------

var gatewayPublicUrl = ocelotGateway.GetEndpoint("http");

examDotnetApi = examDotnetApi
    .WithReference(keycloak)
    .WithEnvironment("Keycloak__Host", keycloakHttp)
    .WithEnvironment("Server__BaseUrl", gatewayPublicUrl)
    .WaitFor(keycloak);

badgeService = badgeService
    .WithReference(keycloak)
    .WithEnvironment("Keycloak__Host", keycloakHttp)
    .WithEnvironment("Server__BaseUrl", gatewayPublicUrl)
    .WaitFor(keycloak);

authApi = authApi
    .WithReference(keycloak)
    .WithEnvironment("Keycloak__Host", keycloakHttp)
    .WithEnvironment("Server__BaseUrl", gatewayPublicUrl)
    .WaitFor(keycloak);

ocelotGateway = ocelotGateway
    .WithReference(keycloak)
    .WithEnvironment("Keycloak__Host", keycloakHttp)
    .WithEnvironment("Server__BaseUrl", gatewayPublicUrl)
    .WaitFor(keycloak);

builder.Build().Run();
