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

    private async Task<bool> EarnsAsync(int userId, BadgeDefinition badge, Action<BadgeDbContext> seedAggregates)
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(badge);
            seedAggregates(ctx);
        });
        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(userId, "c");
        return await BadgeEarnedCount(userId) == 1;
    }

    [Fact]
    public async Task TotalStudyTimeMinutes_rule_reads_seconds_from_the_question_aggregate()
    {
        var earned = await EarnsAsync(1, Badge("TotalStudyTimeMinutes", """{"minutes": 10}"""), ctx =>
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalTimeSeconds = 700 }));
        earned.ShouldBeTrue(); // 700s = 11 min >= 10
    }

    [Fact]
    public async Task TotalCorrectAnswers_rule()
    {
        var earned = await EarnsAsync(2, Badge("TotalCorrectAnswers", """{"correct": 5}"""), ctx =>
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 2, CorrectQuestions = 6 }));
        earned.ShouldBeTrue();
    }

    [Fact]
    public async Task ActiveDays_rule_uses_distinct_active_days()
    {
        var today = DateTime.UtcNow.Date;
        var earned = await EarnsAsync(3, Badge("ActiveDays", """{"days": 3}"""), ctx =>
        {
            for (var i = 0; i < 3; i++)
                ctx.StudentDailyActivities.Add(new StudentDailyActivity
                {
                    Id = Guid.NewGuid(), UserId = 3, ActivityDate = today.AddDays(-i), QuestionCount = 1,
                });
        });
        earned.ShouldBeTrue();
    }

    [Fact]
    public async Task DailyStreak_rule_uses_the_best_consecutive_run()
    {
        var today = DateTime.UtcNow.Date;
        var earned = await EarnsAsync(4, Badge("DailyStreak", """{"days": 3}"""), ctx =>
        {
            foreach (var d in new[] { -1, -2, -3, -8 }) // best run = 3
                ctx.StudentDailyActivities.Add(new StudentDailyActivity
                {
                    Id = Guid.NewGuid(), UserId = 4, ActivityDate = today.AddDays(d), QuestionCount = 1,
                });
        });
        earned.ShouldBeTrue();
    }

    [Fact]
    public async Task SubjectAnswerCount_rule_matches_the_subject_by_id()
    {
        var earned = await EarnsAsync(5, Badge("SubjectAnswerCount", """{"target": 10, "subjectId": 42}"""), ctx =>
        {
            ctx.StudentSubjectAggregates.Add(new StudentSubjectAggregate { Id = Guid.NewGuid(), UserId = 5, SubjectId = 42, TotalQuestions = 12 });
            ctx.StudentSubjectAggregates.Add(new StudentSubjectAggregate { Id = Guid.NewGuid(), UserId = 5, SubjectId = 99, TotalQuestions = 99 });
        });
        earned.ShouldBeTrue();
    }

    [Fact]
    public async Task SubjectCorrectCount_rule_matches_the_subject_by_name()
    {
        var earned = await EarnsAsync(6, Badge("SubjectCorrectCount", """{"count": 3, "subjectName": "Matematik"}"""), ctx =>
            ctx.StudentSubjectAggregates.Add(new StudentSubjectAggregate { Id = Guid.NewGuid(), UserId = 6, SubjectName = "matematik", CorrectQuestions = 4 }));
        earned.ShouldBeTrue();
    }

    [Fact]
    public async Task Subject_rules_without_a_subject_criteria_are_skipped()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("SubjectAnswerCount", """{"target": 1}""")); // no subjectId/name
            ctx.StudentSubjectAggregates.Add(new StudentSubjectAggregate { Id = Guid.NewGuid(), UserId = 7, TotalQuestions = 99 });
        });
        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(7, "c");
        (await Progress(7)).ShouldBeNull();
    }

    [Fact]
    public async Task A_rule_with_a_zero_or_missing_target_does_not_create_progress()
    {
        await GivenAsync(ctx =>
        {
            ctx.BadgeDefinitions.Add(Badge("AnswerCount", """{"target": 0}"""));
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 8, TotalQuestions = 5 });
        });
        await using (var ctx = _db.NewContext())
            await NewEvaluator(ctx).EvaluateAnswerSubmittedAsync(8, "c");
        (await Progress(8)).ShouldBeNull();
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
