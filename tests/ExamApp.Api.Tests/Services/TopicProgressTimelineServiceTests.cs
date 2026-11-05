using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #531: hafta sınırı, cevapsız hafta, az cevap işareti, test + pratik birleşimi, hedef süzgeci.</summary>
public sealed class TopicProgressTimelineServiceTests : IDisposable
{
    // Çarşamba; haftası 14 Eylül 2026 Pazartesi başlar.
    private static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly ThisWeek = new(2026, 9, 14);

    private readonly TestDb _db = TestDb.Create();
    private readonly int _studentId;
    private readonly int _otherStudentId;
    private readonly int _gradeId;
    private readonly int _anglesId;
    private readonly int _cellsId;
    private readonly int _triangleId;
    private readonly int _circleId;

    public TopicProgressTimelineServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        var math = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, math);
        ctx.SaveChanges();
        var angles = new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade.Id };
        var cells = new Topic { Name = "Hücre", SubjectId = math.Id, GradeId = grade.Id };
        ctx.AddRange(angles, cells);
        ctx.SaveChanges();
        var triangle = new SubTopic { Name = "Üçgen", TopicId = angles.Id };
        var circle = new SubTopic { Name = "Çember", TopicId = angles.Id };
        var student = new Student { UserId = 701, StudentNumber = "S1", SchoolName = "s", GradeId = grade.Id };
        var other = new Student { UserId = 702, StudentNumber = "S2", SchoolName = "s", GradeId = grade.Id };
        ctx.AddRange(triangle, circle, student, other);
        ctx.SaveChanges();
        (_gradeId, _anglesId, _cellsId, _triangleId, _circleId) = (grade.Id, angles.Id, cells.Id, triangle.Id, circle.Id);
        (_studentId, _otherStudentId) = (student.Id, other.Id);
    }

    public void Dispose() => _db.Dispose();

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private async Task<TopicProgressTimelineDto> GetAsync(int? topicId = null, int? subTopicId = null, int weeks = 4)
    {
        await using var ctx = _db.NewContext();
        var calendar = new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, new FixedClock(Now));
        return await new TopicProgressTimelineService(ctx, calendar).GetAsync(_studentId, topicId, subTopicId, weeks);
    }

    private async Task<int> QuestionAsync(int? topicId = null, int[]? subTopics = null)
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = 1, InteractionType = "mcq", TopicId = topicId };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        foreach (var subTopic in subTopics ?? [])
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = question.Id, SubTopicId = subTopic });
        await ctx.SaveChangesAsync();
        return question.Id;
    }

    private async Task PracticeAsync(int questionId, bool correct, DateTime at, bool skipped = false, int? studentId = null)
    {
        await using var ctx = _db.NewContext();
        var session = new PracticeSession { StudentId = studentId ?? _studentId, GradeId = _gradeId, StartTime = at };
        session.Questions.Add(new PracticeSessionQuestion { QuestionId = questionId, ShownAt = at, AnsweredAt = at, IsCorrect = correct, IsSkipped = skipped });
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    private async Task TestAsync(int questionId, bool correct, DateTime finishedAt, WorksheetInstanceStatus status = WorksheetInstanceStatus.Completed)
    {
        await using var ctx = _db.NewContext();
        var right = new Answer { QuestionId = questionId, Text = "doğru", Order = 1 };
        var wrong = new Answer { QuestionId = questionId, Text = "yanlış", Order = 2 };
        ctx.Answers.AddRange(right, wrong);
        await ctx.SaveChangesAsync();
        (await ctx.Questions.FindAsync(questionId))!.CorrectAnswerId = right.Id;
        var worksheet = new Worksheet { Name = "Test", Description = "d", GradeId = _gradeId };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        var item = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
        ctx.Add(item);
        var instance = new WorksheetInstance { StudentId = _studentId, WorksheetId = worksheet.Id, StartTime = finishedAt.AddMinutes(-5), EndTime = finishedAt, Status = status };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = item.Id, SelectedAnswerId = correct ? right.Id : wrong.Id });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Every_week_is_listed_oldest_first_and_a_week_without_answers_has_no_accuracy()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q, true, Now.AddDays(-7));

        var result = await GetAsync(_anglesId);

        result.Weeks.Select(w => w.WeekStart).ShouldBe([ThisWeek.AddDays(-21), ThisWeek.AddDays(-14), ThisWeek.AddDays(-7), ThisWeek]);
        result.Weeks[0].AccuracyPct.ShouldBeNull();
        result.Weeks[0].Answered.ShouldBe(0);
        result.Weeks[0].LowSample.ShouldBeFalse();
        result.Weeks[2].AccuracyPct.ShouldBe(100m);
        result.Weeks[3].AccuracyPct.ShouldBeNull();
    }

    [Fact]
    public async Task Tests_and_practice_are_counted_together()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q, correct: true, Now.AddDays(-1));
        await PracticeAsync(q, correct: false, Now.AddHours(-2));
        await PracticeAsync(q, correct: true, Now.AddHours(-1));

        var week = (await GetAsync(_anglesId)).Weeks[^1];

        (week.Answered, week.Correct).ShouldBe((3, 2));
        week.AccuracyPct.ShouldBe(66.7m);
        week.LowSample.ShouldBeFalse();
    }

    [Fact]
    public async Task The_week_changes_at_local_midnight_on_monday_not_at_utc_midnight()
    {
        var q = await QuestionAsync(_anglesId);
        // Istanbul UTC+3: Pazar 23:59 yerel.
        await PracticeAsync(q, true, new DateTime(2026, 9, 13, 20, 59, 0, DateTimeKind.Utc));
        // Pazartesi 00:00 yerel, UTC'de hâlâ Pazar.
        await PracticeAsync(q, false, new DateTime(2026, 9, 13, 21, 0, 0, DateTimeKind.Utc));

        var weeks = (await GetAsync(_anglesId, weeks: 2)).Weeks;

        (weeks[0].WeekStart, weeks[0].Correct, weeks[0].Answered).ShouldBe((ThisWeek.AddDays(-7), 1, 1));
        (weeks[1].WeekStart, weeks[1].Correct, weeks[1].Answered).ShouldBe((ThisWeek, 0, 1));
    }

    [Fact]
    public async Task Answers_before_the_first_week_are_left_out()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q, true, new DateTime(2026, 9, 6, 20, 59, 0, DateTimeKind.Utc));

        var weeks = (await GetAsync(_anglesId, weeks: 2)).Weeks;

        weeks.ShouldAllBe(w => w.Answered == 0);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task A_week_with_few_answers_is_marked(int answers, bool lowSample)
    {
        var q = await QuestionAsync(_anglesId);
        for (var i = 0; i < answers; i++)
            await PracticeAsync(q, true, Now.AddHours(-1 - i));

        (await GetAsync(_anglesId)).Weeks[^1].LowSample.ShouldBe(lowSample);
    }

    [Fact]
    public async Task A_skipped_question_is_not_an_answer()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q, false, Now.AddHours(-1), skipped: true);

        var week = (await GetAsync(_anglesId)).Weeks[^1];

        week.Answered.ShouldBe(0);
        week.AccuracyPct.ShouldBeNull();
    }

    [Fact]
    public async Task A_running_test_is_not_counted()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q, correct: true, DateTime.UtcNow.AddMinutes(1), WorksheetInstanceStatus.Started);

        (await GetAsync(_anglesId)).Weeks.ShouldAllBe(w => w.Answered == 0);
    }

    [Fact]
    public async Task A_topic_includes_questions_known_only_by_their_subtopic_and_leaves_out_other_topics()
    {
        var bySubTopic = await QuestionAsync(subTopics: [_triangleId]);
        var otherTopic = await QuestionAsync(_cellsId);
        await PracticeAsync(bySubTopic, true, Now.AddHours(-1));
        await PracticeAsync(otherTopic, true, Now.AddHours(-1));

        var result = await GetAsync(_anglesId);

        (result.TopicId, result.SubTopicId).ShouldBe(((int?)_anglesId, (int?)null));
        result.Weeks[^1].Answered.ShouldBe(1);
    }

    [Fact]
    public async Task A_subtopic_counts_only_its_own_questions()
    {
        var triangle = await QuestionAsync(_anglesId, [_triangleId]);
        var circle = await QuestionAsync(_anglesId, [_circleId]);
        await PracticeAsync(triangle, true, Now.AddHours(-1));
        await PracticeAsync(circle, false, Now.AddHours(-1));

        var result = await GetAsync(subTopicId: _triangleId);

        (result.TopicId, result.SubTopicId).ShouldBe(((int?)null, (int?)_triangleId));
        (result.Weeks[^1].Answered, result.Weeks[^1].Correct).ShouldBe((1, 1));
    }

    [Fact]
    public async Task Another_student_answers_do_not_leak()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q, true, Now.AddHours(-1), studentId: _otherStudentId);

        (await GetAsync(_anglesId)).Weeks[^1].Answered.ShouldBe(0);
    }
}
