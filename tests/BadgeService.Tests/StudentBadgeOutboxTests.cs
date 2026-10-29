using System.Text.Json;
using BadgeService;
using BadgeService.Commands;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #422: rozet kazanılınca BadgeService outbox'ına <see cref="StudentBadgeEarnedEvent"/> yazılır — BadgeEarned satırıyla
/// aynı SaveChanges'te, rozet başına bir kez (tekrar değerlendirme ikinci event üretmez); ikon yalnız allowlist'ten geçmişse.
/// </summary>
public class StudentBadgeOutboxTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private readonly IHubContext<BadgeNotificationHub> _hub = Substitute.For<IHubContext<BadgeNotificationHub>>();

    public StudentBadgeOutboxTests()
    {
        _hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private static BadgeDefinition Badge(string name, string? icon, int target) => new()
    {
        Id = Guid.NewGuid(),
        Code = $"b-{Guid.NewGuid():N}",
        Name = name,
        Description = "d",
        Category = "c",
        RuleType = "AnswerCount",
        RuleConfigJson = $$"""{"target": {{target}}}""",
        Icon = icon,
        IsActive = true,
    };

    private async Task<List<(OutboxMessageView Row, StudentBadgeEarnedEvent Event)>> BadgeEventsAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.OutboxMessages.Where(m => m.Type == OutboxEventRegistry.NameFor<StudentBadgeEarnedEvent>()).ToListAsync())
            .Select(m => (new OutboxMessageView(m.ProcessedAt), JsonSerializer.Deserialize<StudentBadgeEarnedEvent>(m.Content)!))
            .ToList();
    }

    private sealed record OutboxMessageView(DateTime? ProcessedAt);

    [Fact]
    public async Task Earning_badges_enqueues_one_event_per_badge_with_allowlisted_icon_only()
    {
        var reached = Badge("İlk 5", "rocket_launch", 5);
        var badIcon = Badge("Garip ikon", "not-a-real-icon<script>", 3);
        var notYet = Badge("Yüz soru", "star", 100);
        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeDefinitions.AddRange(reached, badIcon, notYet);
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 7, TotalQuestions = 6 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await BadgeEvaluatorFactory.Create(ctx, _hub).EvaluateAnswerSubmittedAsync(7, "kc-7");
        // Tekrar değerlendirme: zaten kazanılmış rozet ikinci event üretmez.
        await using (var ctx = _db.NewContext())
            await BadgeEvaluatorFactory.Create(ctx, _hub).EvaluateAnswerSubmittedAsync(7, "kc-7");

        var events = await BadgeEventsAsync();
        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.Row.ProcessedAt == null && e.Event.UserId == 7 && e.Event.EarnedAtUtc.Kind == DateTimeKind.Utc);
        var byId = events.ToDictionary(e => e.Event.BadgeDefinitionId, e => e.Event);
        byId[reached.Id].Name.ShouldBe("İlk 5");
        byId[reached.Id].Icon.ShouldBe("rocket_launch");
        byId[badIcon.Id].Icon.ShouldBeNull();
        byId.ContainsKey(notYet.Id).ShouldBeFalse();

        await using var check = _db.NewContext();
        var earned = await check.BadgeEarned.Where(b => b.UserId == 7).ToListAsync();
        earned.Select(b => b.BadgeDefinitionId).OrderBy(x => x).ShouldBe(byId.Keys.OrderBy(x => x));
        earned.Single(b => b.BadgeDefinitionId == reached.Id).EarnedDate.ShouldBe(byId[reached.Id].EarnedAtUtc, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Badge_backfill_enqueues_one_event_per_earned_badge_with_the_original_earn_time()
    {
        // issue #501: #422'den önce kazanılmış rozetler veli paneline backfill ile taşınır (canlı akışla aynı payload).
        var first = Badge("İlk 5", "rocket_launch", 5);
        var second = Badge("Garip ikon", "not-a-real-icon<script>", 3);
        var earnedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        await using (var seed = _db.NewContext())
        {
            seed.BadgeDefinitions.AddRange(first, second);
            seed.BadgeEarned.AddRange(
                new BadgeEarned { Id = Guid.NewGuid(), UserId = 7, BadgeDefinitionId = first.Id, EarnedDate = earnedAt },
                new BadgeEarned { Id = Guid.NewGuid(), UserId = 7, BadgeDefinitionId = second.Id, EarnedDate = earnedAt.AddDays(1) },
                new BadgeEarned { Id = Guid.NewGuid(), UserId = 8, BadgeDefinitionId = first.Id, EarnedDate = earnedAt.AddDays(2) });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await StudentBadgeBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: false)).ShouldBe(3);

        var events = (await BadgeEventsAsync()).Select(e => e.Event).ToList();
        events.Count.ShouldBe(3);
        var mine = events.Single(e => e.UserId == 7 && e.BadgeDefinitionId == first.Id);
        mine.Name.ShouldBe("İlk 5");
        mine.Icon.ShouldBe("rocket_launch");
        mine.EarnedAtUtc.ShouldBe(earnedAt);
        events.Single(e => e.BadgeDefinitionId == second.Id).Icon.ShouldBeNull();
        events.Single(e => e.UserId == 8).EarnedAtUtc.ShouldBe(earnedAt.AddDays(2));
    }

    [Fact]
    public async Task Badge_backfill_dry_run_writes_nothing()
    {
        var badge = Badge("İlk 5", "star", 5);
        await using (var seed = _db.NewContext())
        {
            seed.BadgeDefinitions.Add(badge);
            seed.BadgeEarned.Add(new BadgeEarned { Id = Guid.NewGuid(), UserId = 7, BadgeDefinitionId = badge.Id, EarnedDate = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await StudentBadgeBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: true)).ShouldBe(1);

        (await BadgeEventsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(new[] { "backfill-student-badges" }, true)]
    [InlineData(new[] { "backfill-student-badges", "--dry-run" }, true)]
    [InlineData(new[] { "backfill-student-points" }, false)]
    [InlineData(new string[0], false)]
    public void Badge_backfill_command_is_recognised_only_by_its_name(string[] args, bool requested)
        => StudentBadgeBackfillCommand.IsRequested(args).ShouldBe(requested);
}
