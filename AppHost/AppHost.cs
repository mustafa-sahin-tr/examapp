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
    // AddKeycloak's "primary" endpoint auto-switches to HTTPS (container
    // port 8443) in run mode via an internal SubscribeHttpsEndpointsUpdate
    // hook — WithDeveloperCertificateTrust(false) does NOT stop this (tried;
    // Docker still only published 8443/9000, never 8080). Rather than fight
    // that, a second, independent HTTP endpoint is declared here on
    // Keycloak's own container port 8080 — which Keycloak's startup log
    // confirms it listens on regardless ("Listening on: http://0.0.0.0:8080
    // and https://0.0.0.0:8443") — and used for all downstream wiring
    // instead of the auto-switched "http"-named one. Ocelot's ocelot*.json
    // routes hardcode DownstreamScheme: "http" for every Keycloak route, so
    // sending them at the auto-switched HTTPS endpoint produced TLS
    // ServerHello bytes misread as a garbled HTTP response
    // ("ConnectionToDownstreamServiceError: response ended prematurely").
    .WithHttpEndpoint(port: 8082, targetPort: 8080, name: "http-plain")
    // Custom login theme — docker-compose.override.yml bind-mounts the same
    // ./keycloak-themes/my-theme directory to this exact container path.
    // (keycloak-themes/import, also mounted there, is empty — no realm
    // content is missed by not wiring it too.)
    .WithBindMount("../keycloak-themes/my-theme", "/opt/keycloak/themes/my-theme")
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

var keycloakHttp = keycloak.GetEndpoint("http-plain");

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
    // isProxied: false — without it, DCP tries to front this endpoint with
    // its own loopback (127.0.0.1/::1) proxy, but since ListenAnyIP binds
    // the wildcard address instead of whatever DCP expected, that proxy
    // never actually forwards anywhere: every request to "localhost:5079"
    // hits DCP's dead-end listener and hangs forever instead of reaching
    // Kestrel. isProxied: false lets Kestrel own the port directly instead.
    .WithHttpEndpoint(port: 5079, name: "http", isProxied: false)
    .WithEnvironment("Kestrel__Port", "5079")
    // Program.cs calls app.UseHttpsRedirection(), which — since nothing
    // configures HttpsRedirectionOptions.HttpsPort explicitly — falls back
    // to ASPNETCORE_HTTPS_PORT. That env var otherwise resolves to
    // launchSettings.json's "https" profile port (a leftover from project
    // scaffolding, e.g. auth-api's 7246), which nothing actually listens on
    // here (Kestrel only binds plain HTTP). Requests were getting redirected
    // to that dead port instead of ever reaching the API. Setting it empty
    // makes the middleware unable to determine a target, so it no-ops
    // instead of redirecting (logged at Debug level, not an error).
    .WithEnvironment("ASPNETCORE_HTTPS_PORT", "")
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
    // isProxied: false for the same dead-end-loopback-proxy reason as above.
    .WithHttpEndpoint(port: 8006, name: "http", isProxied: false)
    .WithEnvironment("Kestrel__Port", "8006")
    // Same UseHttpsRedirection()-targets-a-dead-launchSettings-port fix as
    // ExamDotnetApi above.
    .WithEnvironment("ASPNETCORE_HTTPS_PORT", "")
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
    // isProxied: false for the same dead-end-loopback-proxy reason as
    // ExamDotnetApi above.
    .WithHttpEndpoint(port: 6079, name: "http", isProxied: false)
    .WithEnvironment("Kestrel__Port", "6079")
    // Same UseHttpsRedirection()-targets-a-dead-launchSettings-port fix as
    // ExamDotnetApi above — this is literally the service (auth-api) where
    // it was actually observed (redirected to launchSettings' :7246).
    .WithEnvironment("ASPNETCORE_HTTPS_PORT", "")
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

