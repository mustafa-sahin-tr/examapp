using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #571: karışık oturum (MCQ + dragDropLabeling) aynı sayıları verir: öğrenci sonuç listesi, öğrenci sonuç ekranı,
/// öğretmen detay ekranı ve veli "test tamamlandı" event'i. Etkinlik (<c>Worksheet.Kind = Activity</c>) hiçbirinde puanlanmaz.
/// Oturum gerçek akışla kurulur: <c>SaveAnswer</c> (puanlama) → <c>EndTest</c> (veli event'i). SQLite.
/// </summary>
public class TestSessionServiceInteractionScoringParityTests : IDisposable
{
    private const int StudentUser = 7301;
    private const int ParentUser = 7302;
    private const int TeacherUser = 7303;

    private static readonly string DragPlan =
        """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"zone-a"},{"id":"zone-b"}],"draggables":[{"id":"card-1","label":"Bir"},{"id":"card-2","label":"İki"}]}""";

    private static readonly string DragSolution =
        """{"placements":[{"draggableId":"card-1","dropZoneId":"zone-a"},{"draggableId":"card-2","dropZoneId":"zone-b"}]}""";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public void Dispose() => _db.Dispose();

    private sealed record World(int StudentId, int GradeId, int SchoolId, int WorksheetId, int ActivityId, int ParentId);

    /// <summary>Okullu öğrenci, Active velisi, atanmış test + atanmış etkinlik; her ikisinde 5 soru (MCQ doğru/yanlış/boş + drag tam/kısmi).</summary>
    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var grade = new Grade { Name = "7" };
        ctx.AddRange(school, grade);
        await ctx.SaveChangesAsync();

