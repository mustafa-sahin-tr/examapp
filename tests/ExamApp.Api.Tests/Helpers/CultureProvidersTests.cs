using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// Tests for <see cref="NormalizedAcceptLanguageCultureProvider"/> and
/// <see cref="UserPreferredLocaleCultureProvider"/> (issue #181).
/// </summary>
public class CultureProvidersTests
{
    // ---- NormalizedAcceptLanguageCultureProvider tests ----

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_tr_header_returns_tr_TR()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "tr";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("tr-TR");
        result.UICultures.Single().Value.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_en_US_header_returns_en_US()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "en-US";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("en-US");
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_en_GB_header_returns_en_US()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "en-GB";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("en-US");
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_tr_TR_underscore_returns_tr_TR()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "tr_TR";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_quality_weighted_list_returns_first_supported()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "en-GB,tr;q=0.8";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("en-US");
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_second_preference_supported_returns_second()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "de,tr;q=0.8";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_unsupported_language_returns_null()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "de";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_without_header_returns_null()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task NormalizedAcceptLanguageProvider_with_wildcard_returns_null()
    {
        var provider = new NormalizedAcceptLanguageCultureProvider();
        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "*";

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldBeNull();
    }

    // ---- UserPreferredLocaleCultureProvider tests ----

    private UserProfileCacheService CreateCacheServiceWithProfile(string userId, string preferredLocale)
    {
        var profile = new UserProfileDto { Id = userId, Email = "test@test.local", PreferredLocale = preferredLocale };
        var json = JsonSerializer.Serialize(profile);

        // Mock cache that returns the profile for this user
        var mockCache = Substitute.For<IDistributedCache>();
        mockCache
            .GetStringAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(json));

        var logger = Substitute.For<ILogger<UserProfileCacheService>>();
        return new UserProfileCacheService(mockCache, logger);
    }

    [Fact]
    public async Task UserPreferredLocaleProvider_authenticated_user_with_cached_en_profile_returns_en_US()
    {
        var cacheService = CreateCacheServiceWithProfile("user-1", "en");

        var provider = new UserPreferredLocaleCultureProvider();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1")
        }, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("en-US");
    }

    [Fact]
    public async Task UserPreferredLocaleProvider_authenticated_user_with_cached_tr_profile_returns_tr_TR()
    {
        var cacheService = CreateCacheServiceWithProfile("user-2", "tr");

        var provider = new UserPreferredLocaleCultureProvider();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-2")
        }, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldNotBeNull();
        result.Cultures.Single().Value.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task UserPreferredLocaleProvider_cache_miss_returns_null()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var logger = Substitute.For<ILogger<UserProfileCacheService>>();
        var cacheService = new UserProfileCacheService(cache, logger);

        var provider = new UserPreferredLocaleCultureProvider();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-not-in-cache")
        }, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task UserPreferredLocaleProvider_anonymous_user_returns_null()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var logger = Substitute.For<ILogger<UserProfileCacheService>>();
        var cacheService = new UserProfileCacheService(cache, logger);

        var provider = new UserPreferredLocaleCultureProvider();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(); // not authenticated

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task UserPreferredLocaleProvider_cache_exception_returns_null()
    {
        // Create a cache that throws on GetStringAsync
        var mockCache = Substitute.For<IDistributedCache>();
        mockCache
            .GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new Exception("Redis unavailable"));

        var logger = Substitute.For<ILogger<UserProfileCacheService>>();
        var cacheService = new UserProfileCacheService(mockCache, logger);

        var provider = new UserPreferredLocaleCultureProvider();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-sub")
        }, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        var result = await provider.DetermineProviderCultureResult(context);

        result.ShouldBeNull();
    }

    // ---- Provider ordering tests (RequestLocalizationMiddleware) ----

    [Fact]
    public async Task RequestLocalizationMiddleware_header_takes_precedence_over_profile()
    {
        // Setup: user has "tr" preference, but sends "en" in Accept-Language header
        var cacheService = CreateCacheServiceWithProfile("user-pref", "tr");

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName),
            SupportedCultures = new List<CultureInfo> { new CultureInfo("tr-TR"), new CultureInfo("en-US") },
            SupportedUICultures = new List<CultureInfo> { new CultureInfo("tr-TR"), new CultureInfo("en-US") },
            RequestCultureProviders = new IRequestCultureProvider[]
            {
                new NormalizedAcceptLanguageCultureProvider(),
                new UserPreferredLocaleCultureProvider()
            }
        };

        var loggerFactory = Substitute.For<ILoggerFactory>();
        var middleware = new RequestLocalizationMiddleware(
            next: _ => Task.CompletedTask,
            options: Options.Create(options),
            loggerFactory: loggerFactory);

        var context = new DefaultHttpContext();
        context.Request.Headers["Accept-Language"] = "en";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-pref")
        }, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        await middleware.Invoke(context);

        context.Features.Get<IRequestCultureFeature>()?.RequestCulture.Culture.Name.ShouldBe("en-US");
    }

    [Fact]
    public async Task RequestLocalizationMiddleware_profile_used_when_no_header()
    {
        // Setup: user has "en" preference, no Accept-Language header
        var cacheService = CreateCacheServiceWithProfile("user-pref2", "en");

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName),
            SupportedCultures = new List<CultureInfo> { new CultureInfo("tr-TR"), new CultureInfo("en-US") },
            SupportedUICultures = new List<CultureInfo> { new CultureInfo("tr-TR"), new CultureInfo("en-US") },
            RequestCultureProviders = new IRequestCultureProvider[]
            {
                new NormalizedAcceptLanguageCultureProvider(),
                new UserPreferredLocaleCultureProvider()
            }
        };

        var loggerFactory = Substitute.For<ILoggerFactory>();
        var middleware = new RequestLocalizationMiddleware(
            next: _ => Task.CompletedTask,
            options: Options.Create(options),
            loggerFactory: loggerFactory);

        var context = new DefaultHttpContext();
        // No Accept-Language header
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-pref2")
        }, "test"));

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        await middleware.Invoke(context);

        context.Features.Get<IRequestCultureFeature>()?.RequestCulture.Culture.Name.ShouldBe("en-US");
    }

    [Fact]
    public async Task RequestLocalizationMiddleware_default_used_when_header_and_profile_absent()
    {
        // Setup: no Accept-Language header, user not in cache or not authenticated
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var logger = Substitute.For<ILogger<UserProfileCacheService>>();
        var cacheService = new UserProfileCacheService(cache, logger);

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName),
            SupportedCultures = new List<CultureInfo> { new CultureInfo("tr-TR"), new CultureInfo("en-US") },
            SupportedUICultures = new List<CultureInfo> { new CultureInfo("tr-TR"), new CultureInfo("en-US") },
            RequestCultureProviders = new IRequestCultureProvider[]
            {
                new NormalizedAcceptLanguageCultureProvider(),
                new UserPreferredLocaleCultureProvider()
            }
        };

        var loggerFactory = Substitute.For<ILoggerFactory>();
        var middleware = new RequestLocalizationMiddleware(
            next: _ => Task.CompletedTask,
            options: Options.Create(options),
            loggerFactory: loggerFactory);

        var context = new DefaultHttpContext();
        // No Accept-Language header, anonymous user
        context.User = new ClaimsPrincipal();

        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        context.RequestServices = services.BuildServiceProvider();

        await middleware.Invoke(context);

        context.Features.Get<IRequestCultureFeature>()?.RequestCulture.Culture.Name.ShouldBe("tr-TR");
    }
}
