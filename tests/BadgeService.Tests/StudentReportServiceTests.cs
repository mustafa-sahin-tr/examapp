using BadgeService;
using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;

namespace BadgeService.Tests;

public class StudentReportServiceTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private StudentReportService NewService(BadgeDbContext ctx) => new(ctx);

    [Fact]
    public async Task BadgeProgress_returns_zero_progress_for_a_student_with_no_activity()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Name = "İlk Adım", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{\"target\":10}",
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await NewService(read).GetBadgeProgressAsync(1);

        report.ShouldNotBeNull();
        report.Summary.UserId.ShouldBe(1);
        report.Summary.TotalQuestions.ShouldBe(0);
        report.Summary.CorrectQuestions.ShouldBe(0);
        report.Summary.TotalPoints.ShouldBe(0);
        report.Summary.CurrentCorrectStreak.ShouldBe(0);
        report.Summary.BestCorrectStreak.ShouldBe(0);
        report.Summary.TotalTimeSeconds.ShouldBe(0);
        report.Summary.TotalActiveDays.ShouldBe(0);
        report.Summary.LastAnsweredAtUtc.ShouldBeNull();

        var badge = report.BadgeProgress.ShouldHaveSingleItem();
        badge.CurrentValue.ShouldBe(0);
        badge.TargetValue.ShouldBe(10);
        badge.IsCompleted.ShouldBeFalse();
        badge.EarnedDateUtc.ShouldBeNull();

        report.SubjectBreakdown.ShouldBeEmpty();
    }

    [Fact]
    public async Task BadgeProgress_skips_badges_with_malformed_rule_config_for_a_student_with_no_activity()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Name = "Bozuk Kural", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{}",
            });
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Name = "Geçerli Kural", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{\"target\":5}",
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await NewService(read).GetBadgeProgressAsync(1);

        report.ShouldNotBeNull();
        report.BadgeProgress.ShouldHaveSingleItem().Name.ShouldBe("Geçerli Kural");
    }

    [Fact]
    public async Task BadgeProgress_builds_summary_badge_and_subject_breakdown()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
            {
                Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 10, CorrectQuestions = 7,
                TotalPoints = 70, BestCorrectStreak = 4,
            });
            var def = new BadgeDefinition
            {
                Id = Guid.NewGuid(), Name = "Çalışkan", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{}",
            };
            ctx.BadgeDefinitions.Add(def);
            ctx.StudentBadgeProgresses.Add(new StudentBadgeProgress
            {
                Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = def.Id,
                CurrentValue = 7, TargetValue = 10, IsCompleted = false,
            });
            ctx.StudentSubjectAggregates.Add(new StudentSubjectAggregate
            {
                Id = Guid.NewGuid(), UserId = 1, SubjectId = 3, SubjectName = "Fen",
                TotalQuestions = 4, CorrectQuestions = 3,
            });
            ctx.StudentDailyActivities.Add(new StudentDailyActivity
            {
                Id = Guid.NewGuid(), UserId = 1, ActivityDate = DateTime.UtcNow.Date, QuestionCount = 10,
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await NewService(read).GetBadgeProgressAsync(1);

        report.ShouldNotBeNull();
        report!.Summary.TotalQuestions.ShouldBe(10);
        report.Summary.AccuracyPercentage.ShouldBe(70);
        report.Summary.BestCorrectStreak.ShouldBe(4);
        report.BadgeProgress.ShouldHaveSingleItem().Name.ShouldBe("Çalışkan");
        report.SubjectBreakdown.ShouldHaveSingleItem().AccuracyPercentage.ShouldBe(75);
    }

    [Fact]
    public async Task ActivityReport_returns_days_within_the_requested_window()
    {
        var today = DateTime.UtcNow.Date;
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentDailyActivities.AddRange(
                new StudentDailyActivity { Id = Guid.NewGuid(), UserId = 1, ActivityDate = today, QuestionCount = 3 },
                new StudentDailyActivity { Id = Guid.NewGuid(), UserId = 1, ActivityDate = today.AddDays(-40), QuestionCount = 9 });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await NewService(read).GetActivityReportAsync(1, today.AddDays(-7), today);

        report.Days.ShouldHaveSingleItem().QuestionCount.ShouldBe(3);
    }

    public void Dispose() => _db.Dispose();
}
