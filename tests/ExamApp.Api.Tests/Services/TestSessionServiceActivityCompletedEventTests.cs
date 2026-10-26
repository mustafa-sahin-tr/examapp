using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #572: etkinlik tamamlama → tek <see cref="ActivityCompletedEvent"/> (aynı transaction'da, yalnız Started → Completed
/// yapan çağrıda), kırpılmış <c>ActiveSeconds</c> (bildirilen ya da TimeTaken toplamı; üst sınır 30 dk ve geçen süre),
/// boş maddeler event'i engellemez, Test türünde event yok, etkinlik cevapları <see cref="AnswerSubmittedEvent"/> üretmez.
/// </summary>
public class TestSessionServiceActivityCompletedEventTests : IDisposable
{
    private const int StudentUserId = 5721;
    private const string KeycloakId = "kc-5721";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static readonly UserProfileDto Student = new() { Id = StudentUserId, KeycloakId = KeycloakId, Role = "Student" };

    private sealed record Seed(
        int InstanceId, int WorksheetId, int StudentId, int McqRowId, int SecondRowId, int BlankRowId, int CorrectAnswerId, int SecondAnswerId,
        int? AssignmentId, int MathSubjectId);

    private async Task<Seed> SeedAsync(
        WorksheetKind kind, DateTime startTime, bool worksheetHasSubject = true, bool assign = true)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var math = new Subject { Name = "Matematik" };
        var science = new Subject { Name = "Fen" };
        ctx.AddRange(grade, math, science);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S5721", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet
        {
            Name = "Etkinlik", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = kind,
            SubjectId = worksheetHasSubject ? science.Id : null,
        };
        ctx.AddRange(student, worksheet);
        await ctx.SaveChangesAsync();

