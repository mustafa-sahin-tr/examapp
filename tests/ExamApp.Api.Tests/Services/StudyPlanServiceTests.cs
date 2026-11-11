using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Flashcards;
using ExamApp.Api.Services.MockExams;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.StudyPlans;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1113: haftalık çalışma planı — görev seçimi (ödev, deneme, zayıf konu, yanlışlar, bilgi kartı, dolgu), hafta başına tek plan,
/// işaretleme yalnız kendi bu haftaki görevinde. SQLite; deneme ve bilgi kartı servisleri sahte.
/// </summary>
public class StudyPlanServiceTests : IDisposable
{
    private const int StudentUser = 9501;
    private const int OtherUser = 9502;

    private readonly TestDb _db = TestDb.Create();
    private readonly IMockExamService _mockExams = Substitute.For<IMockExamService>();
    private readonly IFlashcardStudyService _flashcards = Substitute.For<IFlashcardStudyService>();
    private int _studentId, _gradeId, _subjectId, _weakTopicId, _strongTopicId;

    public StudyPlanServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        ctx.SaveChanges();
        var weak = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
        var strong = new Topic { Name = "Açılar", SubjectId = subject.Id, GradeId = grade.Id };
        var student = new Student { UserId = StudentUser, StudentNumber = "S1", GradeId = grade.Id };
        var other = new Student { UserId = OtherUser, StudentNumber = "S2", GradeId = grade.Id };
        ctx.AddRange(weak, strong, student, other);
        ctx.SaveChanges();
        (_studentId, _gradeId, _subjectId, _weakTopicId, _strongTopicId) = (student.Id, grade.Id, subject.Id, weak.Id, strong.Id);

        _mockExams.ListForStudentAsync(Arg.Any<StudentProfileDto>(), Arg.Any<CancellationToken>()).Returns(new List<StudentMockExamDto>());
        _flashcards.ListDecksAsync(Arg.Any<FlashcardLearner>(), Arg.Any<CancellationToken>()).Returns(new List<FlashcardDeckSummaryDto>());
    }

    public void Dispose() => _db.Dispose();

    private StudyPlanService Service(AppDbContext ctx) =>
        new(ctx, new TopicPerformanceService(ctx), _mockExams, _flashcards, Substitute.For<IParentChildAccess>(),
            Substitute.For<IParentAccessAuditLog>(), TimeProvider.System);

    private async Task<StudyPlanDto> CurrentAsync(int userId = StudentUser)
    {
        await using var ctx = _db.NewContext();
        return (await Service(ctx).GetCurrentForStudentAsync(userId))!;
    }

    /// <summary>Konuda <paramref name="correct"/> / 5 doğru pratik cevabı (dün).</summary>
    private async Task AnswerAsync(int topicId, int correct)
    {
        await using var ctx = _db.NewContext();
        var at = DateTime.UtcNow.AddDays(-1);
        var session = new PracticeSession { StudentId = _studentId, GradeId = _gradeId, StartTime = at, Status = PracticeSessionStatus.Ended };
        for (var i = 0; i < 5; i++)
        {
            var q = new Question { Text = "?", SubjectId = _subjectId, TopicId = topicId, Point = 1, DifficultyLevel = 1 };
            ctx.Questions.Add(q);
            await ctx.SaveChangesAsync();
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = q.Id, ShownAt = at, AnsweredAt = at, IsCorrect = i < correct, TimeTaken = 5 });
        }
        ctx.PracticeSessions.Add(session);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task A_new_student_gets_a_practice_task_and_the_plan_is_built_once_a_week()
    {
        var plan = await CurrentAsync();
        plan.Tasks.Select(t => t.Kind).ShouldBe(["practice"]);
        plan.WeekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        await AnswerAsync(_weakTopicId, 1);
        (await CurrentAsync()).Tasks.Select(t => t.Kind).ShouldBe(["practice"]);
        await using var ctx = _db.NewContext();
        (await ctx.WeeklyStudyPlans.CountAsync()).ShouldBe(1);
        (await Service(ctx).EnsureAsync(_studentId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Tasks_come_from_assignments_mock_exams_weak_topics_mistakes_and_flashcards()
    {
        await AnswerAsync(_weakTopicId, 1);
        await AnswerAsync(_strongTopicId, 5);
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(42);
            var worksheet = new Worksheet { Name = "Kesir ödevi", Description = "", GradeId = _gradeId, SubjectId = _subjectId };
            ctx.Worksheets.Add(worksheet);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheet.Id, StudentId = _studentId, StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(3)
            });
            await ctx.SaveChangesAsync();
        }
        _mockExams.ListForStudentAsync(Arg.Any<StudentProfileDto>(), Arg.Any<CancellationToken>()).Returns(new List<StudentMockExamDto>
        {
            new() { Id = 7, Name = "Çözülmüş", Sessions = [new StudentMockExamSessionDto()] },
            new() { Id = 8, Name = "LGS Deneme 1" },
        });
        _flashcards.ListDecksAsync(Arg.Any<FlashcardLearner>(), Arg.Any<CancellationToken>())
            .Returns(new List<FlashcardDeckSummaryDto> { new() { Id = 1, Title = "Deste" } });

        var plan = await CurrentAsync();

        plan.Tasks.Select(t => t.Kind).ShouldBe(["assignment", "mockExam", "practiceTopic", "mistakes", "flashcards"]);
        plan.Tasks[0].ShouldSatisfyAllConditions(t => t.Title.ShouldBe("Kesir ödevi"), t => t.SubjectName.ShouldBe("Matematik"), t => t.DueAtUtc.ShouldNotBeNull());
        plan.Tasks[1].ShouldSatisfyAllConditions(t => t.MockExamId.ShouldBe(8), t => t.Title.ShouldBe("LGS Deneme 1"));
        plan.Tasks[2].ShouldSatisfyAllConditions(t => t.TopicId.ShouldBe(_weakTopicId), t => t.Title.ShouldBe("Kesirler"));
        plan.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task Fewer_than_five_tasks_are_filled_with_other_topics_before_free_practice()
    {
        await AnswerAsync(_strongTopicId, 5);

        (await CurrentAsync()).Tasks.Select(t => (t.Kind, t.TopicId)).ShouldBe([("practiceTopic", _strongTopicId), ("practice", null)]);
    }

    [Fact]
    public async Task Only_the_student_can_tick_their_own_task_of_this_week()
    {
        var task = (await CurrentAsync()).Tasks.Single();
        await using var ctx = _db.NewContext();
        var service = Service(ctx);

        (await service.SetTaskDoneAsync(OtherUser, task.Id, true)).ShouldBeNull();
        var done = await service.SetTaskDoneAsync(StudentUser, task.Id, true);
        done!.ShouldSatisfyAllConditions(p => p.DoneCount.ShouldBe(1), p => p.Tasks.Single().Done.ShouldBeTrue());
        (await service.SetTaskDoneAsync(StudentUser, task.Id, false))!.DoneCount.ShouldBe(0);

        await ctx.WeeklyStudyPlans.ExecuteUpdateAsync(s => s.SetProperty(p => p.WeekStart, p => p.WeekStart.AddDays(-7)));
        (await service.SetTaskDoneAsync(StudentUser, task.Id, true)).ShouldBeNull();
    }
}
