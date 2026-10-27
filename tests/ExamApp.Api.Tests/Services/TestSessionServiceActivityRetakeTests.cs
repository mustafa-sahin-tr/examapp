using System.Data.Common;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #609: tamamlanmış etkinliği "Yeniden çöz" (<see cref="TestSessionService.RetakeActivityAsync"/>). Aynı oturum sıfırlanır:
/// cevaplar / kontrol sonuçları silinir, Status = Started; ilk kontrol özeti, <c>FirstCompletedAt</c>, <c>StartTime</c> korunur.
/// Test türü, başka öğrencinin oturumu, hiç tamamlanmamış oturum reddedilir; yarışı kaybeden retake hiçbir şeyi sıfırlamaz.
/// Yeniden tamamlama aynı WorksheetInstanceId ile ikinci bir <see cref="ActivityCompletedEvent"/> yazar (BadgeService (UserId,
/// WorksheetInstanceId) ile tekilleştirir — rozet şişmez, bkz. BadgeService.Tests ActivityBadgeTests). Öğretmen takibi oturumu
/// tamamlandı + ilk sonuçla görmeye devam eder, teslim tarihi geçmiş olsa da.
/// </summary>
public class TestSessionServiceActivityRetakeTests : IDisposable
{
    private const int StudentUserId = 6091;
    private const int OtherUserId = 6092;
    private const int TeacherUserId = 6090;
    private const string KeycloakId = "kc-6091";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static readonly UserProfileDto Student = new() { Id = StudentUserId, KeycloakId = KeycloakId, Role = "Student" };

    private sealed record Seed(
        int InstanceId, int WorksheetId, int StudentId, int SchoolId, int Row1, int Row2, int Right1, int Wrong1, int Right2,
        DateTime StartTime);

    private async Task<Seed> SeedAsync(WorksheetKind kind = WorksheetKind.Activity, DateTime? assignmentEndAt = null)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var school = new School { Name = "Okul" };
        ctx.AddRange(grade, school);
        await ctx.SaveChangesAsync();

        var student = new Student
        {
            UserId = StudentUserId, StudentNumber = "S6091", SchoolName = "s", SchoolId = school.Id,
            SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id,
        };
        var other = new Student
        {
            UserId = OtherUserId, StudentNumber = "S6092", SchoolName = "s", SchoolId = school.Id,
            SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id,
        };
        ctx.AddRange(student, other);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(TeacherUserId);
        var worksheet = new Worksheet { Name = "Etkinlik", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = kind };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();

        var q1 = new Question { Text = "2 + 2 = ?", Point = 10 };
        var q2 = new Question { Text = "3 + 3 = ?", Point = 10 };
        ctx.Questions.AddRange(q1, q2);
        await ctx.SaveChangesAsync();
        var right1 = new Answer { QuestionId = q1.Id, Text = "4", Tag = "A" };
        var wrong1 = new Answer { QuestionId = q1.Id, Text = "5", Tag = "B" };
        var right2 = new Answer { QuestionId = q2.Id, Text = "6", Tag = "A" };
        ctx.Answers.AddRange(right1, wrong1, right2);
        await ctx.SaveChangesAsync();
        q1.CorrectAnswerId = right1.Id;
        q2.CorrectAnswerId = right2.Id;
        await ctx.SaveChangesAsync();

