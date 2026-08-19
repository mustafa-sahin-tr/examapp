var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Infrastructure resources
// Image tags / credentials / volume names mirror docker-compose.yml so this
// AppHost can be treated as a drop-in replacement for local dev without
// losing data. See aspire-migration-agent-brief.md for the full rationale.
// ---------------------------------------------------------------------------

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("examapp-postgres-data")
    .WithPgAdmin();

// One database per service that owns one, per the discovery report.
// Resource names are AppHost-internal; databaseName is the real Postgres DB name.
var examDb = postgres.AddDatabase("examdb", databaseName: "worksheet");
var identityDb = postgres.AddDatabase("identitydb", databaseName: "identity");
var badgeDb = postgres.AddDatabase("badgedb", databaseName: "badge");
var financeDb = postgres.AddDatabase("financedb", databaseName: "finance_db");

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

// ---------------------------------------------------------------------------
// ExamDotnetApi (api/ExamApp.Api)
// ---------------------------------------------------------------------------

var examDotnetApi = builder.AddProject<Projects.ExamApp_Api>("exam-dotnet-api")
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
// OcelotGateway (Services/Gateway) — externally reachable entry point.
// Downstream host/port overrides for exam-dotnet-api and exam-badge-api are
// read by Program.cs from EXAM_DOTNET_API_HOST/PORT and EXAM_BADGE_API_HOST/
// PORT; see the comment there for why (the Ocelot problem — static
// DownstreamHostAndPorts vs. Aspire's dynamic project ports).
// ---------------------------------------------------------------------------

var badgeServiceHttp = badgeService.GetEndpoint("http");

var ocelotGateway = builder.AddProject<Projects.Gateway>("ocelot-gateway")
    .WithReference(examDotnetApi)
    .WithReference(badgeService)
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

builder.Build().Run();
