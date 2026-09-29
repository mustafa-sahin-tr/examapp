using System.Security.Claims;
using System.Text.Json;
using BadgeService.Controllers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

/// <summary>
/// Issue #146: rozet kazanımı, BadgeEarned satırıyla aynı SaveChanges'te kalıcı bir
/// <see cref="Notification"/> üretir (SignalR push'a ek olarak). Idempotency anahtarı:
/// (UserId, SourceBadgeDefinitionId) filtreli unique index + earnedBadgeIds kontrolü.
/// </summary>
public class BadgeEarnedNotificationTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private readonly IHubContext<BadgeNotificationHub> _hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
    private readonly IClientProxy _proxy = Substitute.For<IClientProxy>();

    public BadgeEarnedNotificationTests()
    {
        _hub.Clients.User(Arg.Any<string>()).Returns(_proxy);
    }

    private static BadgeDefinition Badge(string name, int target = 1) => new()
    {
        Id = Guid.NewGuid(),
        Code = $"code-{Guid.NewGuid():N}",
        Name = name,
        Description = "d",
        Category = "c",
        IconUrl = "achievements/x.svg",
        RuleType = "AnswerCount",
        RuleConfigJson = $"{{\"target\": {target}}}",
        IsActive = true,
    };

    private async Task SeedAsync(int userId, int totalQuestions, params BadgeDefinition[] badges)
    {
        await using var ctx = _db.NewContext();
        ctx.BadgeDefinitions.AddRange(badges);
        ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
        { Id = Guid.NewGuid(), UserId = userId, TotalQuestions = totalQuestions });
        await ctx.SaveChangesAsync();
    }

    private async Task EvaluateAsync(int userId, string clientId)
    {
        await using var ctx = _db.NewContext();
        await BadgeEvaluatorFactory.Create(ctx, _hub).EvaluateAnswerSubmittedAsync(userId, clientId);
    }

    private async Task<List<Notification>> NotificationsAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task New_badge_creates_a_persistent_notification_with_expected_fields()
    {
        var badge = Badge("İlk Adım");
        await SeedAsync(1, 5, badge);

        await EvaluateAsync(1, "kc-1");

        var n = (await NotificationsAsync(1)).ShouldHaveSingleItem();
        n.Type.ShouldBe("BadgeEarned");
        n.UserKeycloakId.ShouldBe("kc-1");
        n.SourceBadgeDefinitionId.ShouldBe(badge.Id);
        n.IsRead.ShouldBeFalse();
        n.Title.ShouldBe("Yeni rozet kazandın!"); // varsayılan locale: tr
        n.Body.ShouldContain("İlk Adım");

        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("badgeDefinitionId").GetGuid().ShouldBe(badge.Id);
        data.RootElement.GetProperty("badgeCode").GetString().ShouldBe(badge.Code);
        data.RootElement.GetProperty("iconUrl").GetString().ShouldBe("achievements/x.svg");
    }

    [Theory]
    [InlineData("https://evil.example/pixel.png")]
    [InlineData("../secret.svg")]
    public async Task Invalid_icon_url_is_written_as_null_in_notification_data(string icon)
    {
        var badge = Badge("B");
        badge.IconUrl = icon;
        await SeedAsync(1, 5, badge);

        await EvaluateAsync(1, "kc-1");

        var n = (await NotificationsAsync(1)).ShouldHaveSingleItem();
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("iconUrl").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Blank_clientId_earns_the_badge_but_writes_no_notification(string clientId)
    {
        await SeedAsync(1, 5, Badge("B"));

        await EvaluateAsync(1, clientId);

        (await NotificationsAsync(1)).ShouldBeEmpty();
        await using var ctx = _db.NewContext();
        (await ctx.BadgeEarned.CountAsync(x => x.UserId == 1)).ShouldBe(1);
    }

    [Fact]
    public async Task Notification_uses_the_users_preferred_locale()
    {
        var badge = Badge("First Step");
        await SeedAsync(2, 5, badge);
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            { UserId = 2, KeycloakId = "kc-2", Locale = "en", UpdatedAtUtc = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await EvaluateAsync(2, "kc-2");

        var n = (await NotificationsAsync(2)).ShouldHaveSingleItem();
        n.Title.ShouldBe("You earned a new badge!");
        n.Body.ShouldContain("First Step");
    }

    [Fact]
    public async Task SignalR_push_still_happens_in_addition_to_the_notification()
    {
        await SeedAsync(1, 5, Badge("B"));

        await EvaluateAsync(1, "kc-1");

        _hub.Clients.Received(1).User("kc-1");
        await _proxy.Received(1).SendCoreAsync("BadgeEarned", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        (await NotificationsAsync(1)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Re_evaluation_retry_does_not_create_a_second_notification()
    {
        await SeedAsync(1, 5, Badge("B"));

        for (var i = 0; i < 3; i++)
            await EvaluateAsync(1, "kc-1");

        (await NotificationsAsync(1)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Answer_that_earns_nothing_creates_no_notification()
    {
        await SeedAsync(1, 3, Badge("Hard", target: 100));

        await EvaluateAsync(1, "kc-1");

        (await NotificationsAsync(1)).ShouldBeEmpty();
        await _proxy.DidNotReceiveWithAnyArgs().SendCoreAsync(default!, default!, default);
    }

    [Fact]
    public async Task Multiple_badges_earned_at_once_create_one_notification_each()
    {
        var a = Badge("A");
        var b = Badge("B");
        await SeedAsync(1, 5, a, b);

        await EvaluateAsync(1, "kc-1");

        var list = await NotificationsAsync(1);
        list.Count.ShouldBe(2);
        list.Select(x => x.SourceBadgeDefinitionId).ToHashSet().ShouldBe(new Guid?[] { a.Id, b.Id }.ToHashSet());
    }

    [Fact]
    public async Task Existing_notification_for_a_badge_is_not_duplicated_when_the_badge_is_re_earned()
    {
        // BadgeEarned elle silinmiş (reset dışı) ama bildirim kalmış: unique index'e çarpıp tüm
        // SaveChanges'i düşürmek yerine bildirim atlanır.
        var badge = Badge("B");
        await SeedAsync(1, 5, badge);
        await EvaluateAsync(1, "kc-1");
        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeEarned.RemoveRange(ctx.BadgeEarned);
            await ctx.SaveChangesAsync();
        }

        await EvaluateAsync(1, "kc-1");

        (await NotificationsAsync(1)).Count.ShouldBe(1);
        await using var check = _db.NewContext();
        (await check.BadgeEarned.CountAsync(x => x.UserId == 1)).ShouldBe(1);
    }

    [Fact]
    public async Task User_reset_removes_badge_notifications_so_a_re_earned_badge_notifies_again()
    {
        await SeedAsync(1, 5, Badge("B"));
        await EvaluateAsync(1, "kc-1");

        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1);
        (await NotificationsAsync(1)).ShouldBeEmpty();

        await using (var ctx = _db.NewContext())
        {
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
            { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 5 });
            await ctx.SaveChangesAsync();
        }
        await EvaluateAsync(1, "kc-1");

        (await NotificationsAsync(1)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unique_index_rejects_a_second_BadgeEarned_notification_for_the_same_user_and_badge()
    {
        var id = Guid.NewGuid();
        await using var ctx = _db.NewContext();
        ctx.Notifications.Add(new Notification { UserId = 1, Type = "BadgeEarned", SourceBadgeDefinitionId = id });
        ctx.Notifications.Add(new Notification { UserId = 1, Type = "BadgeEarned", SourceBadgeDefinitionId = id });
        await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    private NotificationsController ControllerFor(BadgeDbContext ctx, string sub) => new(ctx)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, sub) }, "test"))
            }
        }
    };

    [Fact]
    public async Task Notifications_api_returns_the_badge_notification_to_its_owner_only()
    {
        var badge = Badge("B");
        await SeedAsync(1, 5, badge);
        await EvaluateAsync(1, "kc-owner");

        await using var ctx = _db.NewContext();

        var owner = (await ControllerFor(ctx, "kc-owner").GetMineAsync(unreadOnly: true)).Result
            .ShouldBeOfType<OkObjectResult>().Value
            .ShouldBeAssignableTo<IReadOnlyList<NotificationsController.NotificationDto>>()!;
        var dto = owner.ShouldHaveSingleItem();
        dto.Type.ShouldBe("BadgeEarned");
        dto.IsRead.ShouldBeFalse();

        var other = (await ControllerFor(ctx, "kc-other").GetMineAsync(unreadOnly: false)).Result
            .ShouldBeOfType<OkObjectResult>().Value
            .ShouldBeAssignableTo<IReadOnlyList<NotificationsController.NotificationDto>>()!;
        other.ShouldBeEmpty();

        // Başkasının bildirimini okundu işaretleyemez (404, varlık sızdırmaz).
        (await ControllerFor(ctx, "kc-other").MarkReadAsync(dto.Id, default)).ShouldBeOfType<NotFoundResult>();
        (await ControllerFor(ctx, "kc-owner").MarkReadAsync(dto.Id, default)).ShouldBeOfType<NoContentResult>();
    }

    public void Dispose() => _db.Dispose();
}
