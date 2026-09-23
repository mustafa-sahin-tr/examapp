using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
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
        // Issue #238: Program.cs artık Development dışında (Testing dahil) boş/dev-only
        // Keycloak:ClientSecret|AdminClientSecret ile açılışta fail-fast oluyor — bu testler
        // gerçek Keycloak'a hiç gitmez (TestAuthHandler auth'u devre dışı bırakır), değer
        // sadece açılış kontrolünü geçmek için.
        Environment.SetEnvironmentVariable("Keycloak__ClientSecret", "test-only-client-secret");
        Environment.SetEnvironmentVariable("Keycloak__AdminClientSecret", "test-only-admin-client-secret");
        Environment.SetEnvironmentVariable("Gemini__ApiKey", "");
        Environment.SetEnvironmentVariable("MinioConfig__BucketName", "test");
        Environment.SetEnvironmentVariable("MinioConfig__BaseUrl", "http://fake-minio");
        Environment.SetEnvironmentVariable("MinioConfig__Endpoint", "localhost:9000");
        Environment.SetEnvironmentVariable("MinioConfig__AccessKey", "x");
        Environment.SetEnvironmentVariable("MinioConfig__SecretKey", "x");
        // issue #246: admin liste rate limit'i sub başına; bu uçları çağıran testler her test için benzersiz admin sub
        // kullanır. Uzun pencere: 429 testi koşunun süresinden/zamanlamasından bağımsız olsun.
        Environment.SetEnvironmentVariable("RateLimiting__AdminUserList__PermitLimit", "10");
        Environment.SetEnvironmentVariable("RateLimiting__AdminUserList__WindowSeconds", "3600");
        // issue #156: şifre sıfırlama rate limit'i — 429 testi zamanlamadan bağımsız olsun.
        Environment.SetEnvironmentVariable("RateLimiting__AdminPasswordReset__PermitLimit", "5");
        Environment.SetEnvironmentVariable("RateLimiting__AdminPasswordReset__WindowSeconds", "3600");
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

            // issue #246: auth-api test ortamında yok. Kayıtlı sahte kullanıcılar (FakeUserDirectory) dışındaki
            // id'ler gerçek AuthApiClient'a gider ve fail-soft davranış korunur.
            services.AddSingleton<FakeUserDirectory>();
            services.RemoveAll<ExamApp.Api.Services.Interfaces.IAuthApiClient>();
            services.AddScoped<ExamApp.Api.Services.Interfaces.IAuthApiClient>(sp => new FakeUserDirectoryAuthApiClient(
                ActivatorUtilities.CreateInstance<ExamApp.Api.Services.AuthApiClient>(sp),
                sp.GetRequiredService<FakeUserDirectory>()));

            // issue #156: şifre sıfırlama uçları kayıtlı sahte Keycloak hesapları üzerinden uçtan uca çalışır;
            // diğer tüm IKeycloakService çağrıları gerçek servise gider (davranış değişmez).
            services.AddSingleton<FakeKeycloakAccounts>();
            services.RemoveAll<ExamApp.Api.Services.Interfaces.IKeycloakService>();
            services.AddScoped<ExamApp.Api.Services.Interfaces.IKeycloakService>(sp => new FakeKeycloakAccountsService(
                ActivatorUtilities.CreateInstance<ExamApp.Api.Services.KeycloakService>(sp),
                sp.GetRequiredService<FakeKeycloakAccounts>()));

            // issue #156: tüm log çıktısı (Trace dahil) — "geçici şifre loglara yazılmıyor" testi için.
            services.AddSingleton<CapturingLoggerProvider>();
            services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sp => sp.GetRequiredService<CapturingLoggerProvider>());
            services.AddLogging(b => b.AddFilter<CapturingLoggerProvider>(null, Microsoft.Extensions.Logging.LogLevel.Trace));

            services.RemoveAll<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
            services.AddDistributedMemoryCache();

            // issue #225: RabbitMQ:Host test ortamında yok → Program.cs bus kurmaz. Consumer'ları gerçek
            // MassTransit pipeline'ı (retry definition dahil) üzerinden, in-memory test harness ile çalıştır.
            services.AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromMinutes(5));
                x.AddConsumer<ExamApp.Api.Consumers.StudentPointsChangedConsumer,
                    ExamApp.Api.Consumers.StudentPointsChangedConsumerDefinition>();
            });

            services.AddAuthentication(options =>
            {
                options.DefaultScheme = TestAuthHandler.Scheme;
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }
}
