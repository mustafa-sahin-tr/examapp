using BadgeService;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BadgeService.Tests;

/// <summary>
/// issue #396: <c>UserResetService</c> stamps a per-user reset time; <c>AnswerSubmittedEvent</c>s submitted before it
/// (already in the outbox/queue when the student reset) are ignored, so they cannot re-award points for the
/// soft-deleted instances after the reset wiped <c>AnswerPointAward</c>/<c>ProcessedAnswerSubmission</c>.
/// </summary>
public class UserResetEventGuardTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static AnswerSubmissionAggregationService NewService(BadgeDbContext ctx) =>
        new(ctx, Options.Create(new AnswerPointOptions { MaxQuestionPoint = 100 }));

    private static AnswerSubmittedEvent Answer(int userId, int testInstanceId, DateTime submittedAt, int revision = 1) => new()
    {
        EventId = Guid.NewGuid(),
        UserId = userId,
        TestInstanceId = testInstanceId,
        QuestionId = 42,
        IsCorrect = true,
        QuestionPoint = 10,
        TimeTakenInSeconds = 10,
        SubjectId = 1,
        Subject = "Matematik",
        SubmittedAt = submittedAt,
        Revision = revision,
    };

    private async Task<bool> ProcessAsync(AnswerSubmittedEvent e)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).ProcessAsync(e);
    }

    private async Task ResetAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        await new UserResetService(ctx).ResetAsync(userId);
    }

    private async Task<int> PointsAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.StudentQuestionAggregates.Where(x => x.UserId == userId).Select(x => x.TotalPoints).FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Reset_stamps_the_user_and_a_later_reset_moves_the_stamp_forward()
    {
        await ResetAsync(1);
        DateTime first;
        await using (var ctx = _db.NewContext())
            first = (await ctx.UserResetMarkers.SingleAsync(x => x.UserId == 1)).ResetAtUtc;
        first.ShouldBe(DateTime.UtcNow, TimeSpan.FromSeconds(10));

        await Task.Delay(20);
        await ResetAsync(1);

        await using var check = _db.NewContext();
        (await check.UserResetMarkers.SingleAsync(x => x.UserId == 1)).ResetAtUtc.ShouldBeGreaterThan(first);
    }

    [Fact]
    public async Task An_in_flight_answer_from_before_the_reset_gives_no_points()
    {
        // Student answers (10 points), the event that carries the same answer is still in flight (redelivery or a
        // second event for the same question), then the student resets.
        var submittedBeforeReset = DateTime.UtcNow.AddSeconds(-5);
        (await ProcessAsync(Answer(1, 5, submittedBeforeReset))).ShouldBeTrue();
        (await PointsAsync(1)).ShouldBe(10);

        await ResetAsync(1);
        (await PointsAsync(1)).ShouldBe(0);

        // Without the guard this would re-award 10 points: the reset deleted the (TestInstanceId, QuestionId) award.
        var pointsEventsBefore = await PointsEventCountAsync();
        (await ProcessAsync(Answer(1, 5, submittedBeforeReset, revision: 2))).ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
        (await check.StudentSubjectAggregates.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
        (await check.StudentDailyActivities.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
        (await check.AnswerPointAwards.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
        (await check.ProcessedAnswerSubmissions.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
        (await PointsEventCountAsync()).ShouldBe(pointsEventsBefore);
    }

    [Fact]
    public async Task Answers_submitted_after_the_reset_count_normally()
    {
        await ResetAsync(1);
        await Task.Delay(5);

        (await ProcessAsync(Answer(1, 6, DateTime.UtcNow))).ShouldBeTrue();
        (await PointsAsync(1)).ShouldBe(10);
    }

    [Fact]
    public async Task Another_users_reset_does_not_filter_this_users_answers()
    {
        await ResetAsync(2);

        (await ProcessAsync(Answer(1, 5, DateTime.UtcNow.AddMinutes(-10)))).ShouldBeTrue();
        (await PointsAsync(1)).ShouldBe(10);
    }

    [Fact]
    public async Task A_user_who_never_reset_is_unaffected_by_old_timestamps()
    {
        (await ProcessAsync(Answer(1, 5, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)))).ShouldBeTrue();
        (await PointsAsync(1)).ShouldBe(10);
    }

    // ---- issue #396 review: the reset line comes from the exam API's clock ----

    private async Task<DateTime> MarkerAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.UserResetMarkers.SingleAsync(x => x.UserId == userId)).ResetAtUtc;
    }

    [Fact]
    public async Task The_callers_reset_time_is_the_line()
    {
        var resetAt = DateTime.UtcNow.AddSeconds(-30);
        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1, resetAt);

        (await MarkerAsync(1)).ShouldBe(EventVersion.Normalize(resetAt));

        // An answer the exam API stamped 1 s after the line counts; one stamped 1 s before does not.
        (await ProcessAsync(Answer(1, 5, resetAt.AddSeconds(-1)))).ShouldBeFalse();
        (await ProcessAsync(Answer(1, 6, resetAt.AddSeconds(1)))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_far_future_reset_time_is_clamped_to_now()
    {
        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1, DateTime.UtcNow.AddHours(3));

        (await MarkerAsync(1)).ShouldBe(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task An_older_reset_time_never_moves_the_line_back()
    {
        var later = DateTime.UtcNow.AddSeconds(-10);
        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1, later);
        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1, later.AddMinutes(-5));

        (await MarkerAsync(1)).ShouldBe(EventVersion.Normalize(later));
    }

    [Fact]
    public async Task The_reset_endpoint_passes_the_offset_timestamp_through_as_utc()
    {
        var resetAt = new DateTimeOffset(DateTime.UtcNow.AddSeconds(-20)).ToOffset(TimeSpan.FromHours(3));
        await using (var ctx = _db.NewContext())
            await new BadgeService.Controllers.ResetController(new UserResetService(ctx))
                .ResetUserAsync(1, resetAt, CancellationToken.None);

        (await MarkerAsync(1)).ShouldBe(EventVersion.Normalize(resetAt.UtcDateTime));
    }

    private async Task<int> PointsEventCountAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<StudentPointsChangedEvent>();
        return await ctx.OutboxMessages.CountAsync(m => m.Type == type);
    }
}
