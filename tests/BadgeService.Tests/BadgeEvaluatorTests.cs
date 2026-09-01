using BadgeService;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

public class BadgeEvaluatorTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private readonly IHubContext<BadgeNotificationHub> _hub = Substitute.For<IHubContext<BadgeNotificationHub>>();

    public BadgeEvaluatorTests()
    {
        // recursive substitute: Clients.User(x) => IClientProxy; make SendCoreAsync awaitable
        _hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    private BadgeEvaluator NewEvaluator(BadgeDbContext ctx) => new(ctx, _hub);

    private async Task GivenAsync(Action<BadgeDbContext> seed)
    {
        await using var ctx = _db.NewContext();
        seed(ctx);
        await ctx.SaveChangesAsync();
    }

    private static BadgeDefinition Badge(string ruleType, string ruleConfigJson) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Badge",
        Description = "d",
        Category = "c",
        RuleType = ruleType,
        RuleConfigJson = ruleConfigJson,
    };

    private async Task<int> BadgeEarnedCount(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.BadgeEarned.CountAsync(x => x.UserId == userId);
    }

    private async Task<StudentBadgeProgress?> Progress(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.StudentBadgeProgresses.FirstOrDefaultAsync(x => x.UserId == userId);
    }

    [Fact]
    public async Task No_badge_definitions_is_a_no_op()
    {
        await using var ctx = _db.NewContext();
        await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(userId: 1, clientId: "c");

        (await BadgeEarnedCount(1)).ShouldBe(0);
        (await Progress(1)).ShouldBeNull();
    }

    [Fact]
    public async Task Tracks_progress_without_earning_when_below_target()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("AnswerCount", """{"target": 10}"""));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 4 });
        });

        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(1, "c");

        var progress = await Progress(1);
        progress.ShouldNotBeNull();
        progress!.CurrentValue.ShouldBe(4);
        progress.TargetValue.ShouldBe(10);
        progress.IsCompleted.ShouldBeFalse();
        (await BadgeEarnedCount(1)).ShouldBe(0);
    }

    [Fact]
    public async Task Earns_the_badge_when_the_target_is_met()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("AnswerCount", """{"target": 10}"""));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 12 });
        });

        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(1, "c");

        var progress = await Progress(1);
        progress!.IsCompleted.ShouldBeTrue();
        progress.CurrentValue.ShouldBe(10); // capped at target
        (await BadgeEarnedCount(1)).ShouldBe(1);
    }

    [Fact]
    public async Task Earning_is_idempotent_across_re_evaluations()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("CorrectStreak", """{"streak": 3}"""));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, BestCorrectStreak = 5 });
        });

        for (var i = 0; i < 3; i++)
            await using (var ctx = _db.NewContext())
                await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(1, "c");

        (await BadgeEarnedCount(1)).ShouldBe(1);
    }

    [Theory]
    [InlineData("not valid json")]
    [InlineData("")]
    public async Task A_definition_with_a_broken_rule_config_is_skipped(string ruleConfig)
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("AnswerCount", ruleConfig));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 99 });
        });

        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(1, "c");

        (await Progress(1)).ShouldBeNull();
        (await BadgeEarnedCount(1)).ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_rule_type_is_skipped()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("SomethingWeDoNotSupport", """{"target": 1}"""));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 99 });
        });

        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(1, "c");

        (await Progress(1)).ShouldBeNull();
    }

    [Fact]
    public async Task Bare_number_rule_config_is_accepted_as_the_target()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("AnswerCount", "5"));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 6 });
        });

        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(1, "c");

        (await Progress(1))!.IsCompleted.ShouldBeTrue();
        (await BadgeEarnedCount(1)).ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
