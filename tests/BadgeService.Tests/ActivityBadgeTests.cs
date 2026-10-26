using System.Security.Claims;
using System.Text.Json;
using BadgeService;
using BadgeService.Consumers;
using BadgeService.Controllers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Models;
using BadgeService.Security;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #572 (epic #569): ActivityCompletedEvent consumer'ı, etkinlik rozet kuralları (sayı / süre / seri),
/// idempotency, reset guard, puan zincirine dokunmama ve activity-summary rapor ucu.
/// </summary>
public class ActivityBadgeTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private readonly IHubContext<BadgeNotificationHub> _hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
    private static readonly DateTime Noon = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc); // 12:00 Istanbul

    public ActivityBadgeTests()
    {
        _hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private static int _instanceSeq;

    private static ActivityCompletedEvent Event(
        int userId = 1, int activityId = 10, int? instanceId = null, int seconds = 600, DateTime? at = null,
        int? subjectId = null, Guid? eventId = null, int items = 5, int? answered = null) => new()
    {
        EventId = eventId ?? Guid.NewGuid(),
        UserId = userId,
        StudentId = userId,
        ClientId = $"kc-{userId}",
        ActivityId = activityId,
        WorksheetInstanceId = instanceId ?? Interlocked.Increment(ref _instanceSeq),
        SubjectId = subjectId,
        ItemCount = items,
        AnsweredItemCount = answered ?? items,
        ActiveSeconds = seconds,
        CompletedAtUtc = at ?? Noon,
    };

    private async Task ConsumeAsync(ActivityCompletedEvent e)
    {
        await using var ctx = _db.NewContext();
        var consumer = new ActivityCompletedConsumer(
            ctx, BadgeEvaluatorFactory.Create(ctx, _hub), NullLogger<ActivityCompletedConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<ActivityCompletedEvent>>();
        context.Message.Returns(e);
        context.CancellationToken.Returns(CancellationToken.None);
        await consumer.Consume(context);
    }

    private async Task<BadgeDefinition> SeedBadgeAsync(string ruleType, string config, bool active = true, string? name = null)
    {
        await using var ctx = _db.NewContext();
        var b = new BadgeDefinition
        {
            Id = Guid.NewGuid(),
            Code = $"act-{Guid.NewGuid():N}",
            Name = name ?? "Etkinlik Rozeti",
            Description = "d",
            Category = "Etkinlik",
            RuleType = ruleType,
            RuleConfigJson = config,
            Icon = "explore",
            IsActive = active,
        };
        ctx.BadgeDefinitions.Add(b);
        await ctx.SaveChangesAsync();
        return b;
    }

    private async Task<List<StudentBadgeEarnedEvent>> BadgeOutboxAsync()
    {
        await using var ctx = _db.NewContext();
        var name = OutboxEventRegistry.NameFor<StudentBadgeEarnedEvent>();
        return (await ctx.OutboxMessages.Where(m => m.Type == name).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<StudentBadgeEarnedEvent>(m.Content)!).ToList();
    }

    private async Task<int> EarnedAsync(int userId = 1)
    {
        await using var ctx = _db.NewContext();
        return await ctx.BadgeEarned.CountAsync(x => x.UserId == userId);
    }

    // ---------- consumer: idempotency ----------

    [Fact]
    public async Task First_delivery_writes_ledger_row_with_istanbul_date_and_clamped_seconds()
    {
        // 21:30Z = 00:30 ertesi gün İstanbul
        await ConsumeAsync(Event(seconds: 99_999, at: new DateTime(2026, 10, 20, 21, 30, 0, DateTimeKind.Utc)));

        await using var ctx = _db.NewContext();
        var row = await ctx.StudentActivityCompletions.SingleAsync();
        row.ActiveSeconds.ShouldBe(1800);
        row.ActivityDate.ShouldBe(new DateTime(2026, 10, 21));
    }

    [Fact]
    public async Task Same_EventId_twice_is_a_no_op_second_time_and_earns_badge_once()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");
        var e = Event();

        await ConsumeAsync(e);
        await ConsumeAsync(e);

        await using var ctx = _db.NewContext();
        (await ctx.StudentActivityCompletions.CountAsync()).ShouldBe(1);
        (await EarnedAsync()).ShouldBe(1);
        (await BadgeOutboxAsync()).Count.ShouldBe(1);
        (await ctx.Notifications.CountAsync(n => n.Type == "BadgeEarned")).ShouldBe(1);
    }

    [Fact]
    public async Task Same_user_and_instance_with_different_EventId_is_deduplicated()
    {
        await ConsumeAsync(Event(instanceId: 500));
        await ConsumeAsync(Event(instanceId: 500));

        await using var ctx = _db.NewContext();
        (await ctx.StudentActivityCompletions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Same_instance_id_for_a_different_user_is_not_a_duplicate()
    {
        await ConsumeAsync(Event(userId: 1, instanceId: 600));
        await ConsumeAsync(Event(userId: 2, instanceId: 600));

        await using var ctx = _db.NewContext();
        (await ctx.StudentActivityCompletions.CountAsync()).ShouldBe(2);
    }

    // ---------- rules ----------

    [Fact]
    public async Task Resolving_the_same_activity_does_not_increase_the_count_badge()
    {
        var badge = await SeedBadgeAsync("CompletedActivityCount", """{"target":2}""");

        await ConsumeAsync(Event(activityId: 10));
        await ConsumeAsync(Event(activityId: 10)); // yeni deneme, aynı etkinlik
        await ConsumeAsync(Event(activityId: 10));

        await using (var ctx = _db.NewContext())
        {
            (await ctx.StudentActivityCompletions.CountAsync()).ShouldBe(3);
            var p = await ctx.StudentBadgeProgresses.SingleAsync(x => x.BadgeDefinitionId == badge.Id);
            p.CurrentValue.ShouldBe(1);
            p.IsCompleted.ShouldBeFalse();
        }
        (await EarnedAsync()).ShouldBe(0);

        await ConsumeAsync(Event(activityId: 11));
        (await EarnedAsync()).ShouldBe(1);
        var events = await BadgeOutboxAsync();
        events.Count.ShouldBe(1);
        events[0].UserId.ShouldBe(1);
        events[0].BadgeDefinitionId.ShouldBe(badge.Id);

        await ConsumeAsync(Event(activityId: 12)); // eşik aşıldıktan sonra tekrar ödül yok
        (await EarnedAsync()).ShouldBe(1);
        (await BadgeOutboxAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Study_time_badge_sums_active_seconds_of_all_attempts_and_awards_at_threshold()
    {
        await SeedBadgeAsync("ActivityStudyTimeMinutes", """{"target":60}""");

        await ConsumeAsync(Event(activityId: 1, seconds: 1800));
        await ConsumeAsync(Event(activityId: 1, seconds: 1799)); // 59.98 dk -> 59
        (await EarnedAsync()).ShouldBe(0);

        await ConsumeAsync(Event(activityId: 1, seconds: 60));
        (await EarnedAsync()).ShouldBe(1);
        (await BadgeOutboxAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Study_time_per_activity_is_capped_defensively_even_if_producer_sends_more()
    {
        await SeedBadgeAsync("ActivityStudyTimeMinutes", """{"target":31}""");

        await ConsumeAsync(Event(seconds: 100_000)); // 1800 sn = 30 dk
        (await EarnedAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Day_streak_uses_istanbul_local_days_not_utc_days()
    {
        await SeedBadgeAsync("ActivityDayStreak", """{"target":2}""");

        // 20 Ekim 23:30 İstanbul (20:30Z) ve 21 Ekim 00:30 İstanbul (21:30Z) -> UTC'de aynı gün, İstanbul'da ardışık 2 gün
        await ConsumeAsync(Event(activityId: 1, at: new DateTime(2026, 10, 20, 20, 30, 0, DateTimeKind.Utc)));
        (await EarnedAsync()).ShouldBe(0);
        await ConsumeAsync(Event(activityId: 2, at: new DateTime(2026, 10, 20, 21, 30, 0, DateTimeKind.Utc)));

        (await EarnedAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Day_streak_does_not_count_two_completions_on_the_same_istanbul_day_as_two_days()
    {
        await SeedBadgeAsync("ActivityDayStreak", """{"target":2}""");

        // 20 Ekim 03:00Z (06:00 İst) ve 20 Ekim 20:30Z (23:30 İst): aynı İstanbul günü
        await ConsumeAsync(Event(activityId: 1, at: new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc)));
        await ConsumeAsync(Event(activityId: 2, at: new DateTime(2026, 10, 20, 20, 30, 0, DateTimeKind.Utc)));

        (await EarnedAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Day_streak_is_broken_by_a_gap_day()
    {
        await SeedBadgeAsync("ActivityDayStreak", """{"target":3}""");

        await ConsumeAsync(Event(activityId: 1, at: Noon));
        await ConsumeAsync(Event(activityId: 2, at: Noon.AddDays(1)));
        await ConsumeAsync(Event(activityId: 3, at: Noon.AddDays(3))); // 2 gün boşluk

        (await EarnedAsync()).ShouldBe(0);

        await ConsumeAsync(Event(activityId: 4, at: Noon.AddDays(4)));
        await ConsumeAsync(Event(activityId: 5, at: Noon.AddDays(5)));
        (await EarnedAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Subject_filter_only_counts_matching_subject()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":2,"subjectId":7}""");

        await ConsumeAsync(Event(activityId: 1, subjectId: 7));
        await ConsumeAsync(Event(activityId: 2, subjectId: 8));
        await ConsumeAsync(Event(activityId: 3, subjectId: null));
        (await EarnedAsync()).ShouldBe(0);

        await ConsumeAsync(Event(activityId: 4, subjectId: 7));
        (await EarnedAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Inactive_activity_badge_is_not_awarded()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""", active: false);
        await ConsumeAsync(Event());
        (await EarnedAsync()).ShouldBe(0);
    }

    // ---------- puansız: puan/aggregate zincirine dokunmaz ----------

    [Fact]
    public async Task Consumer_never_touches_points_xp_daily_activity_or_aggregates()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");

        await ConsumeAsync(Event(subjectId: 3));

        await using var ctx = _db.NewContext();
        (await ctx.StudentQuestionAggregates.CountAsync()).ShouldBe(0);
        (await ctx.StudentSubjectAggregates.CountAsync()).ShouldBe(0);
        (await ctx.StudentDailyActivities.CountAsync()).ShouldBe(0);
        (await ctx.ProcessedAnswerSubmissions.CountAsync()).ShouldBe(0);
        (await ctx.AnswerPointAwards.CountAsync()).ShouldBe(0);
        var pointsName = OutboxEventRegistry.NameFor<StudentPointsChangedEvent>();
        (await ctx.OutboxMessages.CountAsync(m => m.Type == pointsName)).ShouldBe(0);
        // yalnız rozet event'i yazıldı
        (await ctx.OutboxMessages.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task AnswerSubmitted_evaluation_never_awards_or_tracks_activity_badges()
    {
        var badge = await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");
        await using (var seed = _db.NewContext())
        {
            seed.StudentActivityCompletions.Add(new StudentActivityCompletion
            {
                Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = 1, ActivityId = 1, WorksheetInstanceId = 1,
                ActiveSeconds = 60, CompletedAtUtc = Noon, ActivityDate = new DateTime(2026, 10, 20), CountsTowardBadges = true,
            });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await BadgeEvaluatorFactory.Create(ctx, _hub).EvaluateAnswerSubmittedAsync(1, "kc-1");

        await using var read = _db.NewContext();
        (await read.BadgeEarned.CountAsync()).ShouldBe(0);
        (await read.StudentBadgeProgresses.CountAsync(x => x.BadgeDefinitionId == badge.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Activity_evaluation_never_awards_question_based_badges()
    {
        await SeedBadgeAsync("AnswerCount", """{"target":1}""");
        await using (var seed = _db.NewContext())
        {
            seed.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 50 });
            await seed.SaveChangesAsync();
        }

        await ConsumeAsync(Event());

        (await EarnedAsync()).ShouldBe(0);
    }

    // ---------- reset ----------

    [Fact]
    public async Task Event_completed_before_user_reset_is_ignored()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");
        await using (var seed = _db.NewContext())
        {
            seed.UserResetMarkers.Add(new UserResetMarker { UserId = 1, ResetAtUtc = Noon.AddHours(1) });
            await seed.SaveChangesAsync();
        }

        await ConsumeAsync(Event(at: Noon));

        await using var ctx = _db.NewContext();
        (await ctx.StudentActivityCompletions.CountAsync()).ShouldBe(0);
        (await EarnedAsync()).ShouldBe(0);

        await ConsumeAsync(Event(at: Noon.AddHours(2))); // reset sonrası
        (await EarnedAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task UserResetService_deletes_activity_completions_for_that_user_only()
    {
        await ConsumeAsync(Event(userId: 1));
        await ConsumeAsync(Event(userId: 2));

        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1);

        await using var read = _db.NewContext();
        (await read.StudentActivityCompletions.Select(x => x.UserId).ToListAsync()).ShouldBe(new[] { 2 });
    }

    [Fact]
    public async Task Invalid_message_is_skipped_without_writing()
    {
        await ConsumeAsync(Event(userId: 0));
        await using var ctx = _db.NewContext();
        (await ctx.StudentActivityCompletions.CountAsync()).ShouldBe(0);
    }

    // ---------- katalog ----------

    [Theory]
    [InlineData("CompletedActivityCount")]
    [InlineData("ActivityStudyTimeMinutes")]
    [InlineData("ActivityDayStreak")]
    public void Catalog_accepts_activity_rule_types_with_and_without_subject(string ruleType)
    {
        BadgeRuleTypeCatalog.TryValidateAndNormalize(ruleType, """{"target":10}""", out var n1, out var e1).ShouldBeTrue();
        e1.ShouldBeEmpty();
        n1.ShouldBe("""{"target":10}""");

        BadgeRuleTypeCatalog.TryValidateAndNormalize(ruleType, """{"target":10,"subjectId":3}""", out var n2, out _).ShouldBeTrue();
        n2.ShouldBe("""{"target":10,"subjectId":3}""");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"target":0}""")]
    [InlineData("""{"target":5,"subjectId":0}""")]
    [InlineData("""{"target":5,"subjectName":"Matematik"}""")]
    [InlineData("""{"target":5,"unknown":1}""")]
    public void Catalog_rejects_invalid_activity_rule_configs(string json)
    {
        BadgeRuleTypeCatalog.TryValidateAndNormalize("CompletedActivityCount", json, out _, out var errors).ShouldBeFalse();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Legacy_ActivityStreak_alias_is_not_an_offered_type()
    {
        BadgeRuleTypeCatalog.TryGetSchema("ActivityStreak", out _).ShouldBeFalse();
        BadgeRuleTypeCatalog.TryGetSchema("ActivityDayStreak", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Seeded_activity_badges_pass_catalog_validation_and_are_idempotent()
    {
        await using (var ctx = _db.NewContext()) await BadgeService.Data.BadgeSeeder.SeedAsync(ctx);
        await using (var ctx = _db.NewContext()) await BadgeService.Data.BadgeSeeder.SeedAsync(ctx);

        await using var read = _db.NewContext();
        var seeded = await read.BadgeDefinitions.Where(x => x.Code.StartsWith("activity-")).ToListAsync();
        seeded.Count.ShouldBe(6);
        foreach (var b in seeded)
        {
            BadgeRuleTypeCatalog.TryValidateAndNormalize(b.RuleType, b.RuleConfigJson, out _, out var errors).ShouldBeTrue(b.Code);
            errors.ShouldBeEmpty();
            BadgeIconValidator.IsAllowedIcon(b.Icon).ShouldBeTrue(b.Code);
        }
        seeded.Where(b => b.RuleType == "CompletedActivityCount").Select(b => b.RuleConfigJson).OrderBy(x => x)
            .ShouldBe(new[] { """{"target":10}""", """{"target":100}""", """{"target":50}""" }.OrderBy(x => x));
    }

    // ---------- rapor ucu ----------

    private sealed class FixedResolver(int? userId) : ICallerIdentityResolver
    {
        public Task<int?> ResolveUserIdAsync(HttpContext httpContext, CancellationToken cancellationToken) => Task.FromResult(userId);
    }

    private ReportsController Controller(int? callerUserId, bool admin = false)
    {
        var ctx = _db.NewContext();
        var controller = new ReportsController(new StudentReportService(ctx), new FixedResolver(callerUserId), NullLogger<ReportsController>.Instance);
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "sub"));
        if (admin) identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    [Fact]
    public async Task Activity_summary_other_users_data_is_403_own_and_admin_are_200()
    {
        (await Controller(5).GetActivitySummaryAsync(6, CancellationToken.None)).Result.ShouldBeOfType<ForbidResult>();
        (await Controller(null).GetActivitySummaryAsync(6, CancellationToken.None)).Result.ShouldBeOfType<ForbidResult>();
        (await Controller(5).GetActivitySummaryAsync(5, CancellationToken.None)).Result.ShouldBeOfType<OkObjectResult>();
        (await Controller(null, admin: true).GetActivitySummaryAsync(6, CancellationToken.None)).Result.ShouldBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Activity_summary_today_is_istanbul_day_and_badges_are_the_three_closest_unearned()
    {
        // now = 20 Ekim 21:30Z => İstanbul 21 Ekim 00:30
        var now = new DateTime(2026, 10, 20, 21, 30, 0, DateTimeKind.Utc);
        var b10 = await SeedBadgeAsync("CompletedActivityCount", """{"target":10}""", name: "Kâşif");
        await SeedBadgeAsync("CompletedActivityCount", """{"target":50}""", name: "Gezgin");
        await SeedBadgeAsync("CompletedActivityCount", """{"target":100}""", name: "Usta Kâşif");
        await SeedBadgeAsync("ActivityStudyTimeMinutes", """{"target":20}""", name: "Süre");
        await SeedBadgeAsync("ActivityDayStreak", """{"target":7}""", name: "Seri");
        await SeedBadgeAsync("AnswerCount", """{"target":1}""", name: "Soru rozeti (dahil edilmemeli)");

        // dün (20 Ekim İstanbul) 2 etkinlik, bugün (21 Ekim İstanbul) 2 etkinlik x 7 dk
        await using (var seed = _db.NewContext())
        {
            void Add(int act, int secs, DateTime utc) => seed.StudentActivityCompletions.Add(new StudentActivityCompletion
            {
                Id = Guid.NewGuid(), EventId = Guid.NewGuid(), UserId = 1, ActivityId = act, WorksheetInstanceId = act * 100 + secs,
                ActiveSeconds = secs, CompletedAtUtc = utc, ActivityDate = ActivityCompletionStats.ToIstanbulDate(utc), CountsTowardBadges = true,
            });
            Add(1, 60, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));
            Add(2, 60, new DateTime(2026, 10, 20, 11, 0, 0, DateTimeKind.Utc));
            Add(3, 420, new DateTime(2026, 10, 20, 21, 10, 0, DateTimeKind.Utc));
            Add(4, 420, new DateTime(2026, 10, 20, 21, 20, 0, DateTimeKind.Utc));
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var summary = await new StudentReportService(ctx).GetActivitySummaryAsync(1, now);

        summary.Today.ActivityCount.ShouldBe(2);
        summary.Today.ActiveMinutes.ShouldBe(14);
        summary.Today.StreakDays.ShouldBe(2);

        // oranlar: Kâşif 4/10=.4, Süre 16/20=.8, Gezgin 4/50, Usta 4/100, Seri 2/7=.29 -> Süre, Kâşif, Seri
        summary.Badges.Select(b => b.Name).ShouldBe(new[] { "Süre", "Kâşif", "Seri" });
        summary.Badges[0].Current.ShouldBe(16);
        summary.Badges[0].Target.ShouldBe(20);
        summary.Badges.ShouldAllBe(b => !b.Earned);
        b10.Code.ShouldBe(summary.Badges[1].Code);
    }

    [Fact]
    public async Task Activity_summary_has_no_badges_when_all_are_earned_and_excludes_earned_ones()
    {
        var a = await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""", name: "A");
        var b = await SeedBadgeAsync("CompletedActivityCount", """{"target":5}""", name: "B");
        await ConsumeAsync(Event());

        await using (var ctx = _db.NewContext())
        {
            var s = await new StudentReportService(ctx).GetActivitySummaryAsync(1, Noon);
            s.Badges.Select(x => x.Name).ShouldBe(new[] { "B" });
        }

        await using (var seed = _db.NewContext())
        {
            seed.BadgeEarned.Add(new BadgeEarned { Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = b.Id, EarnedDate = Noon });
            await seed.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        (await new StudentReportService(ctx2).GetActivitySummaryAsync(1, Noon)).Badges.ShouldBeEmpty();
        a.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Activity_summary_for_a_user_without_activity_is_all_zero()
    {
        await using var ctx = _db.NewContext();
        var s = await new StudentReportService(ctx).GetActivitySummaryAsync(99, Noon);
        s.Today.ActivityCount.ShouldBe(0);
        s.Today.ActiveMinutes.ShouldBe(0);
        s.Today.StreakDays.ShouldBe(0);
        s.Badges.ShouldBeEmpty();
    }

    [Fact]
    public async Task Badge_progress_report_for_activity_only_student_includes_activity_badges_and_progress()
    {
        var badge = await SeedBadgeAsync("CompletedActivityCount", """{"target":2}""", name: "Kâşif");
        await ConsumeAsync(Event(activityId: 1));

        await using var ctx = _db.NewContext();
        (await ctx.StudentQuestionAggregates.CountAsync(x => x.UserId == 1)).ShouldBe(0);
        var report = await new StudentReportService(ctx).GetBadgeProgressAsync(1);

        var item = report.BadgeProgress.ShouldHaveSingleItem();
        item.BadgeDefinitionId.ShouldBe(badge.Id);
        item.CurrentValue.ShouldBe(1);
        item.TargetValue.ShouldBe(2);
        report.Summary.TotalQuestions.ShouldBe(0);
        report.Summary.UserId.ShouldBe(1);
    }

    [Fact]
    public async Task Badge_progress_report_for_user_with_nothing_lists_activity_badges_at_zero()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":2}""", name: "Kâşif");

        await using var ctx = _db.NewContext();
        var report = await new StudentReportService(ctx).GetBadgeProgressAsync(77);

        var item = report.BadgeProgress.ShouldHaveSingleItem();
        item.CurrentValue.ShouldBe(0);
        item.TargetValue.ShouldBe(2);
    }

    [Fact]
    public async Task Badge_progress_report_shows_live_streak_not_the_stale_stored_row()
    {
        var badge = await SeedBadgeAsync("ActivityDayStreak", """{"target":7}""", name: "Seri");
        // 5 günlük seri, bugünden 10 gün önce bitti: saklı ilerleme 5, canlı değer 0 olmalı.
        var start = DateTime.UtcNow.AddDays(-14);
        for (var d = 0; d < 5; d++)
            await ConsumeAsync(Event(activityId: d + 1, at: start.AddDays(d)));
        await using (var chk = _db.NewContext())
            (await chk.StudentBadgeProgresses.SingleAsync(x => x.BadgeDefinitionId == badge.Id)).CurrentValue.ShouldBe(5);

        await using var ctx = _db.NewContext();
        var item = (await new StudentReportService(ctx).GetBadgeProgressAsync(1)).BadgeProgress.Single(x => x.BadgeDefinitionId == badge.Id);
        item.CurrentValue.ShouldBe(0);
        item.TargetValue.ShouldBe(7);
    }

    [Fact]
    public async Task Activity_summary_skips_unearned_badges_whose_threshold_is_already_met()
    {
        var b = await SeedBadgeAsync("CompletedActivityCount", """{"target":2}""", name: "A");
        await ConsumeAsync(Event(activityId: 1));
        await ConsumeAsync(Event(activityId: 2)); // kazanıldı
        await using (var ctx = _db.NewContext())
        {
            // kazanım satırını sil + eşiği düşür: "ödüllenmemiş ama eşik geçilmiş" durumu
            ctx.BadgeEarned.RemoveRange(ctx.BadgeEarned);
            (await ctx.BadgeDefinitions.SingleAsync(x => x.Id == b.Id)).RuleConfigJson = """{"target":1}""";
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await new StudentReportService(read).GetActivitySummaryAsync(1, Noon)).Badges.ShouldBeEmpty();
    }

    [Fact]
    public void Catalog_caps_the_activity_day_streak_target_at_the_streak_window()
    {
        BadgeRuleTypeCatalog.TryValidateAndNormalize("ActivityDayStreak", $$"""{"target":{{ActivityCompletionStats.MaxStreakTarget}}}""", out _, out _).ShouldBeTrue();
        BadgeRuleTypeCatalog.TryValidateAndNormalize("ActivityDayStreak", $$"""{"target":{{ActivityCompletionStats.MaxStreakTarget + 1}}}""", out _, out var errors).ShouldBeFalse();
        errors.ShouldContain(e => e.Field == "target");
    }

    [Theory]
    [InlineData(5, 3, true)]   // ceil(5/2)=3
    [InlineData(5, 2, false)]
    [InlineData(4, 2, true)]   // yarısı
    [InlineData(4, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 0, false)]  // tamamen boş
    [InlineData(0, 0, false)]  // max(1, ..) -> en az 1 cevap
    public void Qualification_threshold_is_at_least_half_and_at_least_one(int items, int answered, bool expected) =>
        ActivityCompletionStats.QualifiesForBadges(items, answered).ShouldBe(expected);

    [Fact]
    public async Task Half_answered_completion_counts_for_count_and_streak_and_persists_the_flag()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");
        await ConsumeAsync(Event(items: 6, answered: 3));

        await using var ctx = _db.NewContext();
        var row = await ctx.StudentActivityCompletions.SingleAsync();
        row.CountsTowardBadges.ShouldBeTrue();
        row.ItemCount.ShouldBe(6);
        row.AnsweredItemCount.ShouldBe(3);
        (await EarnedAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Less_than_half_answered_adds_minutes_but_not_count_streak_or_today()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");
        await SeedBadgeAsync("ActivityDayStreak", """{"target":1}""");
        await SeedBadgeAsync("ActivityStudyTimeMinutes", """{"target":10}""");

        await ConsumeAsync(Event(items: 6, answered: 2, seconds: 600)); // 2 < 3

        (await EarnedAsync()).ShouldBe(1); // yalnız süre rozeti
        await using var ctx = _db.NewContext();
        (await ctx.StudentActivityCompletions.SingleAsync()).CountsTowardBadges.ShouldBeFalse();
        var s = await new StudentReportService(ctx).GetActivitySummaryAsync(1, Noon);
        s.Today.ActivityCount.ShouldBe(0);
        s.Today.StreakDays.ShouldBe(0);
        s.Today.ActiveMinutes.ShouldBe(10);
    }

    [Fact]
    public async Task All_blank_completion_does_not_count_but_time_does()
    {
        await SeedBadgeAsync("CompletedActivityCount", """{"target":1}""");
        await SeedBadgeAsync("ActivityStudyTimeMinutes", """{"target":1}""");

        await ConsumeAsync(Event(items: 4, answered: 0, seconds: 120));

        await using var ctx = _db.NewContext();
        var earned = await ctx.BadgeEarned.Include(b => b.BadgeDefinition).Select(b => b.BadgeDefinition.RuleType).ToListAsync();
        earned.ShouldBe(new[] { "ActivityStudyTimeMinutes" });
    }

    [Fact]
    public async Task Non_qualifying_day_does_not_extend_the_streak()
    {
        await SeedBadgeAsync("ActivityDayStreak", """{"target":2}""");
        await ConsumeAsync(Event(activityId: 1, at: Noon));
        await ConsumeAsync(Event(activityId: 2, at: Noon.AddDays(1), items: 4, answered: 1)); // nitelikli değil
        (await EarnedAsync()).ShouldBe(0);
        await ConsumeAsync(Event(activityId: 3, at: Noon.AddDays(1), items: 4, answered: 2));
        (await EarnedAsync()).ShouldBe(1);
    }
}
