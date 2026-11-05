using BadgeService;
using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests;

/// <summary>
/// issue #785: her soru oturum başına bir kez, son cevabıyla sayılır. Temizleme yanlış sayılmaz; seri kuralı
/// <see cref="AnswerSubmissionAggregationService.ProcessAsync"/> XML doc'unda.
/// </summary>
public class AnswerCountingTests : IDisposable
{
    private static readonly DateTime Day1 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly BadgeTestDb _db = BadgeTestDb.Create();
    private int _revision;

    public void Dispose() => _db.Dispose();

    private AnswerSubmittedEvent Answer(
        int questionId, bool correct, int seconds = 10, DateTime? submittedAt = null, int instanceId = 5) => new()
    {
        EventId = Guid.NewGuid(),
        UserId = 1,
        TestInstanceId = instanceId,
        QuestionId = questionId,
        IsCorrect = correct,
        QuestionPoint = 10,
        TimeTakenInSeconds = seconds,
        SubjectId = 1,
        Subject = "Matematik",
        SubmittedAt = (submittedAt ?? Day1).AddSeconds(++_revision),
        Revision = _revision,
    };

    private AnswerSubmittedEvent Clear(int questionId, DateTime? submittedAt = null)
    {
        var e = Answer(questionId, correct: false, seconds: 0, submittedAt: submittedAt);
        e.IsCleared = true;
        return e;
    }

    private async Task ProcessAsync(params AnswerSubmittedEvent[] events)
    {
        foreach (var e in events)
        {
            await using var ctx = _db.NewContext();
            (await new AnswerSubmissionAggregationService(ctx).ProcessAsync(e)).ShouldBeTrue();
        }
    }

