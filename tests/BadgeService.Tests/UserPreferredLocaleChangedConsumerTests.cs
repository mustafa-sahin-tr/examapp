using BadgeService;
using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #185 — UserPreferredLocaleChangedConsumer: auth-api'nin kullanıcı kaydı ve
/// PUT /me/locale yollarında yazdığı UserPreferredLocaleChangedEvent'i BadgeService'te
/// tüketip UserLocalePreference tablosuna upsert eder. İdempotency: aynı UserId için
/// OutOfOrder teslimleri ChangedAtUtc ile korur.
/// </summary>
public class UserPreferredLocaleChangedConsumerTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private UserPreferredLocaleChangedConsumer NewConsumer()
        => new(_db.NewContext(), NullLogger<UserPreferredLocaleChangedConsumer>.Instance);

    private static UserPreferredLocaleChangedEvent Evt(
        int userId = 1,
        string keycloakId = "kc-user-1",
        string preferredLocale = "en",
        DateTime? changedAtUtc = null)
        => new()
        {
            UserId = userId,
            KeycloakId = keycloakId,
            PreferredLocale = preferredLocale,
            ChangedAtUtc = changedAtUtc ?? DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_FirstDelivery_InsertsNewUserLocalePreference()
    {
        var e = Evt(userId: 1, keycloakId: "kc-user-1", preferredLocale: "en");

        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        var pref = await check.UserLocalePreferences.SingleAsync();
        pref.UserId.ShouldBe(1);
        pref.KeycloakId.ShouldBe("kc-user-1");
        pref.Locale.ShouldBe("en");
        pref.UpdatedAtUtc.ShouldBe(e.ChangedAtUtc);
    }

    [Fact]
    public async Task Consume_NewerEventForExistingUser_UpdatesLocale()
    {
        var originalTime = DateTime.UtcNow.AddHours(-1);
        var newerTime = DateTime.UtcNow;

        // First event
        var e1 = Evt(userId: 1, preferredLocale: "tr", changedAtUtc: originalTime);
        await NewConsumer().Consume(Context(e1));

        // Second event (newer) for same user
        var e2 = Evt(userId: 1, preferredLocale: "en", changedAtUtc: newerTime);
        await NewConsumer().Consume(Context(e2));

        await using var check = _db.NewContext();
        var pref = await check.UserLocalePreferences.SingleAsync();
        pref.Locale.ShouldBe("en");
        pref.UpdatedAtUtc.ShouldBe(newerTime);
    }

    [Fact]
    public async Task Consume_OlderEventForExistingUser_IgnoresUpdate()
    {
        var olderTime = DateTime.UtcNow.AddHours(-2);
        var newerTime = DateTime.UtcNow.AddHours(-1);

        // First event (newer)
        var e1 = Evt(userId: 1, preferredLocale: "en", changedAtUtc: newerTime);
        await NewConsumer().Consume(Context(e1));

        // Second event (older) for same user — should be ignored
        var e2 = Evt(userId: 1, preferredLocale: "tr", changedAtUtc: olderTime);
        await NewConsumer().Consume(Context(e2));

        await using var check = _db.NewContext();
        var pref = await check.UserLocalePreferences.SingleAsync();
        pref.Locale.ShouldBe("en");
        pref.UpdatedAtUtc.ShouldBe(newerTime);
    }

    [Fact]
    public async Task Consume_SameEventTwice_IdempotentNoOp()
    {
        var e = Evt(userId: 1, preferredLocale: "en");

        await NewConsumer().Consume(Context(e));
        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.UserLocalePreferences.CountAsync(p => p.UserId == 1)).ShouldBe(1);
    }

    [Fact]
    public async Task Consume_MultipleUsers_CreatesMultiplePreferences()
    {
        var e1 = Evt(userId: 1, keycloakId: "kc-user-1", preferredLocale: "en");
        var e2 = Evt(userId: 2, keycloakId: "kc-user-2", preferredLocale: "tr");

        await NewConsumer().Consume(Context(e1));
        await NewConsumer().Consume(Context(e2));

        await using var check = _db.NewContext();
        (await check.UserLocalePreferences.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Consume_EmptyKeycloakId_StoresNull()
    {
        var e = Evt(userId: 1, keycloakId: "", preferredLocale: "en");

        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        var pref = await check.UserLocalePreferences.SingleAsync();
        pref.KeycloakId.ShouldBeNull();
    }

    [Fact]
    public async Task Consume_WhitespaceKeycloakId_StoresNull()
    {
        var e = Evt(userId: 1, keycloakId: "   ", preferredLocale: "en");

        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        var pref = await check.UserLocalePreferences.SingleAsync();
        pref.KeycloakId.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
