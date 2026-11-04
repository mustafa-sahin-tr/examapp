using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #497: pratik ve test birleşimi, konu düşüşü, süren oturum, eşik ve gruplama.</summary>
public sealed class TopicPerformanceServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly DateTime _to = DateTime.UtcNow;
    private readonly DateTime _from;
    private int _studentId;
    private int _otherStudentId;
    private int _gradeId;
    private int _mathId;
    private int _scienceId;
    private int _anglesId;
    private int _cellsId;
    private int _triangleId;
    private int _circleId;
    private int _worksheetCounter;

    public TopicPerformanceServiceTests()
    {
        _from = _to.AddDays(-30);
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        var math = new Subject { Name = "Matematik" };
        var science = new Subject { Name = "Fen" };
        ctx.AddRange(grade, math, science);
        ctx.SaveChanges();
        var angles = new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade.Id };
        var cells = new Topic { Name = "Hücre", SubjectId = science.Id, GradeId = grade.Id };
        ctx.AddRange(angles, cells);
        ctx.SaveChanges();
        var triangle = new SubTopic { Name = "Üçgen", TopicId = angles.Id };
        var circle = new SubTopic { Name = "Çember", TopicId = angles.Id };
        ctx.AddRange(triangle, circle);
        var student = new Student { UserId = 501, StudentNumber = "S1", SchoolName = "s", GradeId = grade.Id };
        var other = new Student { UserId = 502, StudentNumber = "S2", SchoolName = "s", GradeId = grade.Id };
        ctx.AddRange(student, other);
        ctx.SaveChanges();
        (_gradeId, _mathId, _scienceId, _anglesId, _cellsId) = (grade.Id, math.Id, science.Id, angles.Id, cells.Id);
        (_triangleId, _circleId, _studentId, _otherStudentId) = (triangle.Id, circle.Id, student.Id, other.Id);
    }

    public void Dispose() => _db.Dispose();

    private async Task<TopicPerformanceDto> GetAsync(TopicPerformanceGroupBy groupBy = TopicPerformanceGroupBy.Topic, int? subjectId = null, int minQuestions = 1)
    {
        await using var ctx = _db.NewContext();
        return await new TopicPerformanceService(ctx).GetAsync(_studentId, _from, _to, subjectId, groupBy, minQuestions);
    }

    private async Task<(int Id, int CorrectAnswer, int WrongAnswer)> QuestionAsync(int? topicId = null, int? subjectId = null, int[]? subTopics = null, int difficulty = 1, string? interaction = "mcq")
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = difficulty, InteractionType = interaction, TopicId = topicId, SubjectId = subjectId };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        var right = new Answer { QuestionId = question.Id, Text = "doğru", Order = 1 };
        var wrong = new Answer { QuestionId = question.Id, Text = "yanlış", Order = 2 };
        ctx.Answers.AddRange(right, wrong);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = right.Id;
        foreach (var subTopic in subTopics ?? [])
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = question.Id, SubTopicId = subTopic });
        await ctx.SaveChangesAsync();
        return (question.Id, right.Id, wrong.Id);
    }

    private async Task PracticeAsync(int questionId, bool correct, DateTime? at = null, bool skipped = false, bool answered = true, int timeTaken = 10, int? studentId = null)
    {
        await using var ctx = _db.NewContext();
        var moment = at ?? _to.AddDays(-1);
        var session = new PracticeSession { StudentId = studentId ?? _studentId, GradeId = _gradeId, StartTime = moment };
        session.Questions.Add(new PracticeSessionQuestion
        {
            QuestionId = questionId,
            ShownAt = moment,
            AnsweredAt = answered ? moment : null,
            IsCorrect = correct,
            IsSkipped = skipped,
            TimeTaken = timeTaken,
        });
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    private async Task<int> TestAsync(int questionId, int? selectedAnswerId, WorksheetInstanceStatus status = WorksheetInstanceStatus.Completed, DateTime? endTime = null, int? maxDuration = null, DateTime? start = null, WorksheetKind kind = WorksheetKind.Test, int timeTaken = 20, string? payload = null, bool storedCorrect = false)
    {
        await using var ctx = _db.NewContext();
        var worksheet = new Worksheet { Name = $"Test {++_worksheetCounter}", Description = "d", GradeId = _gradeId, Kind = kind };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        var item = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
        ctx.Add(item);
        await ctx.SaveChangesAsync();
        var begin = start ?? _to.AddDays(-2);
        var instance = new WorksheetInstance
        {
            StudentId = _studentId,
            WorksheetId = worksheet.Id,
            StartTime = begin,
            Status = status,
            MaxDurationSeconds = maxDuration,
            EndTime = status == WorksheetInstanceStatus.Completed ? endTime ?? begin.AddMinutes(5) : endTime,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = item.Id, SelectedAnswerId = selectedAnswerId, TimeTaken = timeTaken, AnswerPayload = payload, IsCorrect = storedCorrect });
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    [Fact]
    public async Task Tests_and_practice_add_up_under_the_same_topic_with_percentages_from_the_server()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q.Id, q.CorrectAnswer, timeTaken: 20);
        await TestAsync(q.Id, q.WrongAnswer, timeTaken: 40);
        await TestAsync(q.Id, null);
        await PracticeAsync(q.Id, correct: true, timeTaken: 30);
        await PracticeAsync(q.Id, correct: false, skipped: true);

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        (item.Correct, item.Wrong, item.Blank, item.Total).ShouldBe((2, 1, 2, 5));
        item.AccuracyPct.ShouldBe(66.7m); // boşlar doğruluğa girmez
        item.AvgTimeSec.ShouldBe(30m); // boş satırlar ortalamaya girmez
        (item.SubjectId, item.SubjectName, item.TopicId, item.TopicName).ShouldBe((_mathId, "Matematik", _anglesId, "Açılar"));
    }

    [Fact]
    public async Task A_question_without_a_topic_falls_back_to_its_subtopics_topic()
    {
        var q = await QuestionAsync(subTopics: [_triangleId]);
        await PracticeAsync(q.Id, correct: true);

        var result = await GetAsync();

        result.Items.ShouldHaveSingleItem().TopicId.ShouldBe(_anglesId);
        result.UnclassifiedCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_question_with_no_topic_is_counted_once_as_unclassified_and_not_listed()
    {
        var q = await QuestionAsync(subjectId: _mathId);
        await PracticeAsync(q.Id, correct: true);
        await PracticeAsync(q.Id, correct: false);

        var result = await GetAsync();

        result.Items.ShouldBeEmpty();
        result.UnclassifiedCount.ShouldBe(1);
        (await GetAsync(TopicPerformanceGroupBy.Difficulty)).Items.ShouldHaveSingleItem().Total.ShouldBe(2);
    }

    [Fact]
    public async Task A_running_test_stays_out_but_a_time_limited_one_that_ran_out_counts()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q.Id, q.CorrectAnswer, WorksheetInstanceStatus.Started, start: _to.AddMinutes(-1), maxDuration: 3600);
        await TestAsync(q.Id, q.CorrectAnswer, WorksheetInstanceStatus.Started, start: _to.AddDays(-5));

        (await GetAsync()).Items.ShouldBeEmpty();

        await TestAsync(q.Id, q.WrongAnswer, WorksheetInstanceStatus.Started, start: _to.AddDays(-3), maxDuration: 600);

        (await GetAsync()).Items.ShouldHaveSingleItem().Wrong.ShouldBe(1);
    }

    [Fact]
    public async Task Activities_and_open_text_questions_are_not_scored()
    {
        var q = await QuestionAsync(_anglesId);
        var openText = await QuestionAsync(_anglesId, interaction: "openText");
        await TestAsync(q.Id, q.CorrectAnswer, kind: WorksheetKind.Activity);
        await PracticeAsync(openText.Id, correct: true);

        (await GetAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_answered_question_later_deleted_counts_as_wrong_like_the_result_screen()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q.Id, q.CorrectAnswer);
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Remove(await ctx.Questions.SingleAsync(x => x.Id == q.Id));
            await ctx.SaveChangesAsync();
        }

        (await GetAsync()).Items.ShouldHaveSingleItem().Wrong.ShouldBe(1);
    }

    [Fact]
    public async Task An_item_later_removed_from_the_test_still_counts()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q.Id, q.CorrectAnswer);
        await using (var ctx = _db.NewContext())
        {
            ctx.Remove(await ctx.Set<WorksheetQuestion>().SingleAsync(x => x.QuestionId == q.Id));
            await ctx.SaveChangesAsync();
        }

        (await GetAsync()).Items.ShouldHaveSingleItem().Correct.ShouldBe(1);
    }

    [Fact]
    public async Task A_shown_but_unanswered_practice_question_is_not_counted()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: false, answered: false);

        (await GetAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_students_answers_and_answers_outside_the_window_are_ignored()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: true, studentId: _otherStudentId);
        await PracticeAsync(q.Id, correct: true, at: _from.AddDays(-40));

        (await GetAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Trend_compares_with_the_previous_window_of_the_same_length()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: true);
        await PracticeAsync(q.Id, correct: true);
        await PracticeAsync(q.Id, correct: true, at: _from.AddDays(-1));
        await PracticeAsync(q.Id, correct: false, at: _from.AddDays(-2));

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        item.Total.ShouldBe(2);
        (item.AccuracyPct, item.PrevAnswered, item.PrevAccuracyPct, item.Trend).ShouldBe((100m, 2, 50m, 50m));
    }

    [Fact]
    public async Task No_previous_answers_means_no_trend_and_a_topic_only_in_the_previous_window_is_not_listed()
    {
        var angle = await QuestionAsync(_anglesId);
        var cell = await QuestionAsync(_cellsId);
        await PracticeAsync(angle.Id, correct: true);
        await PracticeAsync(cell.Id, correct: true, at: _from.AddDays(-1));

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        item.TopicId.ShouldBe(_anglesId);
        item.PrevAccuracyPct.ShouldBeNull();
        item.Trend.ShouldBeNull();
    }

    [Fact]
    public async Task Trend_is_taken_from_unrounded_values()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: true);
        await PracticeAsync(q.Id, correct: false);
        await PracticeAsync(q.Id, correct: false);
        await PracticeAsync(q.Id, correct: true, at: _from.AddDays(-1));
        await PracticeAsync(q.Id, correct: true, at: _from.AddDays(-2));
        await PracticeAsync(q.Id, correct: false, at: _from.AddDays(-3));

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        (item.AccuracyPct, item.PrevAccuracyPct, item.Trend).ShouldBe((33.3m, 66.7m, -33.3m));
    }

    [Fact]
    public async Task A_test_in_the_previous_window_feeds_the_trend_not_the_counts()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: true);
        await TestAsync(q.Id, q.WrongAnswer, start: _from.AddDays(-2));

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        (item.Total, item.PrevAnswered, item.PrevAccuracyPct).ShouldBe((1, 1, 0m));
    }

    [Fact]
    public async Task An_answer_at_or_after_the_end_of_the_window_is_ignored()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: true, at: _to);

        (await GetAsync()).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_abandoned_test_does_not_make_a_topic_look_weak()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q.Id, null);
        await TestAsync(q.Id, null);

        var result = await GetAsync();

        result.Items.ShouldBeEmpty();
        result.UnclassifiedCount.ShouldBe(0);
    }

    [Fact]
    public async Task Interactive_answers_use_the_stored_result()
    {
        var q = await QuestionAsync(_anglesId, interaction: "dragDrop");
        await TestAsync(q.Id, null, payload: "{}", storedCorrect: true);
        await TestAsync(q.Id, null, payload: "{}", storedCorrect: false);

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        (item.Correct, item.Wrong, item.Blank).ShouldBe((1, 1, 0));
    }

    [Fact]
    public async Task Answers_before_a_progress_reset_are_ignored()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: false, at: _to.AddDays(-5));
        await using (var ctx = _db.NewContext())
        {
            var student = await ctx.Students.SingleAsync(s => s.Id == _studentId);
            student.ProgressResetAtUtc = _to.AddDays(-3);
            await ctx.SaveChangesAsync();
        }
        await PracticeAsync(q.Id, correct: true, at: _to.AddDays(-1));

        var item = (await GetAsync()).Items.ShouldHaveSingleItem();

        (item.Correct, item.Wrong, item.PrevAnswered).ShouldBe((1, 0, 0));
    }

    [Fact]
    public async Task A_test_deleted_later_still_counts_for_the_student()
    {
        var q = await QuestionAsync(_anglesId);
        await TestAsync(q.Id, q.CorrectAnswer);
        await using (var ctx = _db.NewContext())
        {
            ctx.Worksheets.Remove(await ctx.Worksheets.SingleAsync());
            await ctx.SaveChangesAsync();
        }

        (await GetAsync()).Items.ShouldHaveSingleItem().Correct.ShouldBe(1);
    }

    [Fact]
    public async Task The_subject_filter_applies_to_the_subtopics_own_subject()
    {
        int cellPart;
        await using (var ctx = _db.NewContext())
        {
            var part = new SubTopic { Name = "Organel", TopicId = _cellsId };
            ctx.Add(part);
            await ctx.SaveChangesAsync();
            cellPart = part.Id;
        }
        var q = await QuestionAsync(_anglesId, subTopics: [_triangleId, cellPart]);
        await PracticeAsync(q.Id, correct: true);

        var math = await GetAsync(TopicPerformanceGroupBy.SubTopic, subjectId: _mathId);

        math.Items.ShouldHaveSingleItem().SubTopicId.ShouldBe(_triangleId);
    }

    [Fact]
    public async Task A_subtopic_written_twice_counts_once()
    {
        var q = await QuestionAsync(_anglesId, subTopics: [_triangleId, _triangleId]);
        await PracticeAsync(q.Id, correct: true);

        (await GetAsync(TopicPerformanceGroupBy.SubTopic)).Items.ShouldHaveSingleItem().Total.ShouldBe(1);
    }

    [Fact]
    public async Task A_parents_window_is_cut_to_the_hour_so_polling_cannot_reveal_when_an_answer_was_given()
    {
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(1, 2, Arg.Any<CancellationToken>()).Returns(new ParentChildAccessGrant(1, 9, 2, 3, null, null));
        var performance = Substitute.For<ITopicPerformanceService>();
        var service = new ParentTopicPerformanceService(access, Substitute.For<IParentAccessAuditLog>(), performance);

        await service.GetAsync(1, 2, 7, null, TopicPerformanceGroupBy.Topic, 1);

        var call = performance.ReceivedCalls().ShouldHaveSingleItem().GetArguments();
        call[0].ShouldBe(2);
        var (from, to) = ((DateTime)call[1]!, (DateTime)call[2]!);
        (to.Minute, to.Second, to.Millisecond).ShouldBe((0, 0, 0));
        (to - from).ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    public void GroupBy_is_written_with_the_same_names_the_request_uses()
    {
        System.Text.Json.JsonSerializer.Serialize(new TopicPerformanceDto { GroupBy = TopicPerformanceGroupBy.InteractionType })
            .ShouldContain("\"GroupBy\":\"interactionType\"");
    }

    [Fact]
    public async Task MinQuestions_counts_answered_questions_only()
    {
        var q = await QuestionAsync(_anglesId);
        await PracticeAsync(q.Id, correct: true);
        await TestAsync(q.Id, null);

        (await GetAsync(minQuestions: 2)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task MinQuestions_hides_groups_with_too_few_questions()
    {
        var angle = await QuestionAsync(_anglesId);
        var cell = await QuestionAsync(_cellsId);
        await PracticeAsync(angle.Id, correct: true);
        await PracticeAsync(angle.Id, correct: true);
        await PracticeAsync(cell.Id, correct: true);

        (await GetAsync(minQuestions: 2)).Items.ShouldHaveSingleItem().TopicId.ShouldBe(_anglesId);
    }

    [Fact]
    public async Task SubjectId_filter_and_weakest_first_order()
    {
        var angle = await QuestionAsync(_anglesId);
        var cell = await QuestionAsync(_cellsId);
        await PracticeAsync(angle.Id, correct: true);
        await PracticeAsync(cell.Id, correct: false);

        (await GetAsync()).Items.Select(i => i.TopicId).ShouldBe([_cellsId, _anglesId]);
        (await GetAsync(subjectId: _mathId)).Items.ShouldHaveSingleItem().TopicId.ShouldBe(_anglesId);
        (await GetAsync(subjectId: _scienceId)).Items.ShouldHaveSingleItem().TopicId.ShouldBe(_cellsId);
    }

    [Fact]
    public async Task Grouping_by_subtopic_counts_each_subtopic_and_keeps_topic_only_questions_under_the_topic()
    {
        var both = await QuestionAsync(_anglesId, subTopics: [_triangleId, _circleId]);
        var topicOnly = await QuestionAsync(_anglesId);
        await PracticeAsync(both.Id, correct: true);
        await PracticeAsync(topicOnly.Id, correct: false);

        var items = (await GetAsync(TopicPerformanceGroupBy.SubTopic)).Items;

        items.Count.ShouldBe(3);
        items.Single(i => i.SubTopicId == _triangleId).SubTopicName.ShouldBe("Üçgen");
        items.Single(i => i.SubTopicId == _circleId).Correct.ShouldBe(1);
        items.Single(i => i.SubTopicId == null).Wrong.ShouldBe(1);
    }

    [Theory]
    [InlineData(1, "easy")]
    [InlineData(3, "easy")]
    [InlineData(4, "medium")]
    [InlineData(6, "medium")]
    [InlineData(7, "hard")]
    [InlineData(10, "hard")]
    public async Task Grouping_by_difficulty_uses_bands(int difficulty, string band)
    {
        var q = await QuestionAsync(_anglesId, difficulty: difficulty);
        await PracticeAsync(q.Id, correct: true);

        var item = (await GetAsync(TopicPerformanceGroupBy.Difficulty)).Items.ShouldHaveSingleItem();

        item.DifficultyBand.ShouldBe(band);
        item.TopicId.ShouldBeNull();
    }

    [Fact]
    public async Task Grouping_by_interaction_type_treats_a_missing_type_as_mcq()
    {
        var plain = await QuestionAsync(_anglesId, interaction: null);
        var drag = await QuestionAsync(_anglesId, interaction: "dragDrop");
        await PracticeAsync(plain.Id, correct: true);
        await PracticeAsync(drag.Id, correct: false);

        var items = (await GetAsync(TopicPerformanceGroupBy.InteractionType)).Items;

        items.Select(i => i.InteractionType).ShouldBe(["dragDrop", "mcq"]);
    }
}
