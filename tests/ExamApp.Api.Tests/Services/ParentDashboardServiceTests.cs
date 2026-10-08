using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #420: veli özeti hesapları — Pazartesi başlangıçlı yerel hafta (Europe/Istanbul) çözülen soru (worksheet + pratik,
/// pas ve cevapsız hariç), atama durum sayıları (öğretmen durum kuralıyla aynı; worksheet başına tek; 30 gün geriye bakış;
/// gelecekteki/silinmiş/başka okulun ataması hariç), toplam puan ve son aktivite. Başka öğrencinin verisi sızmaz.
/// </summary>
public class ParentDashboardServiceTests : IDisposable
{
    private const int ParentUser = 43001;
    private const int StudentUser = 43101;
    private const int OtherStudentUser = 43102;

    // Çarşamba 2026-10-07 09:00 UTC = 12:00 TR. Hafta: Pazartesi 2026-10-05 00:00 TR = 2026-10-04 21:00 UTC.
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WeekStartUtc = new(2026, 10, 4, 21, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(Now));
    private int _seq;

    private int _studentId, _otherStudentId, _gradeId, _schoolId, _otherSchoolId;

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var otherSchool = new School { Name = "Başka Okul" };
        var grade = new Grade { Name = "7. Sınıf" };
        ctx.AddRange(school, otherSchool, grade);
        await ctx.SaveChangesAsync();

        var student = new Student
        {
            UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, SchoolVerifiedAt = Now.AddDays(-30), GradeId = grade.Id
        };
        var other = new Student
        {
            UserId = OtherStudentUser, StudentNumber = "s2", SchoolId = school.Id, SchoolVerifiedAt = Now.AddDays(-30), GradeId = grade.Id
        };
        var parent = new Parent { UserId = ParentUser };
        ctx.AddRange(student, other, parent);
        await ctx.SaveChangesAsync();

        ctx.ParentStudentLinks.Add(new ParentStudentLink
        {
            Origin = ParentStudentLinkOrigin.ParentCreated,
            ParentId = parent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Active,
            CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
        });
        await ctx.SaveChangesAsync();