var authApiHttp = authApi.GetEndpoint("http");

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
    // isProxied: false for the same dead-end-loopback-proxy reason as
    // ExamDotnetApi above — confirmed via netstat that without it, dcp owned
    // 127.0.0.1:5678/::1:5678 while Gateway.exe only got the wildcard
    // address, so every "localhost:5678" request timed out.
    .WithHttpEndpoint(port: 5678, name: "http", isProxied: false)
    .WithEnvironment("Kestrel__Port", "5678")
    .WithExternalHttpEndpoints()
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["EXAM_DOTNET_API_HOST"] = examDotnetApiHttp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["EXAM_DOTNET_API_PORT"] = examDotnetApiHttp.Property(EndpointProperty.Port);
        context.EnvironmentVariables["EXAM_BADGE_API_HOST"] = badgeServiceHttp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["EXAM_BADGE_API_PORT"] = badgeServiceHttp.Property(EndpointProperty.Port);
        // auth-api, keycloak and minio's DownstreamHostAndPorts entries are
        // also docker-compose hostnames Ocelot can't resolve under Aspire —
        // same fix, same reason. Missing these specifically broke the login
        // flow: /oidc-login (Gateway's own redirect middleware, which routes
        // back through /auth/realms/*) needs "keycloak" resolved, and the
        // BFF password-grant call from auth-ui needs "auth-api" resolved.
        context.EnvironmentVariables["AUTH_API_HOST"] = authApiHttp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["AUTH_API_PORT"] = authApiHttp.Property(EndpointProperty.Port);
        context.EnvironmentVariables["KEYCLOAK_HOST"] = keycloakHttp.Property(EndpointProperty.Host);
        context.EnvironmentVariables["KEYCLOAK_PORT"] = keycloakHttp.Property(EndpointProperty.Port);
        context.EnvironmentVariables["MINIO_HOST"] = minioApiEndpoint.Property(EndpointProperty.Host);
        context.EnvironmentVariables["MINIO_PORT"] = minioApiEndpoint.Property(EndpointProperty.Port);
    })
    .WaitFor(examDotnetApi)
    .WaitFor(badgeService)
    .WaitFor(authApi)
    .WaitFor(keycloak)
    .WaitFor(minio);

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
    // AuthApiBaseUrl is another docker-compose hostname ("auth-api:5079")
    // Ocelot's override mechanism never touches, since it's not an Ocelot
    // route at all — ExamDotnetApi's own AuthApiClient calls auth-api
    // directly via HttpClient (e.g. AuthController.RefreshProfileInformation
    // -> GetUserProfileAsync). Surfaced as "No such host is known.
    // (auth-api:5079)" once JWT validation itself started passing.
    .WithEnvironment("AuthApiBaseUrl", authApiHttp)
    .WaitFor(keycloak)
    .WaitFor(authApi);

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

// KC_HOSTNAME pins Keycloak's own issuer stamping to the gateway's public
// URL, regardless of which address a caller actually used to reach it.
// Without this (hostname-strict=false, matching docker-compose), Keycloak
// echoes back whatever Host header the caller used as the token's "iss"
// claim — auth-api calls Keycloak directly at its internal Aspire endpoint
// (Keycloak:Host, not through the gateway, correctly avoiding unnecessary
// gateway hops for server-to-server calls) for the actual login token
// exchange, which minted tokens with iss=<keycloak's internal address>
// instead of iss=<gateway>, so ExamDotnetApi/BadgeService (which validate
// against Server:BaseUrl=<gateway>) rejected every token with a 401 that
// looked unrelated to networking — exactly the failure mode the migration
// brief warned about. docker-compose.yml sidesteps this the same way, just
// pointed at Keycloak's own host port (8081) instead of the gateway (5678),
// since ExamDotnetApi's actual issuer expectation there is a separate,
// pre-existing gap (Server:BaseUrl is missing from its appsettings.json
// entirely in dev, outside this migration's scope to fix).
//
// A literal string, not gatewayPublicUrl (the EndpointReference) — wiring a
// container (Keycloak) to a project resource's isProxied:false endpoint
// creates a dependency DCP's container-tunnel mechanism can't resolve
// ("Container tunnel service ocelot-gateway-http-1 should have valid
// address at this point"). The gateway's port is already pinned and stable
// by design, so no dynamic resolution is actually needed here.
keycloak = keycloak.WithEnvironment("KC_HOSTNAME", "http://localhost:5678");

// KC_HOSTNAME (above) makes Keycloak redirect every URL it builds — admin
// console and welcome page included — to the gateway, which has no Ocelot
// route for /admin/*, so the admin console becomes unreachable from both the
// direct Aspire endpoint and the gateway. KC_HOSTNAME_ADMIN splits the admin
// console onto Keycloak's own http-plain endpoint (:8082) while tokens keep
// iss=<gateway>. Reach the console at http://localhost:8082/admin/.
keycloak = keycloak.WithEnvironment("KC_HOSTNAME_ADMIN", "http://localhost:8082");

// ---------------------------------------------------------------------------
// Angular apps (ui/, auth-ui/) — conveniences only, per the migration brief;
// nothing above depends on these.
//
// Angular env vars set here (WithEnvironment/WithReference) reach the
// ng serve/Node process, never the browser bundle — a build-time env file or
// dev-server proxy would be needed to make the *browser* aware of anything
// dynamic. That step was deliberately skipped: both apps' environment.ts
// already hardcode http://localhost:5079 (ExamDotnetApi) and
// http://localhost:5678 (Gateway), which happen to be exactly what's pinned
// above, so they resolve correctly as-is. This is fragile — if those pins
// ever move, environment.ts needs a manual update — but reworking the
// Angular apps to consume Aspire-injected config was judged out of scope for
// an orchestration migration; noted in the decision log for follow-up.
//
// Both apps' package.json "start" script (`ng serve`) defaults to port 4200
// with no override — as bare host processes under AppHost (not separate
// containers, unlike docker-compose), running both on the same default port
// would collide the same way ExamDotnetApi/auth-api did. ui/ keeps the
// default "start" script (4200, matching docker-compose's own ui port).
// auth-ui/ got a new "start:aspire" package.json script
// (`ng serve ... --port 4201`, matching its docker-compose host-port
// mapping) rather than changing "start" itself, so standalone/docker-compose
// behaviour is untouched.
// ---------------------------------------------------------------------------

