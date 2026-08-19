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

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

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

builder.Build().Run();
