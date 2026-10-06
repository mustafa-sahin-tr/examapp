using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #396: server-side time limit. A Started instance past <c>StartTime + MaxDurationSeconds (snapshot) + 30 s</c> takes
/// no answer (409 TestNotInProgress, Reason=TimeExpired) and is persisted as Expired by save/end/start (and the sweeper);
/// GET reads only report it. Worksheets without a limit are unaffected.
/// </summary>
public class TestSessionServiceTimeLimitTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private const int UserId = 77;
    private const int Limit = 600;

    private sealed record Seeded(int InstanceId, int TiqId, int CorrectAnswerId, int WorksheetId, int StudentId, int GradeId, DateTime StartTime);

    private async Task<Seeded> SeedAsync(
        TimeSpan startedAgo, int? snapshotLimit = Limit, int worksheetLimit = Limit, bool retiredWorksheet = false)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "4" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var question = new Question { Text = "2+2?", Point = 10, DifficultyLevel = 1 };
        var worksheet = new Worksheet { Name = "Timed", Description = "", GradeId = grade.Id, MaxDurationSeconds = worksheetLimit };
        var student = new Student { UserId = UserId, StudentNumber = "S77", SchoolName = "Sch", GradeId = grade.Id };
        ctx.AddRange(question, worksheet, student);
        await ctx.SaveChangesAsync();

        var correct = new Answer { QuestionId = question.Id, Text = "4", Tag = "A" };
        ctx.Answers.Add(correct);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = correct.Id;

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        var startTime = DateTime.UtcNow - startedAgo;
        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = startTime, Status = WorksheetInstanceStatus.Started,
            MaxDurationSeconds = snapshotLimit,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var tiq = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        ctx.TestInstanceQuestions.Add(tiq);
        if (retiredWorksheet)
            worksheet.IsDeleted = true;
        await ctx.SaveChangesAsync();

        return new Seeded(instance.Id, tiq.Id, correct.Id, worksheet.Id, student.Id, grade.Id, startTime);
    }

    private static TimeSpan Overdue => TimeSpan.FromSeconds(Limit) + TestTimeLimit.Tolerance + TimeSpan.FromSeconds(5);

    private static SaveAnswerDto Dto(Seeded s) => new()
    {
        TestInstanceId = s.InstanceId, TestQuestionId = s.TiqId, SelectedAnswerId = s.CorrectAnswerId, TimeTaken = 5,
    };

    private static UserProfileDto User => new() { Id = UserId, KeycloakId = "kc-77" };

    private async Task<WorksheetInstance> InstanceAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstances.IgnoreQueryFilters().AsNoTracking().FirstAsync(i => i.Id == id);
    }

    private async Task ShouldHaveWrittenNoAnswerAsync(Seeded s)
    {
        await using var check = _db.NewContext();
        var tiq = await check.TestInstanceQuestions.IgnoreQueryFilters().FirstAsync(x => x.Id == s.TiqId);
        tiq.SelectedAnswerId.ShouldBeNull();
        tiq.AnswerRevision.ShouldBe(0);
        (await check.OutboxMessages.AnyAsync()).ShouldBeFalse();
    }

    private static void ShouldBeTimeUp(TestSessionResultDto result)
    {
        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        result.Reason.ShouldBe(TestSessionRejectReasons.TimeExpired);
        result.Message.ShouldContain("süre");
    }

    // ---- TestTimeLimit ----

    [Fact]
    public void Time_limit_includes_the_tolerance_and_ignores_unlimited_instances()
    {
        var start = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);
        TestTimeLimit.EndsAt(start, 600).ShouldBe(start.AddMinutes(10));
        TestTimeLimit.IsOver(start, 600, start.AddSeconds(630)).ShouldBeFalse();
        TestTimeLimit.IsOver(start, 600, start.AddSeconds(631)).ShouldBeTrue();
        TestTimeLimit.EndsAt(start, 0).ShouldBeNull();
        TestTimeLimit.EndsAt(start, null).ShouldBeNull();
        TestTimeLimit.IsOver(start, null, start.AddYears(1)).ShouldBeFalse();
        TestTimeLimit.IsOver(start, -5, start.AddYears(1)).ShouldBeFalse();
    }

    [Fact]
    public void Remaining_seconds_round_up_stop_at_zero_and_are_null_without_a_limit()
    {
        var start = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);
        var started = WorksheetInstanceStatus.Started;
        TestTimeLimit.RemainingSeconds(started, start, 600, start.AddSeconds(100.2)).ShouldBe(500);
        TestTimeLimit.RemainingSeconds(started, start, 600, start.AddSeconds(615)).ShouldBe(0);
        TestTimeLimit.RemainingSeconds(started, start, null, start.AddSeconds(5)).ShouldBeNull();
        TestTimeLimit.RemainingSeconds(WorksheetInstanceStatus.Completed, start, 600, start.AddSeconds(5)).ShouldBe(0);
        TestTimeLimit.EffectiveStatus(started, start, 600, start.AddSeconds(631)).ShouldBe(WorksheetInstanceStatus.Expired);
        TestTimeLimit.EffectiveStatus(WorksheetInstanceStatus.Completed, start, 600, start.AddSeconds(631))
            .ShouldBe(WorksheetInstanceStatus.Completed);
    }

    // ---- start-test snapshot ----

    [Fact]
    public async Task StartTest_copies_the_worksheet_limit_onto_the_instance()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "4" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id, MaxDurationSeconds = 900 };
            var st = new Student { UserId = UserId, StudentNumber = "S", SchoolName = "S", GradeId = grade.Id };
            ctx.AddRange(ws, st);
            await ctx.SaveChangesAsync();

            var result = await NewService(ctx).StartTestAsync(ws.Id, new StudentProfileDto { Id = st.Id, GradeId = grade.Id });
            result.Success.ShouldBeTrue();
            (await InstanceAsync(result.InstanceId)).MaxDurationSeconds.ShouldBe(900);
        }
    }

    [Fact]
    public async Task The_instance_snapshot_wins_over_a_later_worksheet_change()
    {
        // Teacher shortened the worksheet to 60 s after the student started a 600 s test.
        var s = await SeedAsync(TimeSpan.FromSeconds(300), snapshotLimit: Limit, worksheetLimit: 60);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveAnswer(Dto(s), User)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            var dto = await NewService(ctx).GetCanvasTestResultAsync(s.InstanceId, UserId);
            dto!.MaxDurationSeconds.ShouldBe(Limit);
            dto.RemainingSeconds!.Value.ShouldBeInRange(Limit - 302, Limit - 298);
        }
    }

    // ---- SaveAnswer ----

    [Fact]
    public async Task SaveAnswer_after_the_time_limit_is_409_time_up_marks_the_instance_expired_and_writes_nothing()
    {
        var s = await SeedAsync(Overdue);

        await using (var ctx = _db.NewContext())
            ShouldBeTimeUp(await NewService(ctx).SaveAnswer(Dto(s), User));

        await ShouldHaveWrittenNoAnswerAsync(s);
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        instance.EndTime.ShouldNotBeNull();
        instance.EndTime!.Value.ShouldBe(s.StartTime.AddSeconds(Limit), TimeSpan.FromMilliseconds(1));
        instance.UpdateUserId.ShouldBe(UserId);

        // Every later answer is refused through the ordinary not-Started path — still reported as time-up.
        await using (var ctx = _db.NewContext())
            ShouldBeTimeUp(await NewService(ctx).SaveAnswer(Dto(s), User));
        await ShouldHaveWrittenNoAnswerAsync(s);
    }

    [Fact]
    public async Task SaveAnswer_on_a_completed_test_keeps_the_generic_not_in_progress_message()
    {
        var s = await SeedAsync(TimeSpan.FromSeconds(10));
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).EndTest(s.InstanceId, UserId)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).SaveAnswer(Dto(s), User);
            result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
            result.Reason.ShouldBeNull();
        }
    }

    // issue #396 re-check: Expired is not always a time-up — the student reset closes open sessions as Expired too.
    // The reason is derived from the clock, so those get the generic message.
    [Fact]
    public async Task A_session_closed_as_Expired_before_its_time_is_not_reported_as_time_up()
    {
        var s = await SeedAsync(TimeSpan.FromSeconds(30));
        await using (var ctx = _db.NewContext())
            await ctx.TestInstances.Where(i => i.Id == s.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, WorksheetInstanceStatus.Expired));

        await using (var ctx = _db.NewContext())
        {
            var save = await NewService(ctx).SaveAnswer(Dto(s), User);
            save.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
            save.Reason.ShouldBeNull();

            var end = await NewService(ctx).EndTest(s.InstanceId, UserId);
            end.Conflict.ShouldBeTrue();
            end.Reason.ShouldBeNull();
        }
    }

    [Fact]
    public async Task SaveAnswer_after_the_limit_but_inside_the_tolerance_is_accepted()
    {
        var s = await SeedAsync(TimeSpan.FromSeconds(Limit) + TimeSpan.FromSeconds(10));

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveAnswer(Dto(s), User)).Success.ShouldBeTrue();

        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Started);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task SaveAnswer_on_an_instance_without_a_time_limit_is_never_expired(int? snapshot)
    {
        var s = await SeedAsync(TimeSpan.FromDays(30), snapshotLimit: snapshot);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveAnswer(Dto(s), User)).Success.ShouldBeTrue();

        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Started);
    }

    [Fact]
    public async Task SaveAnswer_after_the_time_limit_is_refused_under_a_retrying_execution_strategy()
    {
        var s = await SeedAsync(Overdue);

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            ShouldBeTimeUp(await NewService(ctx).SaveAnswer(Dto(s), User));

        await ShouldHaveWrittenNoAnswerAsync(s);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Expired);
    }

    [Fact]
    public async Task The_time_limit_still_applies_after_the_worksheet_is_retired()
    {
        var s = await SeedAsync(Overdue, retiredWorksheet: true);

        await using (var ctx = _db.NewContext())
            ShouldBeTimeUp(await NewService(ctx).SaveAnswer(Dto(s), User));

        await ShouldHaveWrittenNoAnswerAsync(s);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Expired);
    }

    // ---- EndTest ----

    [Fact]
    public async Task EndTest_after_the_time_limit_marks_expired_and_is_a_time_up_conflict()
    {
        var s = await SeedAsync(Overdue);

        await using (var ctx = _db.NewContext())
            ShouldBeTimeUp(await NewService(ctx).EndTest(s.InstanceId, UserId));

        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Expired);

        // Repeated end-test stays a conflict (Expired is terminal, #367).
        await using (var ctx = _db.NewContext())
            ShouldBeTimeUp(await NewService(ctx).EndTest(s.InstanceId, UserId));
    }

    [Fact]
    public async Task EndTest_within_the_time_limit_completes()
    {
        var s = await SeedAsync(TimeSpan.FromSeconds(Limit - 60));

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).EndTest(s.InstanceId, UserId)).Success.ShouldBeTrue();

        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Completed);
    }

    [Fact]
    public async Task EndTest_by_another_user_does_not_expire_someone_elses_instance()
    {
        var s = await SeedAsync(Overdue);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).EndTest(s.InstanceId, userId: 999);
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeFalse();
        }

        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Started);
    }

    // ---- start-test and reads ----

    [Fact]
    public async Task StartTest_on_an_overdue_instance_marks_it_expired_as_the_student_and_returns_alreadyCompleted()
    {
        var s = await SeedAsync(Overdue);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).StartTestAsync(s.WorksheetId, new StudentProfileDto { Id = s.StudentId, GradeId = s.GradeId });
            result.Success.ShouldBeFalse();
            result.InstanceId.ShouldBe(s.InstanceId);
            result.Message.ShouldContain("tamamlan");
        }

        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        instance.UpdateUserId.ShouldBe(UserId);
        await using var check = _db.NewContext();
        (await check.TestInstances.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Reads_report_an_overdue_test_as_expired_without_writing_and_its_result_page_is_available()
    {
        var s = await SeedAsync(Overdue);

        await using (var ctx = _db.NewContext())
        {
            var solve = await NewService(ctx).GetCanvasTestResultAsync(s.InstanceId, UserId);
            solve!.Status.ShouldBe(WorksheetInstanceStatus.Expired);
            solve.RemainingSeconds.ShouldBe(0);
        }

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).GetTestInstanceQuestionsAsync(s.InstanceId, UserId))!.Status.ShouldBe(WorksheetInstanceStatus.Expired);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).GetCanvasTestResultAsync(s.InstanceId, UserId, includeCorrectAnswer: true);
            result.ShouldNotBeNull();
            result!.TestInstanceQuestions.ShouldHaveSingleItem().Question.CorrectAnswerId.ShouldBe(s.CorrectAnswerId);
        }

        // GET is pure: the row is still Started until save/end/start or the sweeper persists it.
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Started);
        instance.EndTime.ShouldBeNull();
    }

    [Fact]
    public async Task Reads_of_a_running_test_return_the_server_remaining_time()
    {
        var s = await SeedAsync(TimeSpan.FromSeconds(100));

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetTestInstanceQuestionsAsync(s.InstanceId, UserId);
        dto!.Status.ShouldBe(WorksheetInstanceStatus.Started);
        dto.RemainingSeconds!.Value.ShouldBeInRange(Limit - 102, Limit - 98);
    }

    [Fact]
    public async Task Correct_answers_stay_hidden_while_the_test_is_running()
    {
        var s = await SeedAsync(TimeSpan.FromSeconds(30));

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetCanvasTestResultAsync(s.InstanceId, UserId, includeCorrectAnswer: true)).ShouldBeNull();
    }

    // ---- sweeper ----

    private ExpiredTestInstanceSweepJob NewSweeper(AppDbContext ctx, int batchSize = 500) =>
        new(ctx, new StaticOptionsMonitor<ExpiredTestInstanceSweepOptions>(new ExpiredTestInstanceSweepOptions { BatchSize = batchSize }));

    [Fact]
    public async Task Sweeper_expires_only_overdue_started_instances_with_a_limit()
    {
        var overdue = await SeedAsync(Overdue);
        int running, unlimited, completed;
        await using (var ctx = _db.NewContext())
        {
            WorksheetInstance Add(TimeSpan ago, int? limit, WorksheetInstanceStatus status)
            {
                var ws = new Worksheet { Name = "W", Description = "", GradeId = overdue.GradeId, MaxDurationSeconds = Limit };
                ctx.Worksheets.Add(ws);
                ctx.SaveChanges();
                var i = new WorksheetInstance
                {
                    StudentId = overdue.StudentId, WorksheetId = ws.Id, StartTime = DateTime.UtcNow - ago, Status = status,
                    MaxDurationSeconds = limit, EndTime = status == WorksheetInstanceStatus.Started ? null : DateTime.UtcNow.AddDays(-1),
                };
                ctx.TestInstances.Add(i);
                ctx.SaveChanges();
                return i;
            }
            running = Add(TimeSpan.FromSeconds(Limit + 10), Limit, WorksheetInstanceStatus.Started).Id; // inside tolerance
            unlimited = Add(TimeSpan.FromDays(3), null, WorksheetInstanceStatus.Started).Id;
            completed = Add(Overdue, Limit, WorksheetInstanceStatus.Completed).Id;
        }

        await using (var ctx = _db.NewContext())
            (await NewSweeper(ctx).SweepAsync()).ShouldBe(1);

        var expired = await InstanceAsync(overdue.InstanceId);
        expired.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        expired.EndTime!.Value.ShouldBe(overdue.StartTime.AddSeconds(Limit), TimeSpan.FromMilliseconds(1));
        expired.UpdateUserId.ShouldBeNull();
        (await InstanceAsync(running)).Status.ShouldBe(WorksheetInstanceStatus.Started);
        (await InstanceAsync(unlimited)).Status.ShouldBe(WorksheetInstanceStatus.Started);
        (await InstanceAsync(completed)).Status.ShouldBe(WorksheetInstanceStatus.Completed);

        // Idempotent: a second run finds nothing.
        await using (var ctx = _db.NewContext())
            (await NewSweeper(ctx).SweepAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Sweeper_respects_the_batch_size()
    {
        var first = await SeedAsync(Overdue);
        await using (var ctx = _db.NewContext())
        {
            var ws = new Worksheet { Name = "W2", Description = "", GradeId = first.GradeId };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = first.StudentId, WorksheetId = ws.Id, StartTime = DateTime.UtcNow - Overdue - TimeSpan.FromMinutes(1),
                Status = WorksheetInstanceStatus.Started, MaxDurationSeconds = Limit,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewSweeper(ctx, batchSize: 1).SweepAsync()).ShouldBe(1);
        await using (var ctx = _db.NewContext())
            (await NewSweeper(ctx, batchSize: 1).SweepAsync()).ShouldBe(1);
        await using (var ctx = _db.NewContext())
            (await ctx.TestInstances.CountAsync(i => i.Status == WorksheetInstanceStatus.Started)).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