// Deliberately NOT calling WithHttpEndpoint here. `ng serve` already binds
// its own fixed port via the "start"/"start:aspire" script's own flags
// (4200/4201, see above) independently of anything Aspire assigns. Declaring
// an Aspire-managed HTTP endpoint on top of that made DCP try to front the
// resource with its own reverse-proxy listener on the *same* port — which
// only partially bound (it grabbed the loopback addresses, 127.0.0.1/::1,
// while the real node process kept the wildcard address, 0.0.0.0), so any
// request to "localhost" hit DCP's half-broken proxy and hung forever
// instead of ever reaching ng serve. Without a declared endpoint, DCP
// doesn't create that competing listener, and "localhost:4200/4201" reaches
// the real process directly. The tradeoff: these two resources have no
// Aspire-tracked endpoint, so the Gateway wiring below uses plain
// "localhost"/4200/4201 literals instead of GetEndpoint("http") — safe here
// specifically because both ports are already hardcoded and stable by
// design, not dynamically assigned.
// WithNpm(install: false) — Aspire's own "npm install" child-process step
// (spawned via DCP before "start"/"start:aspire" runs) was observed hanging
// indefinitely on this machine (zero CPU progress over 90+ seconds, while
// running `npm install` directly in the same folder took 10-16s and found
// everything already up to date) — a known class of Windows/DCP npm-spawn
// issue (see microsoft/aspire#13145 for a related report). Disabling it
// means node_modules must be kept up to date manually (`npm install` in
// ui/ or auth-ui/) whenever package.json changes, before starting AppHost.
var angularApp = builder.AddJavaScriptApp("angular-app", "../ui", "start")
    .WithNpm(install: false)
    .WithReference(ocelotGateway)
    .WaitFor(ocelotGateway);

var authUi = builder.AddJavaScriptApp("auth-ui", "../auth-ui", "start:aspire")
    .WithNpm(install: false)
    .WithReference(ocelotGateway)
    .WaitFor(ocelotGateway);

// Ocelot's /app/* and catch-all routes point at "auth-ui:4200"/
// "angular-app:4200" (the docker-compose container hostnames), which don't
// resolve at all under Aspire — both Angular apps run as bare host processes
// on different ports (4200/4201, see above), not containers on those names.
// Same fix as the exam-dotnet-api/exam-badge-api overrides above, applied
// here too (this was missed when the Angular apps were first added and only
// surfaced when /app/login returned nothing through the gateway). Plain
// literals rather than GetEndpoint("http") — neither Angular resource has an
// Aspire-tracked endpoint (see the comment above explaining why), but their
// ports are hardcoded and stable by design, so this is safe.
ocelotGateway = ocelotGateway
    .WithEnvironment("AUTH_UI_HOST", "localhost")
    .WithEnvironment("AUTH_UI_PORT", "4201")
    .WithEnvironment("ANGULAR_APP_HOST", "localhost")
    .WithEnvironment("ANGULAR_APP_PORT", "4200");

// ---------------------------------------------------------------------------
// question-detector (Python/FastAPI, YOLO + QR/OCR detection) — a container
// built from question-detector/Dockerfile, not AddUvicornApp (a host
// process). pyzbar's Windows wheel ships libzbar-64.dll/libiconv.dll
// directly and repeatedly failed to load them ("Could not find module
// libiconv.dll (or one of its dependencies)") even though both files were
// physically present — a native-dependency problem specific to running on
// the Windows host. The Dockerfile installs the equivalent Linux package
// (libzbar0) via apt, which resolves its own transitive dependencies
// correctly, sidestepping the Windows DLL issue entirely and matching
// docker-compose's own approach (question-detector-dev also runs
// containerized there).
//
// The Dockerfile only installs pip dependencies — it has no COPY for the
// application source and no CMD (docker-compose's dev setup bind-mounts the
// source and supplies the run command externally too), so both are
// replicated here: bind mount question-detector/ to /app (matching the
// Dockerfile's WORKDIR) for the source, and WithArgs for the uvicorn
// command matching docker-compose.override.yml's dev command.
//
// Does not reference Postgres or RabbitMQ — verified via discovery that the
// service has no database/broker imports at all, unlike what the brief's
// Phase 4 step assumed generically.
// ---------------------------------------------------------------------------

var questionDetector = builder.AddDockerfile("question-detector", "..", "question-detector/Dockerfile")
    .WithBindMount("../question-detector", "/app")
    .WithArgs("uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8080", "--reload")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http");

// Ocelot's /question-detector-dev/* route points at "question-detector-dev"
// (docker-compose's hostname), same class of fix as auth-ui/angular-app/
// auth-api/keycloak/minio above — missed initially since question-detector
// was still an AddUvicornApp host process at the time and this route hadn't
// been exercised yet. A literal, not GetEndpoint("http") — the container's
// port is already pinned (8080), matching the auth-ui/angular-app reasoning.
ocelotGateway = ocelotGateway
    .WithEnvironment("QUESTION_DETECTOR_HOST", "localhost")
    .WithEnvironment("QUESTION_DETECTOR_PORT", "8080");

builder.Build().Run();