    private async Task<(StudentQuestionAggregate Q, StudentSubjectAggregate S, StudentDailyActivity A)> AggregatesAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.StudentQuestionAggregates.SingleAsync(),
            await ctx.StudentSubjectAggregates.SingleAsync(),
            await ctx.StudentDailyActivities.SingleAsync());
    }

    [Fact]
    public async Task Changing_an_answer_keeps_one_question_and_follows_the_latest_answer()
    {
        await ProcessAsync(Answer(42, correct: true), Answer(42, correct: false));

        var (q, s, a) = await AggregatesAsync();
        (q.TotalQuestions, q.CorrectQuestions).ShouldBe((1, 0));
        (s.TotalQuestions, s.CorrectQuestions).ShouldBe((1, 0));
        (a.QuestionCount, a.CorrectCount).ShouldBe((1, 0));
        q.TotalPoints.ShouldBe(0);

        await ProcessAsync(Answer(42, correct: true));

        (q, s, a) = await AggregatesAsync();
        (q.TotalQuestions, q.CorrectQuestions).ShouldBe((1, 1));
        (s.TotalQuestions, s.CorrectQuestions).ShouldBe((1, 1));
        (a.QuestionCount, a.CorrectCount).ShouldBe((1, 1));
        q.TotalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task The_activity_score_does_not_grow_when_an_answer_changes()
    {
        await ProcessAsync(Answer(42, correct: true, seconds: 0));
        var before = (await AggregatesAsync()).A.ActivityScore;

        await ProcessAsync(Answer(42, correct: true, seconds: 0));

        (await AggregatesAsync()).A.ActivityScore.ShouldBe(before);
    }

    [Fact]
    public async Task Clearing_removes_the_question_from_the_counts_and_the_points()
    {
        await ProcessAsync(Answer(41, correct: true), Answer(42, correct: true), Clear(42));

        var (q, s, a) = await AggregatesAsync();
        (q.TotalQuestions, q.CorrectQuestions, q.TotalTimeSeconds, q.TotalPoints).ShouldBe((1, 1, 10, 10));
        (s.TotalQuestions, s.CorrectQuestions, s.TotalTimeSeconds, s.TotalPoints).ShouldBe((1, 1, 10, 10));
        (a.QuestionCount, a.CorrectCount, a.TotalTimeSeconds, a.TotalPoints).ShouldBe((1, 1, 10, 10));
    }

    [Fact]
    public async Task Clearing_a_question_that_was_never_answered_touches_no_aggregate()
    {
        await ProcessAsync(Clear(42));

        await using var ctx = _db.NewContext();
        (await ctx.StudentQuestionAggregates.AnyAsync()).ShouldBeFalse();
        (await ctx.StudentSubjectAggregates.AnyAsync()).ShouldBeFalse();
        (await ctx.StudentDailyActivities.AnyAsync()).ShouldBeFalse();
        (await ctx.AnswerPointAwards.SingleAsync()).HasBeenAnswered.ShouldBeFalse();
    }

    [Fact]
    public async Task The_first_real_answer_after_an_early_clear_counts_and_raises_the_streak()
    {
        await ProcessAsync(Clear(42), Answer(42, correct: true));

        var q = (await AggregatesAsync()).Q;
        (q.TotalQuestions, q.CorrectQuestions, q.CurrentCorrectStreak).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Answering_again_after_a_clear_counts_the_question_once()
    {
        await ProcessAsync(Answer(42, correct: true), Clear(42), Answer(42, correct: false, seconds: 20));

        var (q, _, a) = await AggregatesAsync();
        (q.TotalQuestions, q.CorrectQuestions, q.TotalTimeSeconds).ShouldBe((1, 0, 20));
        (a.QuestionCount, a.CorrectCount, a.TotalTimeSeconds).ShouldBe((1, 0, 20));
    }

    [Fact]
    public async Task The_time_total_uses_the_latest_time_of_a_question()
    {
        await ProcessAsync(Answer(42, correct: true, seconds: 30), Answer(42, correct: false, seconds: 45));

        var (q, s, a) = await AggregatesAsync();
        q.TotalTimeSeconds.ShouldBe(45);
        s.TotalTimeSeconds.ShouldBe(45);
        a.TotalTimeSeconds.ShouldBe(45);

        await ProcessAsync(Answer(42, correct: false, seconds: 5));

        (await AggregatesAsync()).Q.TotalTimeSeconds.ShouldBe(5);
    }

    [Fact]
    public async Task The_same_question_in_another_session_counts_again()
    {
        await ProcessAsync(Answer(42, correct: true, instanceId: 5), Answer(42, correct: true, instanceId: 6));

        (await AggregatesAsync()).Q.TotalQuestions.ShouldBe(2);
    }

    [Fact]
    public async Task A_change_on_a_later_day_updates_the_day_the_question_was_counted()
    {
        await ProcessAsync(
            Answer(42, correct: true, submittedAt: Day1),
            Answer(42, correct: false, submittedAt: Day1.AddDays(1)));

        var a = (await AggregatesAsync()).A;
        a.ActivityDate.Date.ShouldBe(Day1.Date);
        (a.QuestionCount, a.CorrectCount, a.TotalPoints).ShouldBe((1, 0, 0));
    }

    [Fact]
    public async Task Clearing_does_not_reset_the_streak()
    {
        await ProcessAsync(Answer(41, correct: true), Answer(42, correct: true), Clear(42));

        var q = (await AggregatesAsync()).Q;
        q.CurrentCorrectStreak.ShouldBe(2);
        q.BestCorrectStreak.ShouldBe(2);
    }

    [Fact]
    public async Task Correcting_or_repeating_an_answer_does_not_raise_the_streak()
    {
        await ProcessAsync(
            Answer(41, correct: false), // first answer wrong: streak 0
            Answer(41, correct: true),  // corrected: no raise
            Answer(42, correct: true),  // first correct: 1
            Answer(42, correct: true),  // same answer again: no raise
            Clear(42),
            Answer(42, correct: true)); // re-answered after a clear: no raise

        (await AggregatesAsync()).Q.CurrentCorrectStreak.ShouldBe(1);
    }

    [Fact]
    public async Task Changing_a_correct_answer_to_wrong_resets_the_streak_but_keeps_the_best()
    {
        await ProcessAsync(Answer(41, correct: true), Answer(42, correct: true), Answer(42, correct: false));

        var q = (await AggregatesAsync()).Q;
        q.CurrentCorrectStreak.ShouldBe(0);
        q.BestCorrectStreak.ShouldBe(2);
    }

    [Fact]
    public async Task Changing_a_wrong_answer_to_another_wrong_answer_does_not_reset_the_streak()
    {
        await ProcessAsync(Answer(41, correct: false), Answer(42, correct: true), Answer(41, correct: false));

        (await AggregatesAsync()).Q.CurrentCorrectStreak.ShouldBe(1);
    }

    [Fact]
    public async Task A_stale_or_duplicate_delivery_does_not_change_the_counts()
    {
        var first = Answer(42, correct: true);
        var second = Answer(42, correct: false, seconds: 25);
        await ProcessAsync(first, second);

        var redelivered = Answer(42, correct: true);
        redelivered.Revision = first.Revision;
        await using (var ctx = _db.NewContext())
            (await new AnswerSubmissionAggregationService(ctx).ProcessAsync(redelivered)).ShouldBeFalse();
        await using (var ctx = _db.NewContext())
            (await new AnswerSubmissionAggregationService(ctx).ProcessAsync(second)).ShouldBeFalse();

        var q = (await AggregatesAsync()).Q;
        (q.TotalQuestions, q.CorrectQuestions, q.TotalTimeSeconds).ShouldBe((1, 0, 25));
    }

    [Fact]
    public async Task An_award_row_marked_by_the_migration_is_not_counted_again()
    {
        // Same values as the backfill in AddAnswerCountStateToPointAwards; the inflated totals stay as they are.
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
            {
                Id = Guid.NewGuid(), UserId = 1, TotalQuestions = 3, CorrectQuestions = 2, TotalPoints = 10, CurrentCorrectStreak = 1,
            });
            ctx.AnswerPointAwards.Add(new AnswerPointAward
            {
                TestInstanceId = 5, QuestionId = 42, UserId = 1, PointsAwarded = 10, LastAppliedRevision = 1,
                LastAppliedRevisionUtc = Day1, CountedActivityDate = Day1.Date, CountedCorrect = true, HasBeenAnswered = true,
            });
            await ctx.SaveChangesAsync();
        }

        _revision = 1;
        await ProcessAsync(Answer(42, correct: false, seconds: 0));

        await using var check = _db.NewContext();
        var q = await check.StudentQuestionAggregates.SingleAsync();
        (q.TotalQuestions, q.CorrectQuestions, q.TotalPoints).ShouldBe((3, 1, 0));
    }
}
