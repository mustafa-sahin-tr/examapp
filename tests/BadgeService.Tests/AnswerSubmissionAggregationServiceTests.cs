using BadgeService;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

public class AnswerSubmissionAggregationServiceTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private AnswerSubmissionAggregationService NewService(BadgeDbContext ctx) => new(ctx);

    private static AnswerSubmittedEvent Answer(
        int userId = 1, bool correct = true, int seconds = 30, int point = 10,
        int? subjectId = 5, string subject = "Matematik", DateTime? submittedAt = null) => new()
    {
        UserId = userId,
        IsCorrect = correct,
        TimeTakenInSeconds = seconds,
        QuestionPoint = point,
        SubjectId = subjectId,
        Subject = subject,
        SubmittedAt = submittedAt ?? DateTime.UtcNow,
    };

    private async Task ProcessAsync(params AnswerSubmittedEvent[] events)
    {
        foreach (var e in events)
        {
            await using var ctx = _db.NewContext();
            await NewService(ctx).ProcessAsync(e);
        }
    }

    [Fact]
    public async Task First_correct_answer_creates_all_three_aggregates()
    {
        await ProcessAsync(Answer());

        await using var ctx = _db.NewContext();
        var q = await ctx.StudentQuestionAggregates.SingleAsync(x => x.UserId == 1);
        q.TotalQuestions.ShouldBe(1);
        q.CorrectQuestions.ShouldBe(1);
        q.TotalPoints.ShouldBe(10);
        q.BestCorrectStreak.ShouldBe(1);

        (await ctx.StudentSubjectAggregates.SingleAsync()).SubjectId.ShouldBe(5);
        (await ctx.StudentDailyActivities.SingleAsync()).QuestionCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_wrong_answer_resets_the_current_streak_but_keeps_the_best()
    {
        await ProcessAsync(
            Answer(correct: true),
            Answer(correct: true),
            Answer(correct: false),
            Answer(correct: true));

        await using var ctx = _db.NewContext();
        var q = await ctx.StudentQuestionAggregates.SingleAsync();
        q.TotalQuestions.ShouldBe(4);
        q.CorrectQuestions.ShouldBe(3);
        q.CurrentCorrectStreak.ShouldBe(1);
        q.BestCorrectStreak.ShouldBe(2);
    }

    [Fact]
    public async Task Subject_aggregate_is_skipped_when_there_is_no_subject()
    {
        await ProcessAsync(Answer(subjectId: null, subject: ""));

        await using var ctx = _db.NewContext();
        (await ctx.StudentSubjectAggregates.AnyAsync()).ShouldBeFalse();
        (await ctx.StudentQuestionAggregates.AnyAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task Answers_on_different_days_produce_separate_daily_rows()
    {
        var d1 = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var d2 = new DateTime(2026, 5, 2, 10, 0, 0, DateTimeKind.Utc);
        await ProcessAsync(Answer(submittedAt: d1), Answer(submittedAt: d1), Answer(submittedAt: d2));

        await using var ctx = _db.NewContext();
        var days = await ctx.StudentDailyActivities.OrderBy(x => x.ActivityDate).ToListAsync();
        days.Count.ShouldBe(2);
        days[0].QuestionCount.ShouldBe(2);
        days[1].QuestionCount.ShouldBe(1);
    }

    [Fact]
    public async Task Daily_activity_score_grows_with_questions_correct_and_time()
    {
        await ProcessAsync(Answer(correct: true, seconds: 120, point: 10));

        await using var ctx = _db.NewContext();
        var day = await ctx.StudentDailyActivities.SingleAsync();
        // 1*10 (questions) + 1*5 (correct bonus) + 2 (min(120/60,60)) = 17
        day.ActivityScore.ShouldBe(17);
    }

    [Fact]
    public async Task Negative_time_or_points_are_clamped_to_zero()
    {
        await ProcessAsync(Answer(seconds: -100, point: -50, correct: true));

        await using var ctx = _db.NewContext();
        var q = await ctx.StudentQuestionAggregates.SingleAsync();
        q.TotalTimeSeconds.ShouldBe(0);
        q.TotalPoints.ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
