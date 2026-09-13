using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
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

    [Fact]
    public void UserPreferredLocaleCultureProvider_requires_null_context_to_be_rejected()
    {
        // Arrange
        var provider = new UserPreferredLocaleCultureProvider();

        // Act & Assert: DetermineProviderCultureResult should throw if context is null
        Should.Throw<ArgumentNullException>(
            () => provider.DetermineProviderCultureResult(null!).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task UserPreferredLocaleCultureProvider_returns_null_when_unauthenticated()
    {
        // Arrange
        var provider = new UserPreferredLocaleCultureProvider();

        var context = new DefaultHttpContext();
        // No authenticated user

        var services = new ServiceCollection();
        context.RequestServices = services.BuildServiceProvider();

        // Act
        var result = await provider.DetermineProviderCultureResult(context);

        // Assert: unauthenticated user should return null
        result.ShouldBeNull();
    }

    [Fact]
    public async Task UserPreferredLocaleCultureProvider_returns_null_without_name_identifier()
    {
        // Arrange
        var provider = new UserPreferredLocaleCultureProvider();

        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                claims: new System.Security.Claims.Claim[0],  // No NameIdentifier claim
                authenticationType: "TestAuth"));

        var services = new ServiceCollection();
        context.RequestServices = services.BuildServiceProvider();

        // Act
        var result = await provider.DetermineProviderCultureResult(context);

        // Assert: no NameIdentifier means no authenticated user identity
        result.ShouldBeNull();
    }

    [Fact]
    public async Task UserPreferredLocaleCultureProvider_returns_null_when_cache_service_unavailable()
    {
        // Arrange
        var provider = new UserPreferredLocaleCultureProvider();

        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user-123")
            }, authenticationType: "TestAuth"));

        var services = new ServiceCollection();
        // UserProfileCacheService NOT registered
        context.RequestServices = services.BuildServiceProvider();

        // Act
        var result = await provider.DetermineProviderCultureResult(context);

        // Assert: missing cache service returns null
        result.ShouldBeNull();
    }
}