        var student = new Student
        {
            UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id,
        };
        var parent = new Parent { UserId = ParentUser };
        var test = new Worksheet { Name = "Karışık test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 900 };
        var activity = new Worksheet { Name = "Karışık etkinlik", Description = "", GradeId = grade.Id, MaxDurationSeconds = 900, Kind = WorksheetKind.Activity };
        ctx.AddRange(student, parent, test, activity);
        await ctx.SaveChangesAsync();

        ctx.ParentStudentLinks.Add(new ParentStudentLink
        {
            Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = parent.Id, StudentId = student.Id,
            Status = ParentStudentLinkStatus.Active, CreatedAt = DateTime.UtcNow.AddDays(-10), ActivatedAt = DateTime.UtcNow.AddDays(-10),
        });
        ctx.WorksheetAssignments.AddRange(
            new WorksheetAssignment { WorksheetId = test.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-5) },
            new WorksheetAssignment { WorksheetId = activity.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-5) });
        await ctx.SaveChangesAsync();

        // Öğretmen: test ve etkinliğin sahibi (detay ekranı "owner" dalı).
        await ctx.Worksheets.Where(w => w.Id == test.Id || w.Id == activity.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.CreateUserId, (int?)TeacherUser));

        return new World(student.Id, grade.Id, school.Id, test.Id, activity.Id, parent.Id);
    }

    /// <summary>5 soruluk (MCQ x2, dragDrop x2, boş x1) oturum: instance + satır id'leri (sıralı) + MCQ doğru/yanlış şık id'leri.</summary>
    private async Task<(int InstanceId, List<int> Rows, int CorrectAnswerId, int WrongAnswerId)> SeedInstanceAsync(World w, int worksheetId, WorksheetInstanceStatus status)
    {
        await using var ctx = _db.NewContext();
        var mcq = new Question { Text = "2+2", Point = 10, TopicId = null };
        var mcq2 = new Question { Text = "3+3", Point = 10 };
        var drag = new Question { Text = "Eşleştir", Point = 10, InteractionType = "dragDropLabeling", InteractionPlan = DragPlan, InteractionSolution = DragSolution };
        var drag2 = new Question { Text = "Eşleştir 2", Point = 10, InteractionType = "dragDropLabeling", InteractionPlan = DragPlan, InteractionSolution = DragSolution };
        var blank = new Question { Text = "Boş", Point = 10 };
        ctx.Questions.AddRange(mcq, mcq2, drag, drag2, blank);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = mcq.Id, Text = "4", Tag = "A" };
        // review #571 (6): save-answer seçilen şıkkın o sorunun şıkkı olmasını ister — yanlış şık, cevaplandığı 2. sorunun (mcq2).
        var wrong = new Answer { QuestionId = mcq2.Id, Text = "5", Tag = "B" };
        var right2 = new Answer { QuestionId = mcq2.Id, Text = "6", Tag = "A" };
        var blankAnswer = new Answer { QuestionId = blank.Id, Text = "7", Tag = "A" };
        ctx.Answers.AddRange(right, wrong, right2, blankAnswer);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = right.Id;
        mcq2.CorrectAnswerId = right2.Id;
        blank.CorrectAnswerId = blankAnswer.Id;
        await ctx.SaveChangesAsync();

        var questions = new[] { mcq, mcq2, drag, drag2, blank };
        var wqs = questions.Select((q, i) => new WorksheetQuestion { TestId = worksheetId, QuestionId = q.Id, Order = i + 1 }).ToList();
        ctx.TestQuestions.AddRange(wqs);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = w.StudentId, WorksheetId = worksheetId, Status = status,
            StartTime = DateTime.UtcNow.AddMinutes(-40),
            EndTime = WorksheetInstanceStatusRules.IsFinished(status) ? DateTime.UtcNow.AddMinutes(-1) : null,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var rows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id }).ToList();
        ctx.TestInstanceQuestions.AddRange(rows);
        await ctx.SaveChangesAsync();
        return (instance.Id, rows.Select(r => r.Id).ToList(), right.Id, wrong.Id);
    }

    /// <summary>Gerçek akış: MCQ doğru, MCQ yanlış, drag tam doğru, drag kısmi, 5. soru boş. Beklenen: doğru 2, yanlış 2, boş 1.</summary>
    private async Task AnswerMixedAsync(int instanceId, List<int> rows, int correctAnswerId, int wrongAnswerId)
    {
        var fullPayload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""";
        var partialPayload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"}]}""";
        var user = new UserProfileDto { Id = StudentUser, KeycloakId = "kc-7301" };

        await SaveAsync(instanceId, rows[0], new SaveAnswerDto { SelectedAnswerId = correctAnswerId }, user);
        await SaveAsync(instanceId, rows[1], new SaveAnswerDto { SelectedAnswerId = wrongAnswerId }, user);
        await SaveAsync(instanceId, rows[2], new SaveAnswerDto { AnswerPayload = fullPayload }, user);
        await SaveAsync(instanceId, rows[3], new SaveAnswerDto { AnswerPayload = partialPayload }, user);
    }

    private async Task SaveAsync(int instanceId, int rowId, SaveAnswerDto dto, UserProfileDto user)
    {
        dto.TestInstanceId = instanceId;
        dto.TestQuestionId = rowId;
        dto.TimeTaken = 10;
        await using var ctx = _db.NewContext();
        (await new TestSessionService(ctx).SaveAnswer(dto, user)).Success.ShouldBeTrue();
    }

    private async Task<List<T>> EventsAsync<T>() where T : class
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<T>();
        return (await ctx.OutboxMessages.AsNoTracking().Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<T>(o.Content)!)
            .ToList();
    }

    // ---------------------------------------------------------------- karışık test: dört ekran aynı sayıları verir

    [Fact]
    public async Task MixedSession_StudentListDetailTeacherDetailAndParentEvent_AllCountTheDragDropAnswers()
    {
        var w = await SeedWorldAsync();
        var (instanceId, rows, correct, wrong) = await SeedInstanceAsync(w, w.WorksheetId, WorksheetInstanceStatus.Started);
        await AnswerMixedAsync(instanceId, rows, correct, wrong);

        await using (var ctx = _db.NewContext())
        {
            (await new TestSessionService(ctx, null, _authApi).EndTest(instanceId, StudentUser)).Success.ShouldBeTrue();
        }

        // 1) Öğrenci sonuç listesi
        Paged<InstanceSummaryDto> list;
        await using (var ctx = _db.NewContext())
        {
            list = await new TestSessionService(ctx).GetCompletedTestsAsync(new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId }, 1, 10);
        }
        var listItem = list.Items.ShouldHaveSingleItem();
        listItem.CorrectAnswers.ShouldBe(2);
        listItem.WrongAnswers.ShouldBe(2);
        listItem.TotalQuestions.ShouldBe(5);
        listItem.Score.ShouldBe(40);

        // 2) Öğrenci sonuç ekranı
        WorksheetCompletedResultDto studentResult;
        await using (var ctx = _db.NewContext())
        {
            var detail = await new WorksheetDetailService(ctx, _authApi).GetWorksheetDetailAsync(w.WorksheetId, "Student", w.StudentId, StudentUser);
            studentResult = detail.ShouldNotBeNull().CompletedResult.ShouldNotBeNull();
        }
        studentResult.CorrectCount.ShouldBe(2);
        studentResult.WrongCount.ShouldBe(2);
        studentResult.EmptyCount.ShouldBe(1);
        studentResult.ScorePercent.ShouldBe(40);

        // 3) Öğretmen detay ekranı (sınıf/istatistik ortalaması: tek tamamlanmış oturum)
        WorksheetDetailDto teacherView;
        await using (var ctx = _db.NewContext())
        {
            teacherView = (await new WorksheetDetailService(ctx, _authApi).GetWorksheetDetailAsync(w.WorksheetId, "Teacher", null, TeacherUser))
                .ShouldNotBeNull();
        }
        teacherView.Stats.AverageScorePercent.ShouldBe(40);
        teacherView.Stats.SolverCount.ShouldBe(1);

        // 4) Veli "test tamamlandı" event'i
        var parentEvent = (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldHaveSingleItem();
        parentEvent.ParentUserId.ShouldBe(ParentUser);
        parentEvent.CorrectAnswers.ShouldBe(2);
        parentEvent.TotalQuestions.ShouldBe(5);
        parentEvent.Score.ShouldBe(40);
    }

    [Fact]
    public async Task MixedSession_TeacherDetailAverage_CountsDragDropAsCorrectWhenFullyAnswered()
    {
        // Yalnız dragDrop cevapları (tam doğru): öğretmen ortalaması MCQ'ya bağlı değil; etkileşimli satır da sayılır.
        var w = await SeedWorldAsync();
        var (instanceId, rows, _, _) = await SeedInstanceAsync(w, w.WorksheetId, WorksheetInstanceStatus.Started);
        var user = new UserProfileDto { Id = StudentUser, KeycloakId = "kc-7301" };
        await SaveAsync(instanceId, rows[2], new SaveAnswerDto { AnswerPayload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""" }, user);
        await SaveAsync(instanceId, rows[3], new SaveAnswerDto { AnswerPayload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"}]}""" }, user);
        await using (var ctx = _db.NewContext())
        {
            (await new TestSessionService(ctx).EndTest(instanceId, StudentUser)).Success.ShouldBeTrue();
        }

        WorksheetDetailDto teacherView;
        await using (var ctx = _db.NewContext())
        {
            teacherView = (await new WorksheetDetailService(ctx, _authApi).GetWorksheetDetailAsync(w.WorksheetId, "Teacher", null, TeacherUser))
                .ShouldNotBeNull();
        }

        // Toplam 5 satır, 2 cevaplı (drag tam + drag kısmi): ortalama = 1 doğru (drag tam) / 5 satır = 20.
        // Not: ortalama DetailService'te cevaplanmamış satırlar dahil toplam satır sayısıyla hesaplanır.
        teacherView.Stats.AverageScorePercent.ShouldBe(20);
    }

    // ---------------------------------------------------------------- etkinlik puansız

    [Fact]
    public async Task Activity_IsExcludedFromStudentListStudentResultTeacherStatsAndParentEvent()
    {
        var w = await SeedWorldAsync();
        var (instanceId, rows, correct, wrong) = await SeedInstanceAsync(w, w.ActivityId, WorksheetInstanceStatus.Started);
        await AnswerMixedAsync(instanceId, rows, correct, wrong);

        await using (var ctx = _db.NewContext())
        {
            (await new TestSessionService(ctx, null, _authApi).EndTest(instanceId, StudentUser)).Success.ShouldBeTrue();
        }

        await using (var ctx = _db.NewContext())
        {
            var list = await new TestSessionService(ctx).GetCompletedTestsAsync(new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId }, 1, 10);
            list.TotalCount.ShouldBe(0);
            list.Items.ShouldBeEmpty();
        }

        await using (var ctx = _db.NewContext())
        {
            var detail = await new WorksheetDetailService(ctx, _authApi).GetWorksheetDetailAsync(w.ActivityId, "Student", w.StudentId, StudentUser);
            detail.ShouldNotBeNull();
            detail.Stats.AverageScorePercent.ShouldBeNull();
            detail.CompletedResult?.Rank.ShouldBeNull();
        }

        await using (var ctx = _db.NewContext())
        {
            var teacher = await new WorksheetDetailService(ctx, _authApi).GetWorksheetDetailAsync(w.ActivityId, "Teacher", null, TeacherUser);
            teacher.ShouldNotBeNull();
            teacher.Stats.AverageScorePercent.ShouldBeNull();
        }

        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Activity_EndTestCompletesTheInstanceWithoutAnyParentEvent()
    {
        var w = await SeedWorldAsync();
        var (instanceId, rows, correct, wrong) = await SeedInstanceAsync(w, w.ActivityId, WorksheetInstanceStatus.Started);
        await AnswerMixedAsync(instanceId, rows, correct, wrong);

        await using (var ctx = _db.NewContext())
        {
            (await new TestSessionService(ctx, null, _authApi).EndTest(instanceId, StudentUser)).Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        (await check.TestInstances.AsNoTracking().SingleAsync(x => x.Id == instanceId)).Status.ShouldBe(WorksheetInstanceStatus.Completed);
        (await EventsAsync<ParentChildTestCompletedEvent>()).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- review #571: istatistik + öğretmen aktivitesi

    /// <summary>Bir öğrenci için (worksheet, oturum) + satırlar: MCQ (doğru/yanlış şık) ya da etkileşimli (saklı IsCorrect).</summary>
    private async Task<int> InstanceWithRowsAsync(
        int studentId, int worksheetId, WorksheetInstanceStatus status, DateTime updateTime,
        params (bool Mcq, bool Correct, int Time)[] rows)
    {
        await using var ctx = _db.NewContext();
        var instance = new WorksheetInstance
        {
            StudentId = studentId, WorksheetId = worksheetId, Status = status,
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = WorksheetInstanceStatusRules.IsFinished(status) ? DateTime.UtcNow.AddMinutes(-1) : null,
            MaxDurationSeconds = 600,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var order = await ctx.TestQuestions.CountAsync(q => q.TestId == worksheetId);
        foreach (var r in rows)
        {
            var question = new Question { Text = "q", Point = 1, InteractionType = r.Mcq ? null : "dragDropLabeling" };
            ctx.Questions.Add(question);
            await ctx.SaveChangesAsync();
            var right = new Answer { QuestionId = question.Id, Text = "A", Tag = "A" };
            var wrong = new Answer { QuestionId = question.Id, Text = "B", Tag = "B" };
            ctx.Answers.AddRange(right, wrong);
            await ctx.SaveChangesAsync();
            question.CorrectAnswerId = right.Id;
            await ctx.SaveChangesAsync();

            var wq = new WorksheetQuestion { TestId = worksheetId, QuestionId = question.Id, Order = ++order };
            ctx.TestQuestions.Add(wq);
            await ctx.SaveChangesAsync();

            ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetInstanceId = instance.Id,
                WorksheetQuestionId = wq.Id,
                SelectedAnswerId = r.Mcq ? (r.Correct ? right.Id : wrong.Id) : null,
                AnswerPayload = r.Mcq ? null : """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z","draggableId":"c"}]}""",
                IsCorrect = r.Correct,
                TimeTaken = r.Time,
                UpdateTime = updateTime,
            });
            await ctx.SaveChangesAsync();
        }
        return instance.Id;
    }

    [Fact]
    public async Task GroupedStudentStatistics_CountsDragDropByHybridRuleAndExcludesActivities()
    {
        var w = await SeedWorldAsync();
        // Test: MCQ doğru + etkileşim doğru + etkileşim yanlış → doğru 2, yanlış 1 (her satır ayrı sayılır).
        await InstanceWithRowsAsync(w.StudentId, w.WorksheetId, WorksheetInstanceStatus.Completed, DateTime.UtcNow,
            (Mcq: true, Correct: true, Time: 60), (Mcq: false, Correct: true, Time: 60), (Mcq: false, Correct: false, Time: 60));
        // Etkinlik: 3 doğru MCQ — istatistiğe hiç girmemeli.
        await InstanceWithRowsAsync(w.StudentId, w.ActivityId, WorksheetInstanceStatus.Completed, DateTime.UtcNow,
            (Mcq: true, Correct: true, Time: 60), (Mcq: true, Correct: true, Time: 60), (Mcq: true, Correct: true, Time: 60));

        ExamAllStatisticsDto stats;
        await using (var ctx = _db.NewContext())
        {
            stats = await new ExamService(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), Substitute.For<IAuthApiClient>())
                .GetGroupedStudentStatistics(w.StudentId);
        }

        stats.Total.TotalSolvedTests.ShouldBe(1);
        stats.Total.CompletedTests.ShouldBe(1);
        stats.Total.TotalCorrectAnswers.ShouldBe(2);
        stats.Total.TotalWrongAnswers.ShouldBe(1);
        stats.Total.TotalTimeSpentMinutes.ShouldBe(3);
        var group = stats.Grouped.ShouldHaveSingleItem();
        group.TotalCorrectAnswers.ShouldBe(2);
        group.TotalWrongAnswers.ShouldBe(1);
    }

    [Fact]
    public async Task TeacherStudentActivity_SolvedAndTimeIncludeActivities_CorrectExcludesThem()
    {
        var w = await SeedWorldAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = TeacherUser, SchoolId = w.SchoolId });
            await ctx.SaveChangesAsync();
        }
        // Test: 3 cevap (2 doğru, 1 yanlış), 10 sn. Etkinlik: 2 cevap (ikisi doğru), 30 sn.
        await InstanceWithRowsAsync(w.StudentId, w.WorksheetId, WorksheetInstanceStatus.Started, DateTime.UtcNow,
            (Mcq: true, Correct: true, Time: 10), (Mcq: true, Correct: true, Time: 10), (Mcq: true, Correct: false, Time: 10));
        await InstanceWithRowsAsync(w.StudentId, w.ActivityId, WorksheetInstanceStatus.Started, DateTime.UtcNow,
            (Mcq: true, Correct: true, Time: 30), (Mcq: true, Correct: true, Time: 30));

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>());
        TeacherStudentsActivitySummaryDto summary;
        await using (var ctx = _db.NewContext())
        {
            summary = await new TeacherService(ctx, _authApi, schoolAccessPolicy: new SchoolAccessPolicy(ctx))
                .GetStudentsActivitySummaryAsync(SchoolScope.For(TeacherUser, w.SchoolId), 7);
        }

        summary.TotalQuestionsSolved.ShouldBe(5);   // 3 test + 2 etkinlik
        summary.TotalCorrectCount.ShouldBe(2);      // yalnız test (etkinlik doğru sayılmaz)
        summary.TotalTimeSeconds.ShouldBe(90);      // 3*10 + 2*30
        var student = summary.TopStudents.ShouldHaveSingleItem();
        student.QuestionsSolved.ShouldBe(5);
        student.CorrectCount.ShouldBe(2);
    }

    [Fact]
    public async Task GroupedStudentStatistics_SoftDeletedQuestionPayloadRow_IsCountedWrongLikeTheSqlPath()
    {
        var w = await SeedWorldAsync();
        var instanceId = await InstanceWithRowsAsync(w.StudentId, w.WorksheetId, WorksheetInstanceStatus.Completed, DateTime.UtcNow,
            (Mcq: true, Correct: true, Time: 60), (Mcq: false, Correct: true, Time: 60), (Mcq: false, Correct: false, Time: 60));
        await using (var ctx = _db.NewContext())
        {
            // Stored IsCorrect=true'lu etkileşim satırının sorusunu soft-delete et.
            var deletedQuestionId = await ctx.TestInstanceQuestions.AsNoTracking()
                .Where(i => i.WorksheetInstanceId == instanceId && i.AnswerPayload != null && i.IsCorrect)
                .Select(i => i.WorksheetQuestion.QuestionId).SingleAsync();
            await ctx.Questions.IgnoreQueryFilters().Where(q => q.Id == deletedQuestionId)
                .ExecuteUpdateAsync(u => u.SetProperty(q => q.IsDeleted, true));
        }

        ExamAllStatisticsDto stats;
        await using (var ctx = _db.NewContext())
        {
            stats = await new ExamService(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), Substitute.For<IAuthApiClient>())
                .GetGroupedStudentStatistics(w.StudentId);
        }

        // Beklenen (SQL yolu ve WorksheetScoring kuralıyla aynı): doğru 1 (MCQ), yanlış 2 (silinmiş soru satırı dahil).
        stats.Total.TotalCorrectAnswers.ShouldBe(1);
        stats.Total.TotalWrongAnswers.ShouldBe(2);
    }
}
