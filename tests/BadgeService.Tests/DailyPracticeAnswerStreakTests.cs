using BadgeService;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

/// <summary>
/// Issue #99: "Günün soruları" cevapları exam API'de mevcut <see cref="AnswerSubmittedEvent"/> ile yayınlanır; worksheet
/// instance'ı olmadığı için <c>TestInstanceId = -PracticeSessionId</c> taşır (bkz. exam API
/// <c>PracticeSessionService.EnqueueDailyAnswerEventAsync</c>). Bu testler, BadgeService'te HİÇBİR değişiklik olmadan bu
/// event'lerin gün aktivitesini (<see cref="StudentDailyActivity"/>) ve dolayısıyla mevcut <c>DailyStreak</c> rozet kuralını
/// beslediğini, puan tekilleştirmesinin (AnswerPointAward) öğrenci/oturum arasında çakışmadığını kanıtlar.
/// </summary>
public class DailyPracticeAnswerStreakTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private readonly IHubContext<BadgeNotificationHub> _hub = Substitute.For<IHubContext<BadgeNotificationHub>>();

    public DailyPracticeAnswerStreakTests()
    {
        _hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Exam API'nin günlük set cevabı için ürettiği event şekli.</summary>
    private static AnswerSubmittedEvent DailyAnswer(int userId, int practiceSessionId, int questionId, DateTime submittedAt, bool correct = true) => new()
    {
        EventId = Guid.NewGuid(),
        UserId = userId,
        QuestionId = questionId,
        SubjectId = 5,
        Subject = "Matematik",
        TestInstanceId = -practiceSessionId,
        TestInstanceQuestionId = practiceSessionId * 100 + questionId,
        ClientId = $"kc-{userId}",
        IsCorrect = correct,
        QuestionPoint = 10,
        DifficultyLevel = 1,
        TimeTakenInSeconds = 20,
        SubmittedAt = submittedAt,
        Revision = 1,
    };

    private async Task<bool> ProcessAsync(AnswerSubmittedEvent e)
    {
        await using var ctx = _db.NewContext();
        return await new AnswerSubmissionAggregationService(ctx).ProcessAsync(e);
    }

    [Fact]
    public async Task Daily_set_answers_on_consecutive_days_feed_daily_activity_and_earn_the_DailyStreak_badge()
    {
        await using (var seed = _db.NewContext())
        {
            seed.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(),
                Code = "daily-streak-3",
                Name = "3 gün seri",
                Description = "d",
                Category = "c",
                RuleType = "DailyStreak",
                RuleConfigJson = """{"days": 3}""",
                IsActive = true,
            });
            await seed.SaveChangesAsync();
        }

        var today = DateTime.UtcNow.Date.AddHours(9);
        // Her gün ayrı günlük set oturumu (session 11/12/13); aynı soru (77) farklı günlerde yeniden çıkabilir.
        (await ProcessAsync(DailyAnswer(userId: 4, practiceSessionId: 11, questionId: 77, today.AddDays(-2)))).ShouldBeTrue();
        (await ProcessAsync(DailyAnswer(userId: 4, practiceSessionId: 12, questionId: 77, today.AddDays(-1)))).ShouldBeTrue();
        (await ProcessAsync(DailyAnswer(userId: 4, practiceSessionId: 13, questionId: 77, today))).ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            (await ctx.StudentDailyActivities.CountAsync(a => a.UserId == 4 && a.QuestionCount > 0)).ShouldBe(3);
            // Aynı soru, farklı oturum anahtarı → her gün puanlanır (stale sayılmaz).
            (await ctx.StudentQuestionAggregates.SingleAsync(a => a.UserId == 4)).TotalPoints.ShouldBe(30);

            await BadgeEvaluatorFactory.Create(ctx, _hub).EvaluateAnswerSubmittedAsync(4, "kc-4");
        }

        await using (var check = _db.NewContext())
        {
            (await check.BadgeEarned.CountAsync(b => b.UserId == 4)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Two_students_answering_the_same_question_in_their_own_daily_sessions_are_both_applied()
    {
        var now = DateTime.UtcNow;
        (await ProcessAsync(DailyAnswer(userId: 1, practiceSessionId: 21, questionId: 9, now))).ShouldBeTrue();
        (await ProcessAsync(DailyAnswer(userId: 2, practiceSessionId: 22, questionId: 9, now))).ShouldBeTrue();

        await using var ctx = _db.NewContext();
        (await ctx.StudentQuestionAggregates.SingleAsync(a => a.UserId == 1)).TotalPoints.ShouldBe(10);
        (await ctx.StudentQuestionAggregates.SingleAsync(a => a.UserId == 2)).TotalPoints.ShouldBe(10);
        (await ctx.AnswerPointAwards.CountAsync(a => a.TestInstanceId < 0)).ShouldBe(2);
    }

    [Fact]
    public async Task A_duplicate_daily_answer_for_the_same_session_and_question_is_stale_and_not_double_counted()
    {
        var now = DateTime.UtcNow;
        (await ProcessAsync(DailyAnswer(userId: 3, practiceSessionId: 31, questionId: 5, now))).ShouldBeTrue();
        // Eşzamanlı çift gönderim: ikinci event farklı EventId ama aynı (oturum, soru) + Revision=1 → stale.
        (await ProcessAsync(DailyAnswer(userId: 3, practiceSessionId: 31, questionId: 5, now.AddSeconds(1)))).ShouldBeFalse();

        await using var ctx = _db.NewContext();
        var agg = await ctx.StudentQuestionAggregates.SingleAsync(a => a.UserId == 3);
        agg.TotalPoints.ShouldBe(10);
        agg.TotalQuestions.ShouldBe(1);
    }
}
