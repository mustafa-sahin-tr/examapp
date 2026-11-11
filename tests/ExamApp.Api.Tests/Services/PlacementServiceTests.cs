using ExamApp.Api.Data;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Placement;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1114: seviye tespiti — test yalnız sınıfın tek ders satılan derslerinden ve dengeli üretilir, öğrenci sınıfı başına bir kez
/// çözer, rapor ders / konu kırılımı ve plan önerisi taşır, veli bağlantılı çocuğun raporunu görür. SQLite.
/// </summary>
public class PlacementServiceTests : IDisposable
{
    private const int StudentUser = 9601;
    private const int ParentUser = 9602;

    private readonly TestDb _db = TestDb.Create();
    private readonly IParentChildAccess _access = Substitute.For<IParentChildAccess>();
    private readonly IParentAccessAuditLog _audit = Substitute.For<IParentAccessAuditLog>();
    private int _gradeId, _studentId, _mathId, _scienceId, _artId;

    public PlacementServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7. Sınıf", Code = "7-sinif" };
        var math = new Subject { Name = "Matematik", Code = "matematik" };
        var science = new Subject { Name = "Fen Bilimleri", Code = "fen-bilimleri" };
        var art = new Subject { Name = "Görsel Sanatlar", Code = "gorsel-sanatlar" };
        ctx.AddRange(grade, math, science, art);
        ctx.SaveChanges();
        ctx.GradeSubjects.AddRange(
            new GradeSubject { GradeId = grade.Id, SubjectId = math.Id },
            new GradeSubject { GradeId = grade.Id, SubjectId = science.Id },
            new GradeSubject { GradeId = grade.Id, SubjectId = art.Id });
        var student = new Student { UserId = StudentUser, StudentNumber = "P1", GradeId = grade.Id };
        ctx.Students.Add(student);
        ctx.SaveChanges();
        (_gradeId, _studentId, _mathId, _scienceId, _artId) = (grade.Id, student.Id, math.Id, science.Id, art.Id);
    }

    public void Dispose() => _db.Dispose();

    private PlacementService Service(AppDbContext ctx) => new(ctx, _access, _audit, TimeProvider.System);

    /// <summary>Derste <paramref name="topics"/> konu, her konuda <paramref name="perTopic"/> çoktan seçmeli soru (sınıfın testinde).</summary>
    private void Seed(int subjectId, int topics, int perTopic)
    {
        using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = $"W{subjectId}", Description = "", GradeId = _gradeId };
        ctx.Worksheets.Add(ws);
        for (var t = 0; t < topics; t++)
        {
            var topic = new Topic { Name = $"K{subjectId}-{t}", SubjectId = subjectId, GradeId = _gradeId };
            ctx.Topics.Add(topic);
            ctx.SaveChanges();
            for (var i = 0; i < perTopic; i++)
            {
                var q = new Question { Text = "?", SubjectId = subjectId, TopicId = topic.Id, Point = 1, DifficultyLevel = 1 };
                ctx.Questions.Add(q);
                ctx.SaveChanges();
                var right = new Answer { QuestionId = q.Id, Text = "doğru", Tag = "A" };
                var wrong = new Answer { QuestionId = q.Id, Text = "yanlış", Tag = "B" };
                ctx.Answers.AddRange(right, wrong);
                ctx.SaveChanges();
                q.CorrectAnswerId = right.Id;
                ctx.TestQuestions.Add(new WorksheetQuestion { TestId = ws.Id, QuestionId = q.Id, Order = i + 1 });
                ctx.SaveChanges();
            }
        }
    }

    private async Task GenerateAndPublishAsync()
    {
        await using var ctx = _db.NewContext();
        (await Service(ctx).GenerateAsync(_gradeId)).Error.ShouldBe(PlacementError.None);
        (await Service(ctx).SetPublishedAsync(_gradeId, true)).Error.ShouldBe(PlacementError.None);
    }

    /// <summary>Matematik sorularında <paramref name="mathCorrect"/>, fen sorularında <paramref name="scienceCorrect"/> ise doğru şık.</summary>
    private async Task<PlacementResult<PlacementReportDto>> SubmitAsync(bool mathCorrect, bool scienceCorrect)
    {
        await using var ctx = _db.NewContext();
        var questions = (await Service(ctx).QuestionsAsync(StudentUser)).Value!;
        var facts = await ctx.Questions.Where(q => questions.Select(x => x.Id).Contains(q.Id))
            .Select(q => new { q.Id, q.SubjectId, q.CorrectAnswerId, Wrong = q.Answers.First(a => a.Id != q.CorrectAnswerId).Id })
            .ToListAsync();
        var answers = facts.Select(f => new PlacementAnswerDto
        {
            QuestionId = f.Id,
            AnswerId = (f.SubjectId == _mathId ? mathCorrect : scienceCorrect) ? f.CorrectAnswerId : f.Wrong,
        }).ToList();
        return await Service(ctx).SubmitAsync(StudentUser, new PlacementSubmitDto { Answers = answers });
    }

    [Fact]
    public async Task The_test_is_balanced_across_sellable_subjects_and_spread_over_topics()
    {
        Seed(_mathId, topics: 4, perTopic: 10);
        Seed(_scienceId, topics: 3, perTopic: 10);
        Seed(_artId, topics: 2, perTopic: 10);

        await using var ctx = _db.NewContext();
        var test = (await Service(ctx).GenerateAsync(_gradeId)).Value!;

        test.QuestionCount.ShouldBe(20);
        test.PublishedAtUtc.ShouldBeNull();
        test.Subjects.Select(s => (s.SubjectId, s.Total)).OrderBy(s => s.SubjectId).ShouldBe([(_mathId, 10), (_scienceId, 10)]);
        var topicCounts = await ctx.PlacementTestQuestions.Where(q => q.Question.SubjectId == _mathId)
            .GroupBy(q => q.Question.TopicId).Select(g => g.Count()).ToListAsync();
        topicCounts.Count.ShouldBe(4);
        topicCounts.Max().ShouldBeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task A_thin_bank_is_refused_and_an_unpublished_test_is_invisible()
    {
        Seed(_mathId, topics: 1, perTopic: 4);
        await using (var ctx = _db.NewContext())
            (await Service(ctx).GenerateAsync(_gradeId)).Error.ShouldBe(PlacementError.NotEnoughQuestions);

        Seed(_scienceId, topics: 2, perTopic: 5);
        await using (var ctx = _db.NewContext())
        {
            (await Service(ctx).GenerateAsync(_gradeId)).Error.ShouldBe(PlacementError.None);
            (await Service(ctx).StatusAsync(StudentUser)).Available.ShouldBeFalse();
            (await Service(ctx).QuestionsAsync(StudentUser)).Error.ShouldBe(PlacementError.NotFound);
        }
    }

    [Fact]
    public async Task A_weak_student_gets_Pro_and_can_take_the_test_only_once()
    {
        Seed(_mathId, topics: 2, perTopic: 6);
        Seed(_scienceId, topics: 2, perTopic: 6);
        await GenerateAndPublishAsync();

        var result = await SubmitAsync(mathCorrect: false, scienceCorrect: false);

        result.Error.ShouldBe(PlacementError.None);
        var report = result.Value!;
        (report.Correct, report.Total, report.Percent).ShouldBe((0, 20, 0));
        report.Suggestion.Tier.ShouldBe("pro");
        report.Suggestion.SubjectId.ShouldBeNull();
        report.Suggestion.Band.ShouldBe("Middle");
        report.WeakTopics.Count.ShouldBe(4);
        report.StrongTopics.ShouldBeEmpty();

        await using var ctx = _db.NewContext();
        var status = await Service(ctx).StatusAsync(StudentUser);
        (status.Available, status.Taken).ShouldBe((true, true));
        (await Service(ctx).QuestionsAsync(StudentUser)).Error.ShouldBe(PlacementError.AlreadyTaken);
        (await Service(ctx).SubmitAsync(StudentUser, new PlacementSubmitDto())).Error.ShouldBe(PlacementError.AlreadyTaken);
        (await Service(ctx).ReportAsync(StudentUser))!.Percent.ShouldBe(0);
        (await ctx.PlacementAttempts.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task One_weak_subject_suggests_that_subject_alone()
    {
        Seed(_mathId, topics: 2, perTopic: 6);
        Seed(_scienceId, topics: 2, perTopic: 6);
        await GenerateAndPublishAsync();

        var report = (await SubmitAsync(mathCorrect: true, scienceCorrect: false)).Value!;

        report.Percent.ShouldBe(50);
        report.Suggestion.Tier.ShouldBe("essential");
        (report.Suggestion.SubjectId, report.Suggestion.SubjectName).ShouldBe((_scienceId, "Fen Bilimleri"));
        report.Subjects.Single(s => s.SubjectId == _mathId).Percent.ShouldBe(100);
        report.StrongTopics.ShouldAllBe(t => t.SubjectName == "Matematik");
    }

    [Fact]
    public async Task Answers_from_another_question_are_rejected()
    {
        Seed(_mathId, topics: 2, perTopic: 6);
        await GenerateAndPublishAsync();
        await using var ctx = _db.NewContext();
        var question = (await Service(ctx).QuestionsAsync(StudentUser)).Value![0];
        var foreign = await ctx.Answers.Where(a => a.QuestionId != question.Id).Select(a => a.Id).FirstAsync();

        var result = await Service(ctx).SubmitAsync(StudentUser,
            new PlacementSubmitDto { Answers = [new PlacementAnswerDto { QuestionId = question.Id, AnswerId = foreign }] });

        result.Error.ShouldBe(PlacementError.InvalidAnswers);
        (await ctx.PlacementAttempts.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_parent_sees_the_linked_childs_report_only()
    {
        Seed(_mathId, topics: 2, perTopic: 6);
        await GenerateAndPublishAsync();
        _access.EnsureActiveChildAsync(ParentUser, _studentId, Arg.Any<CancellationToken>())
            .Returns(new ParentChildAccessGrant(1, 77, _studentId, StudentUser, _gradeId, null));

        await using (var ctx = _db.NewContext())
            (await Service(ctx).ReportForParentAsync(ParentUser, _studentId)).ShouldBe((true, null));

        await SubmitAsync(mathCorrect: true, scienceCorrect: true);
        await using (var ctx = _db.NewContext())
        {
            var (found, report) = await Service(ctx).ReportForParentAsync(ParentUser, _studentId);
            found.ShouldBeTrue();
            report!.Suggestion.Tier.ShouldBe("basic");
            (await Service(ctx).ReportForParentAsync(ParentUser, _studentId + 1)).Found.ShouldBeFalse();
        }
        await _audit.Received(2).RecordAsync(77, _studentId, ParentAccessEndpoints.ChildPlacement, Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }
}
