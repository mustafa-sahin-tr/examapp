using System.Net;
using System.Security.Claims;
using BadgeService.Security;
using BadgeService.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace BadgeService.Tests;

/// <summary>
/// Tests for <see cref="AuthApiCallerIdentityResolver"/>.
/// Covers HTTP client integration, caching, and error handling.
/// </summary>
public class AuthApiCallerIdentityResolverTests
{
    private static AuthApiCallerIdentityResolver CreateResolver(
        StubHttp stubHttp,
        Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?> { ["AuthApi:BaseUrl"] = "http://auth-api" })
            .Build();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = NullLogger<AuthApiCallerIdentityResolver>.Instance;

        return new AuthApiCallerIdentityResolver(stubHttp, config, cache, logger);
    }

    private static ClaimsIdentity CreateIdentity(string? sub = "test-sub")
    {
        var identity = new ClaimsIdentity("Bearer");
        if (!string.IsNullOrWhiteSpace(sub))
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
        return identity;
    }

    private static HttpContext CreateHttpContext(ClaimsIdentity identity, string? authorizationHeader = null)
    {
        var principal = new ClaimsPrincipal(identity);
        var context = new DefaultHttpContext { User = principal };
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            context.Request.Headers.Authorization = authorizationHeader;
        return context;
    }

    [Fact]
    public async Task ResolveUserIdAsync_ValidProfileReturned_ReturnsUserId()
    {
        // Criterion #1, #4: Auth API returns user profile with matching sub; resolver returns the id
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.OK, """{"id": 7, "keycloakId": "test-sub"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity, "Bearer test-token");

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBe(7);
        stubHttp.Requests.Count.ShouldBe(1);
        var request = stubHttp.Requests.First();
        request.Headers.Authorization?.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization?.Parameter.ShouldBe("test-token");
    }

    [Fact]
    public async Task ResolveUserIdAsync_ProfileKeycloakIdMismatch_ReturnsNull()
    {
        // Criterion #4: Profile keycloakId does not match token sub; security defense returns null
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.OK, """{"id": 7, "keycloakId": "different-sub"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_AuthApiReturnsUnauthorized_ReturnsNull()
    {
        // Criterion #4: Non-2xx from auth-api (401) leads to null return; fail-closed
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.Unauthorized, """{"error": "Invalid token"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_HttpClientThrowsException_ReturnsNull()
    {
        // Criterion #4: Exception during HTTP call is caught; returns null (fail-closed)
        var stubHttp = new StubHttp()
            .OnThrow("user-profile", new HttpRequestException("Simulated network failure"));

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_ConfigurationMissing_ReturnsNullWithoutHttpCall()
    {
        // Criterion #5: Missing AuthApi:BaseUrl causes early return; HTTP never called
        var stubHttp = new StubHttp();

        var resolver = CreateResolver(stubHttp, new Dictionary<string, string?> { });

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBeNull();
        stubHttp.Requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ResolveUserIdAsync_TokenWithoutSubClaim_ReturnsNull()
    {
        // Criterion #1: No sub claim in token leads to null; HTTP never called
        var stubHttp = new StubHttp();

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity(sub: null);
        var httpContext = CreateHttpContext(identity);

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBeNull();
        stubHttp.Requests.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ResolveUserIdAsync_SecondCallWithCachedValue_ReturnsFromCacheWithoutHttpCall()
    {
        // Criterion #4: Positive caching works; second call does not invoke HTTP
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.OK, """{"id": 7, "keycloakId": "test-sub"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        // First call: should hit HTTP
        var result1 = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);
        result1.ShouldBe(7);
        stubHttp.Requests.Count.ShouldBe(1);

        // Second call: should use cache
        var result2 = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);
        result2.ShouldBe(7);
        stubHttp.Requests.Count.ShouldBe(1); // No additional HTTP call
    }

    [Fact]
    public async Task ResolveUserIdAsync_NegativeCacheAfter401_SecondCallDoesNotInvokeHttp()
    {
        // Criterion #4: Negative caching works; non-2xx response is cached for 60s
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.Unauthorized, """{"error": "Invalid token"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        // First call: should hit HTTP and get 401
        var result1 = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);
        result1.ShouldBeNull();
        stubHttp.Requests.Count.ShouldBe(1);

        // Second call: should use negative cache, no additional HTTP call
        var result2 = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);
        result2.ShouldBeNull();
        stubHttp.Requests.Count.ShouldBe(1); // Still only 1 HTTP call
    }

    [Fact]
    public async Task ResolveUserIdAsync_NegativeCacheAfterKeycloakIdMismatch_SecondCallDoesNotInvokeHttp()
    {
        // Criterion #4: Negative caching works for keycloakId mismatch
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.OK, """{"id": 7, "keycloakId": "different-sub"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        // First call: should hit HTTP but profile keycloakId doesn't match
        var result1 = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);
        result1.ShouldBeNull();
        stubHttp.Requests.Count.ShouldBe(1);

        // Second call: should use negative cache
        var result2 = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);
        result2.ShouldBeNull();
        stubHttp.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ResolveUserIdAsync_ClientTimeoutTaskCanceledWithoutCtCancellation_ReturnsNull()
    {
        // Criterion #1: Client timeout (TaskCanceledException) without ct being cancelled is caught and returns null
        var stubHttp = new StubHttp()
            .OnThrow("user-profile", new TaskCanceledException("Client timeout"));

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        var result = await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_CallerCancellationTokenCancelled_ThrowsOperationCanceledException()
    {
        // Criterion #2: When caller's ct is cancelled, OperationCanceledException propagates (not caught)
        var stubHttp = new StubHttp()
            .OnThrow("user-profile", new OperationCanceledException("Caller cancelled"));

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // When OperationCanceledException is thrown AND ct.IsCancellationRequested is true,
        // the exception should propagate (not be caught as a generic exception)
        await Should.ThrowAsync<OperationCanceledException>(
            () => resolver.ResolveUserIdAsync(httpContext, cts.Token));
    }

    [Fact]
    public async Task ResolveUserIdAsync_NamedHttpClientUsesCorrectName()
    {
        // Criterion #6: Resolver uses the named HttpClient "auth-api-caller-identity"
        var stubHttp = new StubHttp()
            .On("user-profile", HttpStatusCode.OK, """{"id": 7, "keycloakId": "test-sub"}""");

        var resolver = CreateResolver(stubHttp);

        var identity = CreateIdentity("test-sub");
        var httpContext = CreateHttpContext(identity);

        await resolver.ResolveUserIdAsync(httpContext, CancellationToken.None);

        stubHttp.RequestedClientNames.ShouldContain(AuthApiCallerIdentityResolver.HttpClientName);
        stubHttp.RequestedClientNames.ShouldContain("auth-api-caller-identity");
    }
}
