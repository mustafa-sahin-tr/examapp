using System.Globalization;
using BadgeService;
using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

/// <summary>
/// Issue #185 — UserLocaleResolver: UserLocalePreference tablosundan hedef kullanıcının
/// dil tercihini okur ve CultureInfo'ya dönüştürür. Kayıt yoksa varsayılan dile düşer.
/// </summary>
public class UserLocaleResolverTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private UserLocaleResolver NewResolver() => new(_db.NewContext());

    [Fact]
    public async Task ResolveAsync_WithUserId_FindsPreferenceAndReturnsCulture()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 42,
                KeycloakId = "kc-user",
                Locale = "en",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var culture = await NewResolver().ResolveAsync(42, null);

        culture.Name.ShouldBe("en-US");
    }

    [Fact]
    public async Task ResolveAsync_WithTurkishLocale_ReturnsTrTRCulture()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 43,
                KeycloakId = "kc-user-tr",
                Locale = "tr",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var culture = await NewResolver().ResolveAsync(43, null);

        culture.Name.ShouldBe("tr-TR");
    }

    [Fact]
    public async Task ResolveAsync_UserNotFound_ReturnsDefaultCulture()
    {
        var culture = await NewResolver().ResolveAsync(999, null);

        culture.Name.ShouldBe(SupportedLocales.DefaultCultureName);
    }

    [Fact]
    public async Task ResolveAsync_WithKeycloakIdFallback_FindsPreference()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 44,
                KeycloakId = "kc-fallback-user",
                Locale = "en",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        // Resolve by KeycloakId when UserId is 0 or not found
        var culture = await NewResolver().ResolveAsync(0, "kc-fallback-user");

        culture.Name.ShouldBe("en-US");
    }

    [Fact]
    public async Task ResolveAsync_UserIdTakesPrecedenceOverKeycloakId()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 45,
                KeycloakId = "kc-user-id-45",
                Locale = "en",
                UpdatedAtUtc = DateTime.UtcNow
            });
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 46,
                KeycloakId = "kc-fallback-user",
                Locale = "tr",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        // Should find by UserId first, not KeycloakId
        var culture = await NewResolver().ResolveAsync(45, "kc-fallback-user");

        culture.Name.ShouldBe("en-US");
    }

    [Fact]
    public async Task ResolveAsync_NegativeUserId_FallsBackToKeycloakId()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 47,
                KeycloakId = "kc-negative-test",
                Locale = "en",
                UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        // Negative UserId should be treated as "not set" and fall back to KeycloakId
        var culture = await NewResolver().ResolveAsync(-1, "kc-negative-test");

        culture.Name.ShouldBe("en-US");
    }

    [Fact]
    public async Task ResolveAsync_BothNotFound_ReturnsDefault()
    {
        var culture = await NewResolver().ResolveAsync(999, "kc-nonexistent");

        culture.Name.ShouldBe(SupportedLocales.DefaultCultureName);
    }

    [Fact]
    public async Task ResolveAsync_EmptyKeycloakId_IgnoredInFallback()
    {
        var culture = await NewResolver().ResolveAsync(999, "");

        culture.Name.ShouldBe(SupportedLocales.DefaultCultureName);
    }

    [Fact]
    public async Task ResolveAsync_WhitespaceKeycloakId_IgnoredInFallback()
    {
        var culture = await NewResolver().ResolveAsync(999, "   ");

        culture.Name.ShouldBe(SupportedLocales.DefaultCultureName);
    }

    public void Dispose() => _db.Dispose();
}