        // Maddelerde en sık ders: Matematik (2) > Fen (1).
        var q1 = new Question { Text = "2 + 2 = ?", Point = 10, SubjectId = math.Id };
        var q2 = new Question { Text = "3 + 3 = ?", Point = 10, SubjectId = math.Id };
        var q3 = new Question { Text = "Boş", Point = 10, SubjectId = science.Id };
        ctx.Questions.AddRange(q1, q2, q3);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = q1.Id, Text = "4", Tag = "A" };
        var right2 = new Answer { QuestionId = q2.Id, Text = "6", Tag = "A" };
        var right3 = new Answer { QuestionId = q3.Id, Text = "1", Tag = "A" };
        ctx.Answers.AddRange(right, right2, right3);
        await ctx.SaveChangesAsync();
        q1.CorrectAnswerId = right.Id;
        q2.CorrectAnswerId = right2.Id;
        q3.CorrectAnswerId = right3.Id;
        await ctx.SaveChangesAsync();

        var wqs = new[] { q1, q2, q3 }
            .Select((q, i) => new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = i + 1 })
            .ToList();
        ctx.TestQuestions.AddRange(wqs);
        await ctx.SaveChangesAsync();

        int? assignmentId = null;
        if (assign)
        {
            var assignment = new WorksheetAssignment { WorksheetId = worksheet.Id, StudentId = student.Id, StartAt = startTime.AddDays(-1) };
            ctx.WorksheetAssignments.Add(assignment);
            await ctx.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = startTime, MaxDurationSeconds = 0,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var rows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id }).ToList();
        ctx.TestInstanceQuestions.AddRange(rows);
        await ctx.SaveChangesAsync();

        return new Seed(instance.Id, worksheet.Id, student.Id, rows[0].Id, rows[1].Id, rows[2].Id, right.Id, right2.Id, assignmentId, math.Id);
    }

    private static TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private async Task CheckAsync(Seed s)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CheckActivityAsync(s.InstanceId, new ActivityCheckRequestDto
        {
            Answers = [new ActivityCheckAnswerDto { InstanceQuestionId = s.McqRowId, SelectedAnswerId = s.CorrectAnswerId }],
        }, StudentUserId);
        result.Success.ShouldBeTrue(result.Message);
    }

    private async Task SaveAsync(Seed s, int rowId, int timeTaken)
    {
        var answerId = rowId == s.SecondRowId ? s.SecondAnswerId : s.CorrectAnswerId;
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).SaveAnswer(new SaveAnswerDto
        {
            TestInstanceId = s.InstanceId, TestQuestionId = rowId, SelectedAnswerId = answerId, TimeTaken = timeTaken,
        }, Student);
        result.Success.ShouldBeTrue(result.Message);
    }

    private async Task<TestSessionResultDto> EndAsync(Seed s, int? activeSeconds)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).EndTest(
            s.InstanceId, StudentUserId, new EndTestRequestDto { ActiveSeconds = activeSeconds }, KeycloakId);
    }

    private async Task<List<ActivityCompletedEvent>> ActivityEventsAsync()
    {
        await using var ctx = _db.NewContext();
        var name = OutboxEventRegistry.NameFor<ActivityCompletedEvent>();
        var rows = await ctx.OutboxMessages.AsNoTracking().Where(o => o.Type == name).ToListAsync();
        return rows.Select(o =>
        {
            var evt = JsonSerializer.Deserialize<ActivityCompletedEvent>(o.Content)!;
            evt.EventId.ShouldBe(o.Id);
            return evt;
        }).ToList();
    }

    private async Task<int> OutboxCountAsync<T>()
    {
        await using var ctx = _db.NewContext();
        var name = OutboxEventRegistry.NameFor<T>();
        return await ctx.OutboxMessages.AsNoTracking().CountAsync(o => o.Type == name);
    }

    private async Task<WorksheetInstance> InstanceAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstances.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    [Fact]
    public async Task EndTest_Activity_WritesOneEventWithFieldsAndPersistsActiveSeconds()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-20));
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: 600)).Success.ShouldBeTrue();

        var evt = (await ActivityEventsAsync()).ShouldHaveSingleItem();
        var instance = await InstanceAsync(s.InstanceId);
        evt.UserId.ShouldBe(StudentUserId);
        evt.StudentId.ShouldBe(s.StudentId);
        evt.ClientId.ShouldBe(KeycloakId);
        evt.ActivityId.ShouldBe(s.WorksheetId);
        evt.WorksheetInstanceId.ShouldBe(s.InstanceId);
        evt.AssignmentId.ShouldBe(s.AssignmentId);
        evt.ItemCount.ShouldBe(3);
        evt.ActiveSeconds.ShouldBe(600);
        evt.CompletedAtUtc.ShouldBe(instance.EndTime!.Value, TimeSpan.FromMilliseconds(1));
        instance.ActiveSeconds.ShouldBe(600);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
    }

    [Fact]
    public async Task EndTest_Activity_ClampsReportedSecondsToMaxPerActivity()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddHours(-3));
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: 99_999)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBe(ActivityOptions.MaxActiveSecondsPerActivity);
        (await InstanceAsync(s.InstanceId)).ActiveSeconds.ShouldBe(ActivityOptions.MaxActiveSecondsPerActivity);
    }

    [Fact]
    public async Task EndTest_Activity_ClampsReportedSecondsToElapsedSinceStart()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddSeconds(-90));
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: 1200)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBeInRange(90, 95);
    }

    [Fact]
    public async Task EndTest_Activity_NegativeReportedSecondsClampToZero()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: -50)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBe(0);
    }

    [Fact]
    public async Task EndTest_Activity_WithoutReportedSeconds_FallsBackToTimeTakenSum()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await SaveAsync(s, s.McqRowId, timeTaken: 40);
        await SaveAsync(s, s.SecondRowId, timeTaken: 25);
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: null)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBe(65);
    }

    [Fact]
    public async Task EndTest_Activity_LegacyOverloadWithoutBody_StillEmitsEvent()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10), assign: false);
        await CheckAsync(s);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();

        var evt = (await ActivityEventsAsync()).ShouldHaveSingleItem();
        evt.AssignmentId.ShouldBeNull();
        evt.ClientId.ShouldBeNull();
    }

    [Fact]
    public async Task EndTest_Activity_WithBlankItems_StillEmitsEvent()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await CheckAsync(s); // yalnız 1 / 3 madde cevaplı

        (await EndAsync(s, activeSeconds: 120)).Success.ShouldBeTrue();

        var evt = (await ActivityEventsAsync()).ShouldHaveSingleItem();
        evt.ItemCount.ShouldBe(3);
        evt.AnsweredItemCount.ShouldBe(1); // M2: yalnız cevaplı madde
    }

    [Fact]
    public async Task EndTest_Activity_AnsweredItemCount_CountsSavedAndCheckedItems()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await SaveAsync(s, s.SecondRowId, timeTaken: 10);
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: 120)).Success.ShouldBeTrue();

        var evt = (await ActivityEventsAsync()).ShouldHaveSingleItem();
        evt.ItemCount.ShouldBe(3);
        evt.AnsweredItemCount.ShouldBe(2);
    }

    [Fact]
    public async Task EndTest_Activity_AllItemsBlank_StillEmitsEventWithZeroAnswered()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        // Kontrol yapılmış ama hiçbir madde cevaplı değil (EndTest kapısı CheckCount ≥ 1).
        await using (var ctx = _db.NewContext())
            await ctx.TestInstances.Where(x => x.Id == s.InstanceId).ExecuteUpdateAsync(u => u.SetProperty(x => x.CheckCount, 1));

        (await EndAsync(s, activeSeconds: 60)).Success.ShouldBeTrue();

        var evt = (await ActivityEventsAsync()).ShouldHaveSingleItem();
        evt.ItemCount.ShouldBe(3);
        evt.AnsweredItemCount.ShouldBe(0);
    }

    [Fact]
    public async Task EndTest_Activity_SecondCall_IsIdempotentAndWritesNoSecondEvent()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await CheckAsync(s);
        (await EndAsync(s, activeSeconds: 120)).Success.ShouldBeTrue();
        var firstEnd = (await InstanceAsync(s.InstanceId)).EndTime;

        (await EndAsync(s, activeSeconds: 500)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBe(120);
        var instance = await InstanceAsync(s.InstanceId);
        instance.ActiveSeconds.ShouldBe(120);
        instance.EndTime.ShouldBe(firstEnd);
    }

    [Fact]
    public async Task EndTest_Activity_UsesWorksheetSubject_ElseMostCommonQuestionSubject()
    {
        var withSubject = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await CheckAsync(withSubject);
        (await EndAsync(withSubject, 60)).Success.ShouldBeTrue();
        var worksheetSubject = (await ActivityEventsAsync()).ShouldHaveSingleItem().SubjectId;
        worksheetSubject.ShouldNotBeNull();
        worksheetSubject.ShouldNotBe(withSubject.MathSubjectId); // worksheet dersi (Fen) önce gelir
    }

    [Fact]
    public async Task EndTest_ActivityWithoutWorksheetSubject_UsesMostCommonQuestionSubject()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10), worksheetHasSubject: false);
        await CheckAsync(s);

        (await EndAsync(s, 60)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().SubjectId.ShouldBe(s.MathSubjectId);
    }

    [Fact]
    public async Task EndTest_TestKind_WritesNoActivityEventAndNoActiveSeconds()
    {
        var s = await SeedAsync(WorksheetKind.Test, DateTime.UtcNow.AddMinutes(-10));
        await SaveAsync(s, s.McqRowId, timeTaken: 30);

        (await EndAsync(s, activeSeconds: 300)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldBeEmpty();
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        instance.ActiveSeconds.ShouldBeNull();
        (await OutboxCountAsync<AnswerSubmittedEvent>()).ShouldBe(1); // test akışı değişmedi
    }

    [Fact]
    public async Task EndTest_ActivityWithoutCheck_WritesNoEvent()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));

        var result = await EndAsync(s, activeSeconds: 300);

        result.Success.ShouldBeFalse();
        (await ActivityEventsAsync()).ShouldBeEmpty();
        (await InstanceAsync(s.InstanceId)).ActiveSeconds.ShouldBeNull();
    }

    [Fact]
    public async Task Activity_SaveCheckAndEnd_WriteNoAnswerSubmittedEvent()
    {
        var s = await SeedAsync(WorksheetKind.Activity, DateTime.UtcNow.AddMinutes(-10));
        await SaveAsync(s, s.McqRowId, timeTaken: 30);
        await CheckAsync(s);
        (await EndAsync(s, activeSeconds: 120)).Success.ShouldBeTrue();

        (await OutboxCountAsync<AnswerSubmittedEvent>()).ShouldBe(0);
        (await ActivityEventsAsync()).Count.ShouldBe(1);
    }

    /// <summary>
    /// review M1: aynı öğrenciye başka bir worksheet + oturum ekler (tek madde). <paramref name="endTime"/> verilirse oturum
    /// Completed (o anda bitmiş), verilmezse Started + CheckCount = 1 (bitirilebilir).
    /// </summary>
    private async Task<int> AddOtherInstanceAsync(Seed s, WorksheetKind kind, DateTime startTime, DateTime? endTime)
    {
        await using var ctx = _db.NewContext();
        var gradeId = await ctx.Worksheets.Where(x => x.Id == s.WorksheetId).Select(x => x.GradeId).SingleAsync();
        var worksheet = new Worksheet { Name = "Diğer", Description = "", GradeId = gradeId, MaxDurationSeconds = 0, Kind = kind };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        var questionId = await ctx.Questions.Select(q => q.Id).FirstAsync();
        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();
        var instance = new WorksheetInstance
        {
            StudentId = s.StudentId, WorksheetId = worksheet.Id, StartTime = startTime, MaxDurationSeconds = 0,
            Status = endTime is null ? WorksheetInstanceStatus.Started : WorksheetInstanceStatus.Completed,
            EndTime = endTime, CheckCount = 1,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id });
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    private async Task<TestSessionResultDto> EndInstanceAsync(int instanceId, int activeSeconds)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).EndTest(instanceId, StudentUserId, new EndTestRequestDto { ActiveSeconds = activeSeconds }, KeycloakId);
    }

    [Fact]
    public async Task EndTest_Activity_OverlappingCompletedActivity_CreditsOnlyNonOverlappingSeconds()
    {
        // İki etkinlik 30 dk önce birlikte açıldı; diğeri 10 dk önce bitti → bu etkinlik yalnız son ~10 dk'yı alabilir.
        var start = DateTime.UtcNow.AddMinutes(-30);
        var s = await SeedAsync(WorksheetKind.Activity, start);
        await AddOtherInstanceAsync(s, WorksheetKind.Activity, start, endTime: DateTime.UtcNow.AddMinutes(-10));
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: 1800)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBeInRange(600, 605);
        (await InstanceAsync(s.InstanceId)).ActiveSeconds!.Value.ShouldBeInRange(600, 605);
    }

    [Fact]
    public async Task EndTest_TwoParallelActivitiesFinishedSequentially_SecondGetsOnlyTheRemainder()
    {
        var start = DateTime.UtcNow.AddMinutes(-30);
        var s = await SeedAsync(WorksheetKind.Activity, start);
        var other = await AddOtherInstanceAsync(s, WorksheetKind.Activity, start, endTime: null);
        await CheckAsync(s);

        (await EndInstanceAsync(other, 1800)).Success.ShouldBeTrue();
        (await EndAsync(s, activeSeconds: 1800)).Success.ShouldBeTrue();

        var events = await ActivityEventsAsync();
        events.Single(e => e.WorksheetInstanceId == other).ActiveSeconds.ShouldBeInRange(1795, 1800);
        events.Single(e => e.WorksheetInstanceId == s.InstanceId).ActiveSeconds.ShouldBeInRange(0, 5);
        events.Sum(e => e.ActiveSeconds).ShouldBeLessThanOrEqualTo(1805);
    }

    [Fact]
    public async Task EndTest_Activity_ActivityFinishedBeforeStart_OrCompletedTest_DoesNotShortenWindow()
    {
        var start = DateTime.UtcNow.AddMinutes(-20);
        var s = await SeedAsync(WorksheetKind.Activity, start);
        // Bu oturum başlamadan bitmiş etkinlik ve oturum sırasında bitmiş TEST pencereyi kısaltmaz.
        await AddOtherInstanceAsync(s, WorksheetKind.Activity, start.AddMinutes(-40), endTime: start.AddMinutes(-5));
        await AddOtherInstanceAsync(s, WorksheetKind.Test, start, endTime: DateTime.UtcNow.AddMinutes(-2));
        await CheckAsync(s);

        (await EndAsync(s, activeSeconds: 900)).Success.ShouldBeTrue();

        (await ActivityEventsAsync()).ShouldHaveSingleItem().ActiveSeconds.ShouldBe(900);
    }

    [Theory]
    [InlineData(500, 3600, 500)]
    [InlineData(5000, 3600, 1800)]
    [InlineData(500, 100, 100)]
    [InlineData(-1, 3600, 0)]
    [InlineData(500, -10, 0)] // saat kayması: başlangıç gelecekte → 0
    public void ClampActiveSeconds_AppliesFloorCapAndElapsed(long reported, int elapsedSeconds, int expected)
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        TestSessionService.ClampActiveSeconds(reported, now.AddSeconds(-elapsedSeconds), now).ShouldBe(expected);
    }
}
