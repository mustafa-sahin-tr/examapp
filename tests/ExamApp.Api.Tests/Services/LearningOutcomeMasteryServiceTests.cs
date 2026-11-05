using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #696: seviye eşikleri, son cevaplar, az cevap işareti, alt konuya düşüş, başlanmadı listesi, etkinlik ve boş cevap.</summary>
public sealed class LearningOutcomeMasteryServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly DateTime _to = DateTime.UtcNow;
    private readonly LearningOutcomeMasteryOptions _rules = new();
    private int _studentId;
    private int _gradeId;
    private int _otherGradeId;
    private int _mathId;
    private int _scienceId;
    private int _anglesId;
    private int _triangleId;
    private int _circleId;
    private int _worksheetCounter;

    public LearningOutcomeMasteryServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5. Sınıf" };
        var otherGrade = new Grade { Name = "6. Sınıf" };
        var math = new Subject { Name = "Matematik" };
        var science = new Subject { Name = "Fen" };
        ctx.AddRange(grade, otherGrade, math, science);
        ctx.SaveChanges();
        var angles = new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade.Id };
        ctx.Add(angles);
        ctx.SaveChanges();
        var triangle = new SubTopic { Name = "Üçgen", TopicId = angles.Id };
        var circle = new SubTopic { Name = "Çember", TopicId = angles.Id };
        ctx.AddRange(triangle, circle);
        var student = new Student { UserId = 601, StudentNumber = "S1", SchoolName = "s", GradeId = grade.Id };
        ctx.Add(student);
        ctx.SaveChanges();
        (_gradeId, _otherGradeId, _mathId, _scienceId) = (grade.Id, otherGrade.Id, math.Id, science.Id);
        (_anglesId, _triangleId, _circleId, _studentId) = (angles.Id, triangle.Id, circle.Id, student.Id);
    }

    public void Dispose() => _db.Dispose();

    private async Task<LearningOutcomeMasteryDto> GetAsync(int? subjectId = null, CurriculumVersion? curriculum = null)
    {
        await using var ctx = _db.NewContext();
        var options = Substitute.For<IOptionsMonitor<LearningOutcomeMasteryOptions>>();
        options.CurrentValue.Returns(_rules);
        return await new LearningOutcomeMasteryService(ctx, options).GetAsync(_studentId, _to, subjectId, curriculum);
    }

    private async Task<int> OutcomeAsync(string code, int? subTopicId = null, int? gradeId = null, CurriculumVersion curriculum = CurriculumVersion.Maarif)
    {
        await using var ctx = _db.NewContext();
        var outcome = new LearningOutcome { Code = code, Description = $"Kazanım {code}", GradeId = gradeId ?? _gradeId, SubjectId = _mathId, SubTopicId = subTopicId, CurriculumVersion = curriculum };
        ctx.Add(outcome);
        await ctx.SaveChangesAsync();
        return outcome.Id;
    }

    private async Task<int> QuestionAsync(int? outcomeId = null, int? topicId = null, int[]? subTopics = null, int? subjectId = null, string interaction = "mcq")
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = 5, InteractionType = interaction, TopicId = topicId, SubjectId = subjectId };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        if (outcomeId is { } id)
            ctx.Add(new QuestionLearningOutcome { QuestionId = question.Id, LearningOutcomeId = id });
        foreach (var subTopic in subTopics ?? [])
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = question.Id, SubTopicId = subTopic });
        await ctx.SaveChangesAsync();
        return question.Id;
    }

    private async Task PracticeAsync(int questionId, bool correct, DateTime? at = null, bool skipped = false)
    {
        await using var ctx = _db.NewContext();
        var moment = at ?? _to.AddDays(-1);
        var session = new PracticeSession { StudentId = _studentId, GradeId = _gradeId, StartTime = moment };
        session.Questions.Add(new PracticeSessionQuestion { QuestionId = questionId, ShownAt = moment, AnsweredAt = skipped ? null : moment, IsCorrect = correct, IsSkipped = skipped, TimeTaken = 10 });
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    private async Task PracticeManyAsync(int questionId, int correct, int wrong, DateTime? at = null)
    {
        for (var i = 0; i < correct; i++)
            await PracticeAsync(questionId, true, at);
        for (var i = 0; i < wrong; i++)
            await PracticeAsync(questionId, false, at);
    }

    private static LearningOutcomeMasteryItemDto ItemOf(LearningOutcomeMasteryDto report, int outcomeId) =>
        report.Items.Single(i => i.LearningOutcomeId == outcomeId);

    [Theory]
    [InlineData(0, 0, LearningOutcomeMasteryLevel.NotStarted)]
    [InlineData(1, 2, LearningOutcomeMasteryLevel.Familiar)]
    [InlineData(6, 4, LearningOutcomeMasteryLevel.Familiar)] // %60
    [InlineData(7, 3, LearningOutcomeMasteryLevel.Proficient)] // tam %70
    [InlineData(5, 0, LearningOutcomeMasteryLevel.Proficient)] // %100 ama 10 cevap yok
    [InlineData(9, 1, LearningOutcomeMasteryLevel.Mastered)] // tam %90, tam 10 cevap
    [InlineData(17, 3, LearningOutcomeMasteryLevel.Proficient)] // %85
    public void Level_follows_the_documented_thresholds(int correct, int wrong, LearningOutcomeMasteryLevel expected) =>
        LearningOutcomeMasteryService.LevelOf(correct + wrong, correct, new LearningOutcomeMasteryOptions()).ShouldBe(expected);

    [Fact]
    public async Task Thresholds_come_from_the_settings()
    {
        _rules.ProficientPct = 50;
        var outcome = await OutcomeAsync("M.1");
        await PracticeManyAsync(await QuestionAsync(outcome), correct: 3, wrong: 3);

        var item = ItemOf(await GetAsync(), outcome);

        item.Level.ShouldBe(LearningOutcomeMasteryLevel.Proficient);
        (await GetAsync()).Rules.ProficientPct.ShouldBe(50);
    }

    [Fact]
    public async Task Few_answers_are_marked_tentative_and_enough_answers_are_not()
    {
        var few = await OutcomeAsync("M.1");
        var enough = await OutcomeAsync("M.2");
        await PracticeManyAsync(await QuestionAsync(few), correct: 4, wrong: 0);
        await PracticeManyAsync(await QuestionAsync(enough), correct: 5, wrong: 0);

        var report = await GetAsync();

        ItemOf(report, few).IsTentative.ShouldBeTrue();
        ItemOf(report, enough).IsTentative.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_most_recent_answers_count_so_old_mistakes_drop_out()
    {
        _rules.RecentAnswers = 10;
        var outcome = await OutcomeAsync("M.1");
        var question = await QuestionAsync(outcome);
        await PracticeManyAsync(question, correct: 0, wrong: 10, at: _to.AddDays(-20));
        await PracticeManyAsync(question, correct: 10, wrong: 0, at: _to.AddDays(-1));

        var item = ItemOf(await GetAsync(), outcome);

        (item.Answered, item.Correct, item.AccuracyPct, item.Level).ShouldBe((10, 10, 100m, LearningOutcomeMasteryLevel.Mastered));
    }

    [Fact]
    public async Task Answers_older_than_the_lookback_or_before_a_reset_are_not_read()
    {
        var outcome = await OutcomeAsync("M.1");
        var question = await QuestionAsync(outcome);
        await PracticeAsync(question, true, _to.AddDays(-_rules.LookbackDays - 1));
        var afterReset = await OutcomeAsync("M.2");
        var resetQuestion = await QuestionAsync(afterReset);
        await PracticeAsync(resetQuestion, true, _to.AddDays(-10));
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Students.FindAsync(_studentId))!.ProgressResetAtUtc = _to.AddDays(-5);
            await ctx.SaveChangesAsync();
        }

        var report = await GetAsync();

        ItemOf(report, outcome).Level.ShouldBe(LearningOutcomeMasteryLevel.NotStarted);
        ItemOf(report, afterReset).Level.ShouldBe(LearningOutcomeMasteryLevel.NotStarted);
    }

    [Fact]
    public async Task Unanswered_outcomes_of_the_grade_show_as_not_started_and_other_grades_stay_out()
    {
        var own = await OutcomeAsync("M.1");
        await OutcomeAsync("X.1", gradeId: _otherGradeId);

        var report = await GetAsync();

        var item = ItemOf(report, own);
        (item.Level, item.Answered, item.AccuracyPct, item.IsTentative).ShouldBe((LearningOutcomeMasteryLevel.NotStarted, 0, (decimal?)null, false));
        report.Items.ShouldNotContain(i => i.Code == "X.1");
    }

    [Fact]
    public async Task Answered_outcome_of_another_grade_stays_visible()
    {
        var other = await OutcomeAsync("X.1", gradeId: _otherGradeId);
        await PracticeAsync(await QuestionAsync(other), true);

        ItemOf(await GetAsync(), other).Answered.ShouldBe(1);
    }

    [Fact]
    public async Task Question_without_an_outcome_falls_back_to_each_subtopic_then_to_its_topic()
    {
        await PracticeAsync(await QuestionAsync(subTopics: [_triangleId, _circleId]), true);
        await PracticeAsync(await QuestionAsync(topicId: _anglesId), false);

        var report = await GetAsync();

        report.Items.Single(i => i.SubTopicId == _triangleId).Kind.ShouldBe(MasteryItemKind.SubTopic);
        report.Items.Single(i => i.SubTopicId == _circleId).Answered.ShouldBe(1);
        var topic = report.Items.Single(i => i.Kind == MasteryItemKind.Topic);
        (topic.TopicId, topic.SubjectId, topic.Answered, topic.Correct).ShouldBe(((int?)_anglesId, (int?)_mathId, 1, 0));
    }

    [Fact]
    public async Task Question_with_an_outcome_counts_only_for_the_outcome_not_its_subtopics()
    {
        var outcome = await OutcomeAsync("M.1", _triangleId);
        await PracticeAsync(await QuestionAsync(outcome, subTopics: [_triangleId]), true);

        var report = await GetAsync();

        ItemOf(report, outcome).Answered.ShouldBe(1);
        report.Items.ShouldNotContain(i => i.Kind == MasteryItemKind.SubTopic && i.SubTopicId == _triangleId);
    }

    [Fact]
    public async Task Subtopic_with_outcomes_is_not_listed_twice_while_unanswered()
    {
        await OutcomeAsync("M.1", _triangleId);

        var report = await GetAsync();

        report.Items.ShouldNotContain(i => i.Kind == MasteryItemKind.SubTopic && i.SubTopicId == _triangleId);
        report.Items.Single(i => i.Kind == MasteryItemKind.SubTopic && i.SubTopicId == _circleId).Level.ShouldBe(LearningOutcomeMasteryLevel.NotStarted);
    }

    [Fact]
    public async Task Activity_answers_and_skips_do_not_affect_levels()
    {
        var outcome = await OutcomeAsync("M.1");
        var question = await QuestionAsync(outcome);
        await PracticeAsync(question, false, skipped: true);
        await using (var ctx = _db.NewContext())
        {
            var worksheet = new Worksheet { Name = $"Etkinlik {++_worksheetCounter}", Description = "d", GradeId = _gradeId, Kind = WorksheetKind.Activity };
            ctx.Worksheets.Add(worksheet);
            await ctx.SaveChangesAsync();
            var item = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question, Order = 1 };
            ctx.Add(item);
            await ctx.SaveChangesAsync();
            var instance = new WorksheetInstance { StudentId = _studentId, WorksheetId = worksheet.Id, StartTime = _to.AddDays(-2), EndTime = _to.AddDays(-2).AddMinutes(5), Status = WorksheetInstanceStatus.Completed };
            ctx.TestInstances.Add(instance);
            await ctx.SaveChangesAsync();
            ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = item.Id, AnswerPayload = "{}", IsCorrect = true, TimeTaken = 5 });
            await ctx.SaveChangesAsync();
        }

        ItemOf(await GetAsync(), outcome).Level.ShouldBe(LearningOutcomeMasteryLevel.NotStarted);
    }

    [Fact]
    public async Task Filters_narrow_by_subject_and_curriculum()
    {
        var maarif = await OutcomeAsync("M.1");
        var previous = await OutcomeAsync("P.1", curriculum: CurriculumVersion.Previous);

        var report = await GetAsync(curriculum: CurriculumVersion.Maarif);
        report.Items.ShouldContain(i => i.LearningOutcomeId == maarif);
        report.Items.ShouldNotContain(i => i.LearningOutcomeId == previous);

        (await GetAsync(subjectId: _scienceId)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Question_with_no_classification_is_reported_as_unclassified()
    {
        await PracticeAsync(await QuestionAsync(subjectId: _mathId), true);

        var report = await GetAsync();

        report.UnclassifiedCount.ShouldBe(1);
        (await GetAsync(subjectId: _scienceId)).UnclassifiedCount.ShouldBe(0);
    }
}
