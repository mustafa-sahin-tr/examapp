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
                Id = Guid.NewGuid(), Code = "test-ilk-adim", Name = "İlk Adım", Description = "d", Category = "c",
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
                Id = Guid.NewGuid(), Code = "test-bozuk-kural", Name = "Bozuk Kural", Description = "d", Category = "c",
                RuleType = "AnswerCount", RuleConfigJson = "{}",
            });
            ctx.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "test-gecerli-kural", Name = "Geçerli Kural", Description = "d", Category = "c",
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
                Id = Guid.NewGuid(), Code = "test-caliskan", Name = "Çalışkan", Description = "d", Category = "c",
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
    public async Task BadgeProgress_hides_inactive_badges_with_no_progress_but_keeps_already_earned_ones()
    {
        // Issue #148: deactivated badges disappear from the catalog for badges never started, but a
        // badge the student already earned/completed stays visible — deactivation isn't a history-eraser.
        Guid inactiveNeverStartedId;
        Guid inactiveButEarnedId;
        await using (var ctx = _db.NewContext())
        {
            var neverStarted = new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "inactive-never-started", Name = "Hiç Başlanmamış", Description = "d",
                Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":10}", IsActive = false,
            };
            var earnedButNowInactive = new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "inactive-earned", Name = "Kazanılmış Ama Pasif", Description = "d",
                Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":10}", IsActive = false,
            };
            inactiveNeverStartedId = neverStarted.Id;
            inactiveButEarnedId = earnedButNowInactive.Id;
            ctx.BadgeDefinitions.AddRange(neverStarted, earnedButNowInactive);
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
            {
                Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 10, CorrectQuestions = 10,
            });
            ctx.StudentBadgeProgresses.Add(new StudentBadgeProgress
            {
                Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = earnedButNowInactive.Id,
                CurrentValue = 10, TargetValue = 10, IsCompleted = true,
            });
            // Code review follow-up (#148, SHOULD-FIX): "earned" is now defined by BadgeEarned existing
            // (see GetBadgeProgressAsync), not just the recomputed progress row's IsCompleted flag.
            ctx.BadgeEarned.Add(new BadgeEarned
            {
                Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = earnedButNowInactive.Id, EarnedDate = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await NewService(read).GetBadgeProgressAsync(1);

        report.BadgeProgress.ShouldNotContain(b => b.BadgeDefinitionId == inactiveNeverStartedId);
        report.BadgeProgress.ShouldContain(b => b.BadgeDefinitionId == inactiveButEarnedId && b.IsCompleted);
    }

    [Fact]
    public async Task BadgeProgress_keeps_an_earned_badge_visible_and_completed_after_target_raised_then_deactivated()
    {
        // Code review follow-up (#148, SHOULD-FIX): raising a badge's target after it was earned can
        // recompute StudentBadgeProgress.IsCompleted back to false (BadgeEvaluatorTests covers that it
        // shouldn't, but this pins down the report's OWN fallback — even if IsCompleted somehow reads
        // false, a BadgeEarned row must still make the badge visible and shown as completed).
        Guid badgeId;
        await using (var ctx = _db.NewContext())
        {
            var badge = new BadgeDefinition
            {
                Id = Guid.NewGuid(), Code = "raised-then-deactivated", Name = "Yükseltilmiş Hedef", Description = "d",
                Category = "c", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":500}", IsActive = false,
            };
            badgeId = badge.Id;
            ctx.BadgeDefinitions.Add(badge);
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
            {
                Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 10, CorrectQuestions = 10,
            });
            // Simulates a stale/recomputed progress row: target was raised past the student's current
            // value, so a naive recompute reads IsCompleted = false — but BadgeEarned says otherwise.
            ctx.StudentBadgeProgresses.Add(new StudentBadgeProgress
            {
                Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = badge.Id,
                CurrentValue = 10, TargetValue = 500, IsCompleted = false,
            });
            ctx.BadgeEarned.Add(new BadgeEarned
            {
                Id = Guid.NewGuid(), UserId = 1, BadgeDefinitionId = badge.Id, EarnedDate = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var report = await NewService(read).GetBadgeProgressAsync(1);

        var item = report.BadgeProgress.ShouldHaveSingleItem();
        item.BadgeDefinitionId.ShouldBe(badgeId);
        item.IsCompleted.ShouldBeTrue();
        item.EarnedDateUtc.ShouldNotBeNull();
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
