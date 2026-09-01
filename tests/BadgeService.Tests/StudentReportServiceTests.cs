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
    public async Task BadgeProgress_is_null_for_a_student_with_no_activity()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetBadgeProgressAsync(1)).ShouldBeNull();
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
