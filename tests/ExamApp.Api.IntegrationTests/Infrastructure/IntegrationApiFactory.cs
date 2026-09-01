using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real exam API against a throwaway PostgreSQL container. Keeps the real
/// pipeline (routing, EF, migrations, KeycloakRoleTransformer, authorization policies)
/// and swaps only the external edges: Keycloak JWT → header auth, MinIO → no-op,
/// Redis → in-memory cache.
/// </summary>
public sealed class IntegrationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _db.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();

        // Program.cs reads these before builder.Build(), so they must be on the
        // environment before the host is first created (which happens lazily on
        // the first CreateClient()/.Services call from a test).
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _db.GetConnectionString());
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("Redis__Configuration", "unused");
        Environment.SetEnvironmentVariable("Redis__InstanceName", "test");
        Environment.SetEnvironmentVariable("Server__BaseUrl", "http://localhost");
        Environment.SetEnvironmentVariable("Keycloak__Host", "http://localhost");
        Environment.SetEnvironmentVariable("Keycloak__Realm", "exam-realm");
        Environment.SetEnvironmentVariable("Keycloak__ServiceClients__0", "exam-admin");
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "");
        Environment.SetEnvironmentVariable("MinioConfig__BucketName", "test");
        Environment.SetEnvironmentVariable("MinioConfig__BaseUrl", "http://fake-minio");
        Environment.SetEnvironmentVariable("MinioConfig__Endpoint", "localhost:9000");
        Environment.SetEnvironmentVariable("MinioConfig__AccessKey", "x");
        Environment.SetEnvironmentVariable("MinioConfig__SecretKey", "x");
    }

    public override async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMinIoService>();
            services.AddSingleton<IMinIoService, FakeMinIoService>();

            services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.Scheme;
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }
}