        var wqs = new[] { q1, q2 }
            .Select((q, i) => new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = i + 1 })
            .ToList();
        ctx.TestQuestions.AddRange(wqs);
        await ctx.SaveChangesAsync();

        var startTime = DateTime.UtcNow.AddDays(-2);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheet.Id, StudentId = student.Id, StartAt = startTime.AddDays(-1), EndAt = assignmentEndAt,
        });
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

        return new Seed(instance.Id, worksheet.Id, student.Id, school.Id, rows[0].Id, rows[1].Id, right1.Id, wrong1.Id, right2.Id, startTime);
    }

    private static TestSessionService NewService(AppDbContext ctx) => new(ctx);

    /// <summary>İlk kontrol: madde 1 doğru, madde 2 boş → 1/2 (öğretmenin göreceği ilk sonuç).</summary>
    private async Task CheckAsync(Seed s, int row1Answer, int? row2Answer = null)
    {
        await using var ctx = _db.NewContext();
        var answers = new List<ActivityCheckAnswerDto> { new() { InstanceQuestionId = s.Row1, SelectedAnswerId = row1Answer } };
        if (row2Answer != null)
            answers.Add(new ActivityCheckAnswerDto { InstanceQuestionId = s.Row2, SelectedAnswerId = row2Answer.Value });
        var result = await NewService(ctx).CheckActivityAsync(s.InstanceId, new ActivityCheckRequestDto { Answers = answers }, StudentUserId);
        result.Success.ShouldBeTrue(result.Message);
    }

    private async Task EndAsync(Seed s, int? activeSeconds = null)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).EndTest(
            s.InstanceId, StudentUserId, new EndTestRequestDto { ActiveSeconds = activeSeconds }, KeycloakId);
        result.Success.ShouldBeTrue(result.Message);
    }

    private async Task<ActivityRetakeResultDto> RetakeAsync(Seed s, int userId = StudentUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).RetakeActivityAsync(s.InstanceId, userId);
    }

    private async Task CompleteFirstAttemptAsync(Seed s)
    {
        await CheckAsync(s, s.Right1);
        await EndAsync(s, activeSeconds: 300);
    }

    private async Task<WorksheetInstance> InstanceAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstances.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<List<WorksheetInstanceQuestion>> RowsAsync(int instanceId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstanceQuestions.AsNoTracking().Where(r => r.WorksheetInstanceId == instanceId).OrderBy(r => r.Id).ToListAsync();
    }

    private async Task<List<ActivityCompletedEvent>> ActivityEventsAsync()
    {
        await using var ctx = _db.NewContext();
        var name = OutboxEventRegistry.NameFor<ActivityCompletedEvent>();
        var rows = await ctx.OutboxMessages.AsNoTracking().Where(o => o.Type == name).OrderBy(o => o.CreatedAt).ToListAsync();
        return rows.Select(o => JsonSerializer.Deserialize<ActivityCompletedEvent>(o.Content)!).ToList();
    }

    [Fact]
    public async Task Retake_CompletedActivity_ResetsAnswersAndStatus_KeepsFirstResult()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);
        var before = await InstanceAsync(s.InstanceId);
        before.FirstCompletedAt.ShouldNotBeNull();
        before.FirstCompletedAt.ShouldBe(before.EndTime);
        var revisionsBefore = (await RowsAsync(s.InstanceId)).ToDictionary(r => r.Id, r => r.AnswerRevision);

        var result = await RetakeAsync(s);

        result.Success.ShouldBeTrue(result.Message);
        result.InstanceId.ShouldBe(s.InstanceId);
        result.AlreadyRetaking.ShouldBeFalse();

        var after = await InstanceAsync(s.InstanceId);
        after.Status.ShouldBe(WorksheetInstanceStatus.Started);
        after.EndTime.ShouldBeNull();
        after.SolutionsRevealedAt.ShouldBeNull();
        after.CheckCount.ShouldBe(0);
        // Korunanlar: ilk sonuç (öğretmen takibi), ilk tamamlanma, başlangıç (atama penceresi), etkin süre.
        after.FirstCheckCorrectCount.ShouldBe(before.FirstCheckCorrectCount);
        after.FirstCheckTotalCount.ShouldBe(before.FirstCheckTotalCount);
        after.FirstCheckedAt.ShouldBe(before.FirstCheckedAt);
        after.FirstCompletedAt.ShouldBe(before.FirstCompletedAt);
        after.StartTime.ShouldBe(before.StartTime);
        after.ActiveSeconds.ShouldBe(before.ActiveSeconds);
        before.RetakeCount.ShouldBe(0);
        before.AttemptStartedAt.ShouldBeNull();
        after.RetakeCount.ShouldBe(1);
        after.AttemptStartedAt.ShouldNotBeNull();
        after.AttemptStartedAt!.Value.ShouldBeGreaterThan(before.EndTime!.Value);

        var rows = await RowsAsync(s.InstanceId);
        rows.Count.ShouldBe(2); // madde satırları kalır
        rows.ShouldAllBe(r => r.SelectedAnswerId == null && r.AnswerPayload == null && !r.IsCorrect
            && r.CorrectCount == 0 && r.TotalCount == 0 && r.Feedback == null && r.CheckedAnswerRevision == null && r.TimeTaken == 0);
        rows.ShouldAllBe(r => r.AnswerRevision == revisionsBefore[r.Id] + 1);

        // Oynatıcı oturumu Started olarak açar; start-test aynı oturumu devam ettirir (yeni oturum yok).
        await using var ctx = _db.NewContext();
        var canvas = await NewService(ctx).GetCanvasTestResultAsync(s.InstanceId, StudentUserId);
        canvas!.Status.ShouldBe(WorksheetInstanceStatus.Started);
        canvas.FirstCompletedAt.ShouldBe(before.FirstCompletedAt);
        canvas.FirstCheckCorrectCount.ShouldBe(before.FirstCheckCorrectCount);
        var start = await NewService(ctx).StartTestAsync(s.WorksheetId, new StudentProfileDto { Id = s.StudentId, GradeId = null });
        start.Success.ShouldBeTrue(start.Message);
        start.InstanceId.ShouldBe(s.InstanceId);
        (await ctx.TestInstances.CountAsync(ti => ti.StudentId == s.StudentId)).ShouldBe(1);
    }

    [Fact]
    public async Task Retake_ThenCheckAndEndAgain_KeepsFirstCheckAndFirstCompletedAt_AccumulatesActiveSeconds_SecondEventSameInstance()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s); // ilk kontrol 1/1 (yalnız madde 1 gönderildi)
        var first = await InstanceAsync(s.InstanceId);

        (await RetakeAsync(s)).Success.ShouldBeTrue();
        // Deneme 10 dk önce açılmış say (etkin süre penceresi AttemptStartedAt'ten başlar, review L3).
        await SetAttemptStartedAtAsync(s, DateTime.UtcNow.AddMinutes(-10));

        // Yeni denemede: madde 1 yanlış, madde 2 doğru → 1/2; ilk kontrol özeti DEĞİŞMEZ.
        await CheckAsync(s, s.Wrong1, s.Right2);
        var retaking = await InstanceAsync(s.InstanceId);
        retaking.CheckCount.ShouldBe(1);
        retaking.FirstCheckCorrectCount.ShouldBe(first.FirstCheckCorrectCount);
        retaking.FirstCheckTotalCount.ShouldBe(first.FirstCheckTotalCount);
        retaking.FirstCheckedAt.ShouldBe(first.FirstCheckedAt);

        await EndAsync(s, activeSeconds: 200);

        var done = await InstanceAsync(s.InstanceId);
        done.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        done.EndTime.ShouldNotBeNull();
        done.EndTime!.Value.ShouldBeGreaterThan(first.EndTime!.Value);
        done.FirstCompletedAt.ShouldBe(first.FirstCompletedAt);
        done.ActiveSeconds.ShouldBe(300 + 200);

        var events = await ActivityEventsAsync();
        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.WorksheetInstanceId == s.InstanceId && e.UserId == StudentUserId && e.ActivityId == s.WorksheetId);
        events.Select(e => e.EventId).Distinct().Count().ShouldBe(2);
        events[1].ActiveSeconds.ShouldBe(200); // event yalnız bu denemenin süresi
    }

    private async Task SetAttemptStartedAtAsync(Seed s, DateTime value)
    {
        await using var ctx = _db.NewContext();
        await ctx.TestInstances.Where(ti => ti.Id == s.InstanceId)
            .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.AttemptStartedAt, (DateTime?)value));
    }

    [Fact]
    public async Task Recompletion_ActiveSecondsWindowStartsAtAttemptStart_NotFirstStart()
    {
        // review L3: StartTime 2 gün önce; deneme 90 sn önce açıldı → bildirilen 1200 sn, 90 sn'ye kırpılır.
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);
        (await RetakeAsync(s)).Success.ShouldBeTrue();
        await SetAttemptStartedAtAsync(s, DateTime.UtcNow.AddSeconds(-90));
        await CheckAsync(s, s.Right1);

        await EndAsync(s, activeSeconds: 1200);

        (await ActivityEventsAsync())[1].ActiveSeconds.ShouldBeInRange(90, 95);
        (await InstanceAsync(s.InstanceId)).ActiveSeconds!.Value.ShouldBeInRange(300 + 90, 300 + 95);
    }

    [Fact]
    public async Task OtherActivity_CompletedWhileThisOneIsRetaking_DoesNotReclaimTheRetakenFirstAttemptWindow()
    {
        // review U1: iki etkinlik aynı anda başladı (2 gün önce). A 60 sn önce bitti, sonra yeniden çözülüyor (EndTime boş).
        // B şimdi bitiyor: pencere A'nın ilk bitişinden (FirstCompletedAt) başlar, A'nın talep ettiği süreyi tekrar saymaz.
        var a = await SeedAsync();
        var b = await SeedSecondActivityAsync(a);
        await CompleteFirstAttemptAsync(a);
        await using (var ctx = _db.NewContext())
        {
            // A'nın ilk bitişini B'nin şu anki bitişine 60 sn kalaya çek; sonra A yeniden çözülüyor.
            var near = DateTime.UtcNow.AddSeconds(-60);
            await ctx.TestInstances.Where(ti => ti.Id == a.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.EndTime, (DateTime?)near).SetProperty(ti => ti.FirstCompletedAt, (DateTime?)near));
        }
        (await RetakeAsync(a)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).CheckActivityAsync(b, new ActivityCheckRequestDto
            {
                Answers = [new ActivityCheckAnswerDto { InstanceQuestionId = (await ctx.TestInstanceQuestions.FirstAsync(r => r.WorksheetInstanceId == b)).Id }],
            }, StudentUserId)).Success.ShouldBeTrue();
        }
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).EndTest(b, StudentUserId, new EndTestRequestDto { ActiveSeconds = 1800 }, KeycloakId)).Success.ShouldBeTrue();
        }

        var bEvent = (await ActivityEventsAsync()).Single(e => e.WorksheetInstanceId == b);
        bEvent.ActiveSeconds.ShouldBeInRange(60, 65); // FirstCompletedAt hesaba katılmasaydı 1800 olurdu
    }

    private async Task<int> SeedSecondActivityAsync(Seed a)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(TeacherUserId);
        var gradeId = await ctx.Students.Where(x => x.Id == a.StudentId).Select(x => x.GradeId!.Value).SingleAsync();
        var worksheet = new Worksheet { Name = "Etkinlik 2", Description = "", GradeId = gradeId, MaxDurationSeconds = 0, Kind = WorksheetKind.Activity };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        var questionId = await ctx.TestQuestions.Where(x => x.TestId == a.WorksheetId).Select(x => x.QuestionId).FirstAsync();
        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
        ctx.TestQuestions.Add(wq);
        var instance = new WorksheetInstance
        {
            StudentId = a.StudentId, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started, StartTime = a.StartTime, MaxDurationSeconds = 0,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id });
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    [Fact]
    public async Task Retake_WhenStudentCanNoLongerStartIt_IsForbidden_AndNothingChanges()
    {
        // review M2: start-test ile aynı erişim kararı — atama bitti, sınıf eşleşiyor ama etkinlik kısıtlı görünürlükte.
        var s = await SeedAsync(assignmentEndAt: DateTime.UtcNow.AddHours(-1));
        await CompleteFirstAttemptAsync(s);
        await using (var ctx = _db.NewContext())
        {
            await ctx.Worksheets.Where(w => w.Id == s.WorksheetId)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.StudentVisibility, WorksheetStudentVisibility.Restricted));
        }

        var result = await RetakeAsync(s);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.AccessDenied);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Completed);
        (await RowsAsync(s.InstanceId)).Single(r => r.Id == s.Row1).SelectedAnswerId.ShouldBe(s.Right1);
    }

    [Fact]
    public async Task Retake_AfterDeadline_IsAllowedByGradeMatch()
    {
        var s = await SeedAsync(assignmentEndAt: DateTime.UtcNow.AddHours(-1));
        await CompleteFirstAttemptAsync(s);

        (await RetakeAsync(s)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Retake_SoftDeletedActivity_IsNotFound_AndNothingChanges()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);
        await using (var ctx = _db.NewContext())
        {
            await ctx.Worksheets.Where(w => w.Id == s.WorksheetId)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.IsDeleted, true));
        }

        var result = await RetakeAsync(s);

        result.NotFound.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.WorksheetNotFound);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Completed);
    }

    [Fact]
    public async Task Calendar_RetakingActivity_DeadlineStaysDone()
    {
        var s = await SeedAsync(assignmentEndAt: DateTime.UtcNow.AddDays(1));
        await CompleteFirstAttemptAsync(s);
        (await RetakeAsync(s)).Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var student = await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.StudentId);
        var calendar = await new WorksheetCalendarService(ctx, Substitute.For<IAuthApiClient>()).GetMyCalendarAsync(
            s.StudentId, KeycloakId, student.GradeId, student.SchoolId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(3), CancellationToken.None);

        var deadline = calendar.Events.Single(e => e.WorksheetId == s.WorksheetId);
        deadline.IsCompleted.ShouldBe(true);
    }

    [Fact]
    public async Task Retake_TestKind_IsRejectedAsNotAnActivity_AndNothingChanges()
    {
        var s = await SeedAsync(WorksheetKind.Test);
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(new SaveAnswerDto
            {
                TestInstanceId = s.InstanceId, TestQuestionId = s.Row1, SelectedAnswerId = s.Right1, TimeTaken = 5,
            }, Student)).Success.ShouldBeTrue();
        }
        await EndAsync(s);

        var result = await RetakeAsync(s);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.NotAnActivity);
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        (await RowsAsync(s.InstanceId)).Single(r => r.Id == s.Row1).SelectedAnswerId.ShouldBe(s.Right1);
    }

    [Fact]
    public async Task Retake_OtherStudentsInstance_IsNotFound_AndNothingChanges()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);

        var result = await RetakeAsync(s, OtherUserId);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Completed);
        (await RowsAsync(s.InstanceId)).Single(r => r.Id == s.Row1).SelectedAnswerId.ShouldBe(s.Right1);
    }

    [Fact]
    public async Task Retake_NeverCompletedStartedInstance_IsRejected_AndAnswersKept()
    {
        var s = await SeedAsync();
        await CheckAsync(s, s.Right1);

        var result = await RetakeAsync(s);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityNotCompleted);
        var instance = await InstanceAsync(s.InstanceId);
        instance.CheckCount.ShouldBe(1);
        instance.FirstCompletedAt.ShouldBeNull();
        (await RowsAsync(s.InstanceId)).Single(r => r.Id == s.Row1).SelectedAnswerId.ShouldBe(s.Right1);
    }

    [Fact]
    public async Task Retake_ExpiredInstance_IsRejected()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.TestInstances.Where(ti => ti.Id == s.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.Status, WorksheetInstanceStatus.Expired)
                    .SetProperty(ti => ti.EndTime, (DateTime?)DateTime.UtcNow));
        }

        var result = await RetakeAsync(s);

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityNotCompleted);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Expired);
    }

    [Fact]
    public async Task Retake_Twice_SecondIsIdempotent_AndDoesNotWipeNewAnswers()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);
        (await RetakeAsync(s)).AlreadyRetaking.ShouldBeFalse();
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(new SaveAnswerDto
            {
                TestInstanceId = s.InstanceId, TestQuestionId = s.Row2, SelectedAnswerId = s.Right2, TimeTaken = 3,
            }, Student)).Success.ShouldBeTrue();
        }

        var second = await RetakeAsync(s);

        second.Success.ShouldBeTrue();
        second.AlreadyRetaking.ShouldBeTrue();
        (await RowsAsync(s.InstanceId)).Single(r => r.Id == s.Row2).SelectedAnswerId.ShouldBe(s.Right2);
    }

    /// <summary>
    /// Yarış: bu istek ön okumada Completed gördü, ama transaction'ını açmadan önce başka bir retake kazandı ve öğrenci yeni
    /// denemede cevap yazdı. Koşullu UPDATE 0 satır görür → kaybeden hiçbir şeyi sıfırlamaz, idempotent başarı döner.
    /// </summary>
    [Fact]
    public async Task Retake_ConcurrentRetakes_LoserDoesNotResetTheWinnersAttempt()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);

        var interceptor = new BeforeFirstTransaction(async () =>
        {
            await using var winnerCtx = _db.NewContext();
            (await NewService(winnerCtx).RetakeActivityAsync(s.InstanceId, StudentUserId)).AlreadyRetaking.ShouldBeFalse();
            await using var saveCtx = _db.NewContext();
            (await NewService(saveCtx).SaveAnswer(new SaveAnswerDto
            {
                TestInstanceId = s.InstanceId, TestQuestionId = s.Row2, SelectedAnswerId = s.Right2, TimeTaken = 4,
            }, Student)).Success.ShouldBeTrue();
        });

        ActivityRetakeResultDto loser;
        await using (var ctx = _db.NewContext(interceptor))
        {
            loser = await NewService(ctx).RetakeActivityAsync(s.InstanceId, StudentUserId);
        }

        interceptor.Fired.ShouldBeTrue();
        loser.Success.ShouldBeTrue();
        loser.AlreadyRetaking.ShouldBeTrue();
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Started);
        (await RowsAsync(s.InstanceId)).Single(r => r.Id == s.Row2).SelectedAnswerId.ShouldBe(s.Right2);
    }

    [Fact]
    public async Task TeacherTracking_RetakingAfterDeadline_StillCompletedWithFirstResult()
    {
        // Teslim tarihi geçmiş atama: öğrenci etkinliği bitirdi, sonra (teslimden sonra) yeniden çözüyor.
        var s = await SeedAsync(assignmentEndAt: DateTime.UtcNow.AddHours(-1));
        await CompleteFirstAttemptAsync(s);
        var first = await InstanceAsync(s.InstanceId);
        (await RetakeAsync(s)).Success.ShouldBeTrue();
        await CheckAsync(s, s.Wrong1, s.Right2);

        await using var ctx = _db.NewContext();
        var overview = await new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx))
            .GetWorksheetAssignmentsForTeacherAsync(s.WorksheetId, SchoolScope.For(TeacherUserId, s.SchoolId));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.CompletedCount.ShouldBe(1);
        assignment.InProgressCount.ShouldBe(0);
        assignment.ExpiredCount.ShouldBe(0);
        var row = assignment.Students.ShouldHaveSingleItem();
        row.Status.ShouldBe(AssignmentStudentStatuses.Completed);
        row.Retaking.ShouldBeTrue();
        row.InstanceId.ShouldBe(s.InstanceId);
        row.FirstCheckCorrectCount.ShouldBe(first.FirstCheckCorrectCount);
        row.FirstCheckTotalCount.ShouldBe(first.FirstCheckTotalCount);
        row.LastActivity.ShouldBe(first.FirstCompletedAt);
        row.RetakeCount.ShouldBe(1);
    }

    [Fact]
    public async Task StudentAssignments_RetakingActivity_StaysCompletedButInstanceIsStarted()
    {
        var s = await SeedAsync();
        await CompleteFirstAttemptAsync(s);
        (await RetakeAsync(s)).Success.ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var student = await ctx.Students.AsNoTracking().SingleAsync(x => x.Id == s.StudentId);
        var list = await new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx)).GetActiveAssignmentsForStudentAsync(
            new StudentProfileDto { Id = student.Id, GradeId = student.GradeId, SchoolId = student.SchoolId });

        var item = list.ShouldHaveSingleItem();
        item.InstanceId.ShouldBe(s.InstanceId);
        item.InstanceStatus.ShouldBe(WorksheetInstanceStatus.Started);
        item.AssignmentStatus.ShouldBe("Completed");
        item.Retaking.ShouldBeTrue();
        item.IsCompleted.ShouldBeTrue();
        item.IsFinished.ShouldBeTrue(); // review U3: teslim/aciliyet uyarısı yanmaz
    }

    /// <summary>Bağlantıda ilk transaction açılmadan hemen önce bir kez <paramref name="action"/> çalıştırır (yarış simülasyonu).</summary>
    private sealed class BeforeFirstTransaction(Func<Task> action) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await action();
            }
            return result;
        }
    }
}