        (_studentId, _otherStudentId, _gradeId, _schoolId, _otherSchoolId) = (student.Id, other.Id, grade.Id, school.Id, otherSchool.Id);
    }

    private async Task<ExamApp.Api.Models.Dtos.ParentDashboard.ParentChildSummaryDto> SummaryAsync()
    {
        await using var ctx = _db.NewContext();
        var service = new ParentDashboardService(ctx, new ParentChildAccess(ctx), new ParentAccessAuditLog(ctx, _time),
            new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _time), _time);
        return (await service.GetChildSummaryAsync(ParentUser, _studentId)).ShouldNotBeNull();
    }

    private async Task<int> WorksheetAsync(bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = $"W{++_seq}", Description = "", GradeId = _gradeId, IsDeleted = deleted };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task AssignAsync(int worksheetId, DateTime startAt, DateTime? endAt, int? studentId = null,
        bool toGrade = false, int? schoolId = null, bool deleted = false)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            StudentId = toGrade ? null : studentId ?? _studentId,
            GradeId = toGrade ? _gradeId : null,
            SchoolId = schoolId,
            StartAt = startAt,
            EndAt = endAt,
            IsDeleted = deleted
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> InstanceAsync(int worksheetId, DateTime startTime, WorksheetInstanceStatus status,
        DateTime? endTime = null, int? studentId = null)
    {
        await using var ctx = _db.NewContext();
        var instance = new WorksheetInstance
        {
            StudentId = studentId ?? _studentId, WorksheetId = worksheetId, StartTime = startTime, Status = status, EndTime = endTime
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    /// <summary>Instance'a bir soru satırı ekler; <paramref name="answeredAt"/> null ise cevapsız (test başlarken açılan satır).</summary>
    private async Task AnswerAsync(int instanceId, int worksheetId, DateTime? answeredAt)
    {
        await using var ctx = _db.NewContext();
        var question = new Question { Text = $"q{++_seq}", Point = 1 };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        var answer = new Answer { QuestionId = question.Id, Text = "A", Tag = "A" };
        var wq = new WorksheetQuestion { TestId = worksheetId, QuestionId = question.Id, Order = _seq };
        ctx.AddRange(answer, wq);
        await ctx.SaveChangesAsync();
        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
        {
            WorksheetInstanceId = instanceId,
            WorksheetQuestionId = wq.Id,
            SelectedAnswerId = answeredAt.HasValue ? answer.Id : null,
            UpdateTime = answeredAt
        });
        await ctx.SaveChangesAsync();
    }

    private async Task PracticeAsync(DateTime? answeredAt, bool skipped = false, int? studentId = null)
    {
        await using var ctx = _db.NewContext();
        var session = new PracticeSession { StudentId = studentId ?? _studentId, GradeId = _gradeId, StartTime = Now.AddDays(-10) };
        var question = new Question { Text = $"p{++_seq}", Point = 1 };
        ctx.AddRange(session, question);
        await ctx.SaveChangesAsync();
        ctx.PracticeSessionQuestions.Add(new PracticeSessionQuestion
        {
            PracticeSessionId = session.Id, QuestionId = question.Id, ShownAt = Now.AddDays(-10),
            AnsweredAt = answeredAt, IsSkipped = skipped
        });
        await ctx.SaveChangesAsync();
    }

    [Theory]
    [InlineData("2026-10-05", "2026-10-05")] // Pazartesi
    [InlineData("2026-10-07", "2026-10-05")] // Çarşamba
    [InlineData("2026-10-11", "2026-10-05")] // Pazar → aynı haftanın Pazartesi'si
    [InlineData("2026-10-12", "2026-10-12")]
    public void StartOfWeek_is_monday(string day, string expected)
        => ParentDashboardService.StartOfWeek(DateOnly.Parse(day)).ShouldBe(DateOnly.Parse(expected));

    [Fact]
    public async Task Questions_solved_this_week_uses_istanbul_monday_and_both_sources()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        var instance = await InstanceAsync(ws, Now.AddDays(-5), WorksheetInstanceStatus.Started);
        await AnswerAsync(instance, ws, WeekStartUtc.AddMinutes(30));    // Pzt 00:30 TR → sayılır
        await AnswerAsync(instance, ws, WeekStartUtc.AddMinutes(-30));   // Paz 23:30 TR → geçen hafta
        await AnswerAsync(instance, ws, Now.AddHours(-1));               // sayılır
        await AnswerAsync(instance, ws, null);                           // cevapsız satır

        await PracticeAsync(Now.AddHours(-2));                           // sayılır
        await PracticeAsync(Now.AddHours(-2), skipped: true);            // pas → sayılmaz
        await PracticeAsync(null);                                       // gösterildi, cevaplanmadı
        await PracticeAsync(WeekStartUtc.AddHours(-1));                  // geçen hafta

        // Başka öğrencinin cevapları sızmaz.
        var otherWs = await WorksheetAsync();
        var otherInstance = await InstanceAsync(otherWs, Now.AddDays(-1), WorksheetInstanceStatus.Started, studentId: _otherStudentId);
        await AnswerAsync(otherInstance, otherWs, Now.AddHours(-1));
        await PracticeAsync(Now.AddHours(-1), studentId: _otherStudentId);

        var summary = await SummaryAsync();

        summary.WeekStart.ShouldBe(new DateOnly(2026, 10, 5));
        summary.QuestionsSolvedThisWeek.ShouldBe(3);
    }

    [Fact]
    public async Task Assignment_counts_follow_teacher_status_rule_and_window()
    {
        await SeedAsync();
        var start = Now.AddDays(-7);

        var completed = await WorksheetAsync();
        await AssignAsync(completed, start, Now.AddDays(3));
        await InstanceAsync(completed, Now.AddDays(-2), WorksheetInstanceStatus.Completed, Now.AddDays(-2));

        var completedLate = await WorksheetAsync(); // teslim tarihi geçti ama bitirilmişti → Completed
        await AssignAsync(completedLate, start, Now.AddDays(-1));
        await InstanceAsync(completedLate, Now.AddDays(-3), WorksheetInstanceStatus.Completed, Now.AddDays(-3));

        var openNotStarted = await WorksheetAsync();
        await AssignAsync(openNotStarted, start, Now.AddDays(2));

        var openNoDeadline = await WorksheetAsync();
        await AssignAsync(openNoDeadline, start, null);

        var openInProgress = await WorksheetAsync();
        await AssignAsync(openInProgress, start, Now.AddDays(2));
        await InstanceAsync(openInProgress, Now.AddHours(-3), WorksheetInstanceStatus.Started);

        var missed = await WorksheetAsync(); // teslim tarihi geçti, hiç başlanmadı
        await AssignAsync(missed, start, Now.AddDays(-2));

        var lateInProgress = await WorksheetAsync(); // başlandı, bitirilmeden teslim tarihi geçti
        await AssignAsync(lateInProgress, start, Now.AddDays(-1));
        await InstanceAsync(lateInProgress, Now.AddDays(-2), WorksheetInstanceStatus.Started);

        var timedOut = await WorksheetAsync(); // süre sınırıyla kapandı (#396) — tekrar çözülemez
        await AssignAsync(timedOut, start, Now.AddDays(5));
        await InstanceAsync(timedOut, Now.AddDays(-1), WorksheetInstanceStatus.Expired, Now.AddDays(-1));

        var gradeSameSchool = await WorksheetAsync();
        await AssignAsync(gradeSameSchool, start, Now.AddDays(4), toGrade: true, schoolId: _schoolId);

        // Aynı worksheet: doğrudan atama gecikmiş + sınıf ataması hâlâ açık → tek sayım, Pending.
        var duplicated = await WorksheetAsync();
        await AssignAsync(duplicated, Now.AddDays(-20), Now.AddDays(-10));
        await AssignAsync(duplicated, start, Now.AddDays(6), toGrade: true, schoolId: _schoolId);

        // Hariç tutulanlar.
        var tooOld = await WorksheetAsync();
        await AssignAsync(tooOld, Now.AddDays(-60), Now.AddDays(-40));
        var future = await WorksheetAsync();
        await AssignAsync(future, Now.AddDays(1), Now.AddDays(5));
        var withdrawn = await WorksheetAsync();
        await AssignAsync(withdrawn, start, Now.AddDays(-1), deleted: true);
        var retired = await WorksheetAsync(deleted: true);
        await AssignAsync(retired, start, Now.AddDays(-1));
        var gradeOtherSchool = await WorksheetAsync();
        await AssignAsync(gradeOtherSchool, start, Now.AddDays(-1), toGrade: true, schoolId: _otherSchoolId);
        var otherStudents = await WorksheetAsync();
        await AssignAsync(otherStudents, start, Now.AddDays(-1), studentId: _otherStudentId);

        var counts = (await SummaryAsync()).Assignments;

        counts.Completed.ShouldBe(2);
        counts.Pending.ShouldBe(5); // openNotStarted, openNoDeadline, openInProgress, gradeSameSchool, duplicated
        counts.Overdue.ShouldBe(3); // missed, lateInProgress, timedOut
        counts.WindowDays.ShouldBe(ParentDashboardService.AssignmentWindowDays);
    }

    [Fact]
    public async Task Instance_started_before_assignment_window_counts_only_if_finished()
    {
        await SeedAsync();
        // Atamadan önce bitirilmiş test atamayı karşılar (#367).
        var doneBefore = await WorksheetAsync();
        await InstanceAsync(doneBefore, Now.AddDays(-9), WorksheetInstanceStatus.Completed, Now.AddDays(-9));
        await AssignAsync(doneBefore, Now.AddDays(-5), Now.AddDays(2));

        // Atamadan önce başlanmış ama bitmemiş test pencereye sayılmaz → başlanmadı (Pending).
        var startedBefore = await WorksheetAsync();
        await InstanceAsync(startedBefore, Now.AddDays(-9), WorksheetInstanceStatus.Started);
        await AssignAsync(startedBefore, Now.AddDays(-5), Now.AddDays(2));

        var counts = (await SummaryAsync()).Assignments;

        counts.Completed.ShouldBe(1);
        counts.Pending.ShouldBe(1);
        counts.Overdue.ShouldBe(0);
    }

    [Fact]
    public async Task Points_and_last_activity()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentPoints.Add(new StudentPoint { StudentId = _studentId, XP = 1250 });
            ctx.StudentPoints.Add(new StudentPoint { StudentId = _otherStudentId, XP = 9000 });
            await ctx.SaveChangesAsync();
        }

        var ws = await WorksheetAsync();
        var instance = await InstanceAsync(ws, Now.AddDays(-3), WorksheetInstanceStatus.Completed, Now.AddDays(-3).AddMinutes(20));
        await AnswerAsync(instance, ws, Now.AddDays(-3).AddMinutes(10));
        var lastPractice = Now.AddHours(-5).AddMinutes(37); // 04:37 -> saate kesilir: 04:00
        await PracticeAsync(lastPractice, skipped: true); // pas da bir aktivitedir
        await PracticeAsync(Now.AddHours(-1), studentId: _otherStudentId);

        var summary = await SummaryAsync();

        summary.TotalPoints.ShouldBe(1250);
        summary.LastActivityAt.ShouldBe(new DateTime(2026, 10, 7, 4, 0, 0, DateTimeKind.Utc));
        summary.LastActivityAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Last_activity_considers_test_start_and_finish()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        var finishedAt = Now.AddHours(-4).AddMinutes(12).AddSeconds(5); // 05:12:05
        await InstanceAsync(ws, Now.AddHours(-6), WorksheetInstanceStatus.Completed, finishedAt);

        (await SummaryAsync()).LastActivityAt.ShouldBe(new DateTime(2026, 10, 7, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Fresh_student_has_zeroes_and_no_activity()
    {
        await SeedAsync();

        var summary = await SummaryAsync();

        summary.StudentId.ShouldBe(_studentId);
        summary.QuestionsSolvedThisWeek.ShouldBe(0);
        summary.TotalPoints.ShouldBe(0);
        summary.LastActivityAt.ShouldBeNull();
        summary.Assignments.Completed.ShouldBe(0);
        summary.Assignments.Pending.ShouldBe(0);
        summary.Assignments.Overdue.ShouldBe(0);
    }

    [Fact]
    public async Task Last_activity_from_test_start_only()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await InstanceAsync(ws, Now.AddMinutes(-50), WorksheetInstanceStatus.Started); // 08:10, bitirilmedi

        (await SummaryAsync()).LastActivityAt.ShouldBe(new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TruncateToHour_drops_minutes_and_marks_utc()
    {
        var truncated = ParentDashboardService.TruncateToHour(new DateTime(2026, 10, 7, 23, 59, 59, 999, DateTimeKind.Unspecified));
        truncated.ShouldBe(new DateTime(2026, 10, 7, 23, 0, 0, DateTimeKind.Utc));
        truncated.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(AssignmentStudentStatuses.Completed, -1, ParentAssignmentBucket.Completed)]
    [InlineData(AssignmentStudentStatuses.Completed, 1, ParentAssignmentBucket.Completed)]
    [InlineData(AssignmentStudentStatuses.Expired, 1, ParentAssignmentBucket.Overdue)]
    [InlineData(AssignmentStudentStatuses.InProgress, -1, ParentAssignmentBucket.Overdue)]
    [InlineData(AssignmentStudentStatuses.InProgress, 1, ParentAssignmentBucket.Pending)]
    [InlineData(AssignmentStudentStatuses.NotStarted, 1, ParentAssignmentBucket.Pending)]
    [InlineData(AssignmentStudentStatuses.NotStarted, null, ParentAssignmentBucket.Pending)]
    [InlineData(AssignmentStudentStatuses.InProgress, null, ParentAssignmentBucket.Pending)]
    public void ToBucket_maps_teacher_status(string status, int? endOffsetDays, ParentAssignmentBucket expected)
    {
        DateTime? endAt = endOffsetDays.HasValue ? Now.AddDays(endOffsetDays.Value) : null;
        ParentDashboardService.ToBucket(status, endAt, Now).ShouldBe(expected);
    }
}
