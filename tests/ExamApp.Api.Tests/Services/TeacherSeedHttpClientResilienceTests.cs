using System.Diagnostics;
using System.Net;
using ExamApp.Api.Services.Teachers.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Kök neden (2026-09-22 Kars+Erzincan koşusu): ServiceDefaults <c>ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler())</c>
/// tüm named client'lara 10 sn attempt timeout + 3 retry ekliyordu; <c>client.Timeout = 30 dk</c> bunu ezmiyordu. 500'lük
/// partial-import 10 sn'yi aşınca istek iptal edilip aynı parti yeniden gönderildi → Keycloak 409, identity yazılmadı,
/// 4.480 yetim Keycloak kullanıcısı. Bu testler <see cref="AuthApiSeedClient.HttpClientName"/> client'ının o varsayılandan
/// muaf olduğunu (pipeline'da ResilienceHandler yok; attempt timeout'u aşan tek istek iptal edilmez ve tekrarlanmaz) ve kontrol
/// olarak başka bir client'ın hâlâ handler'a sahip olduğunu (aynı istek iptal edilip yenilenir) doğrular. Testin kendi
/// standart handler'ı kısa sürelerle (attempt 1 sn) kurulur — kanıt üretimdeki 10 sn ile aynı, süre kısa.
/// </summary>
public class TeacherSeedHttpClientResilienceTests
{
    private sealed class CountingDelayHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        public int Calls;
        public bool ObservedCancellation;

        public CountingDelayHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            try
            {
                await Task.Delay(_delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static IHostEnvironment DevEnv()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        return env;
    }

    /// <summary>Üretimdeki kayıt sırasıyla aynı: önce ServiceDefaults benzeri varsayılan, sonra AddTeacherSeed.</summary>
    private static ServiceProvider BuildProvider(HttpMessageHandler primaryForSeedClient, HttpMessageHandler? primaryForControl = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(3);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(2);
            o.Retry.Delay = TimeSpan.FromMilliseconds(50); // varsayılan 2 sn backoff toplam süreyi aşardı; kontrol testinde retry görülsün
        }));
        services.AddTeacherSeed(DevEnv());
        services.AddHttpClient(AuthApiSeedClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => primaryForSeedClient);
        services.AddHttpClient("control").ConfigurePrimaryHttpMessageHandler(() => primaryForControl ?? new CountingDelayHandler(TimeSpan.Zero));
        return services.BuildServiceProvider();
    }

    private static List<Type> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<Type>();
        var current = handler;
        while (current is not null)
        {
            chain.Add(current.GetType());
            current = (current as DelegatingHandler)?.InnerHandler;
        }
        return chain;
    }

    [Fact]
    public void Seed_client_pipeline_has_no_resilience_handler_but_other_clients_still_do()
    {
        using var provider = BuildProvider(new CountingDelayHandler(TimeSpan.Zero));
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        var seedChain = HandlerChain(factory.CreateHandler(AuthApiSeedClient.HttpClientName));
        var controlChain = HandlerChain(factory.CreateHandler("control"));

        seedChain.ShouldNotContain(t => t == typeof(ResilienceHandler) || t.IsSubclassOf(typeof(ResilienceHandler)));
        controlChain.ShouldContain(t => t == typeof(ResilienceHandler) || t.IsSubclassOf(typeof(ResilienceHandler)));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AuthApiSeedClient.HttpClientName);
        client.Timeout.ShouldBe(AuthApiSeedClient.Timeout);
        client.Timeout.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Seed_client_request_slower_than_attempt_timeout_completes_once_without_cancellation_or_retry()
    {
        // Attempt timeout 1 sn (üretimde 10 sn): 1,5 sn süren istek standart handler'da iptal edilip yenilenirdi.
        var handler = new CountingDelayHandler(TimeSpan.FromSeconds(1.5));
        using var provider = BuildProvider(handler);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AuthApiSeedClient.HttpClientName);

        var sw = Stopwatch.StartNew();
        using var response = await client.PostAsync("http://auth-api.test/api/auth/dev/seed-users", new StringContent("{}"));
        sw.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Calls.ShouldBe(1);
        handler.ObservedCancellation.ShouldBeFalse();
        sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.4));
    }

    [Fact]
    public async Task Control_client_with_standard_handler_cancels_the_same_slow_request_and_retries()
    {
        // Kontrol: aynı gecikme, resilience'lı client → attempt iptal (handler iptali görür) ve yeniden deneme (>1 çağrı).
        var control = new CountingDelayHandler(TimeSpan.FromSeconds(1.5));
        using var provider = BuildProvider(new CountingDelayHandler(TimeSpan.Zero), control);
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("control");

        await Should.ThrowAsync<Exception>(() => client.PostAsync("http://auth-api.test/x", new StringContent("{}")));

        control.ObservedCancellation.ShouldBeTrue();
        control.Calls.ShouldBeGreaterThan(1);
    }
}
