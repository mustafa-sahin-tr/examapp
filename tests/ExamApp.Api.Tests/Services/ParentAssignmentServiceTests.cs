using System.Data.Common;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #421 (epic #407 V3): veli ödev/test listesi + test sonuç özeti. Kapı → audit → veri sırası (kapıdan geçmeyen istek
/// hiçbir veri sorgusu çalıştırmaz); IDOR (başka velinin çocuğu, başka çocuğun oturumu, Active olmayan bağlantı → 404);
/// V2 ile aynı kovalar; sayfalama ve sıralama; DTO alan listesi kilitli (soru/cevap içeriği yok).
/// </summary>
public class ParentAssignmentServiceTests : IDisposable
{
    private const int ParentUser = 44001;
    private const int OtherParentUser = 44002;
    private const int PendingParentUser = 44003;
    private const int StudentUser = 44101;
    private const int OtherStudentUser = 44102;
    private const int TeacherUser = 44201;

    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private int _seq;

    private int _studentId, _otherStudentId, _gradeId, _schoolId, _subjectId, _parentId;

    public ParentAssignmentServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<UserLookupResultDto>)ci.Arg<IEnumerable<int>>()
                .Where(id => id == TeacherUser)
                .Select(id => new UserLookupResultDto { Id = id, FullName = "Zeynep Öğretmen", Email = "secret@x.com" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var grade = new Grade { Name = "7. Sınıf" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(school, grade, subject);
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
        var otherParent = new Parent { UserId = OtherParentUser };
        var pendingParent = new Parent { UserId = PendingParentUser };
        ctx.AddRange(student, other, parent, otherParent, pendingParent);
        await ctx.SaveChangesAsync();

        ctx.ParentStudentLinks.AddRange(
            new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated,
                ParentId = parent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Active,
                CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
            },
            // Başka veli, BAŞKA çocuğa bağlı (IDOR: bu veli bizim öğrencimizi göremez).
            new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated,
                ParentId = otherParent.Id, StudentId = other.Id, Status = ParentStudentLinkStatus.Active,
                CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
            },
            // Onay bekleyen istek erişim vermez.
            new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated,
                ParentId = pendingParent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Pending,
                CreatedAt = Now.AddHours(-2)
            });
        await ctx.SaveChangesAsync();

        (_studentId, _otherStudentId, _gradeId, _schoolId, _subjectId, _parentId) =
            (student.Id, other.Id, grade.Id, school.Id, subject.Id, parent.Id);
    }

    private ParentAssignmentService Service(AppDbContext ctx, IParentChildAccess? access = null, IParentAccessAuditLog? audit = null)
        => new(ctx, access ?? new ParentChildAccess(ctx), audit ?? new ParentAccessAuditLog(ctx, _time), _authApi, time: _time);

    private async Task<ParentChildAssignmentListDto?> ListAsync(ParentAssignmentBucket? status = null, int page = 1, int parentUser = ParentUser)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).GetAssignmentsAsync(parentUser, _studentId, status, page);
    }

    private async Task<ParentTestResultLookup> ResultAsync(int instanceId, int parentUser = ParentUser, int? studentId = null)
    {
        await using var ctx = _db.NewContext();
        return await Service(ctx).GetTestResultAsync(parentUser, studentId ?? _studentId, instanceId);
    }

    private async Task<int> WorksheetAsync(string? name = null, bool withSubject = true)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet
        {
            Name = name ?? $"W{++_seq}", Description = "gizli açıklama", GradeId = _gradeId,
            SubjectId = withSubject ? _subjectId : null
        };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private async Task AssignAsync(int worksheetId, DateTime startAt, DateTime? endAt, int? studentId = null, bool toGrade = false,
        int? createUserId = TeacherUser)
    {
        await using var ctx = _db.NewContext();
        var assignment = new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            StudentId = toGrade ? null : studentId ?? _studentId,
            GradeId = toGrade ? _gradeId : null,
            SchoolId = toGrade ? _schoolId : null,
            StartAt = startAt,
            EndAt = endAt
        };
        ctx.WorksheetAssignments.Add(assignment);
        await ctx.SaveChangesAsync();
        // Audit hook'u CreateUserId'yi o anki kullanıcıyla (testte yok) yazar; atayanı sonradan sabitle.
        await ctx.WorksheetAssignments.Where(a => a.Id == assignment.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.CreateUserId, createUserId));
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

    private enum Pick { Correct, Wrong, Blank }

    /// <summary>Soru + iki şık (A doğru, B yanlış) + oturum satırı; <paramref name="topicName"/> null ise konusuz.</summary>
    private async Task QuestionAsync(int instanceId, int worksheetId, Pick pick, string? topicName = "Kesirler")
    {
        await using var ctx = _db.NewContext();
        int? topicId = null;
        if (topicName != null)
        {
            var topic = await ctx.Topics.FirstOrDefaultAsync(t => t.Name == topicName);
            if (topic == null)
            {
                topic = new Topic { Name = topicName, SubjectId = _subjectId, GradeId = _gradeId };
                ctx.Topics.Add(topic);
                await ctx.SaveChangesAsync();
            }

            topicId = topic.Id;
        }

        var question = new Question { Text = $"GIZLI-SORU-METNI-{++_seq}", ImageUrl = "gizli/gorsel.png", Point = 1, TopicId = topicId };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();
        var right = new Answer { QuestionId = question.Id, Text = "GIZLI-DOGRU-SIK", Tag = "A" };
        var wrong = new Answer { QuestionId = question.Id, Text = "GIZLI-YANLIS-SIK", Tag = "B" };
        ctx.AddRange(right, wrong);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = right.Id;
        var wq = new WorksheetQuestion { TestId = worksheetId, QuestionId = question.Id, Order = _seq };
        ctx.Add(wq);
        await ctx.SaveChangesAsync();
        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
        {
            WorksheetInstanceId = instanceId,
            WorksheetQuestionId = wq.Id,
            SelectedAnswerId = pick switch { Pick.Correct => right.Id, Pick.Wrong => wrong.Id, _ => null },
            UpdateTime = pick == Pick.Blank ? null : Now.AddHours(-1)
        });
        await ctx.SaveChangesAsync();
    }

    private Task<int> AuditCountAsync(string endpoint)
    {
        using var ctx = _db.NewContext();
        return Task.FromResult(ctx.ParentAccessAudits.Count(a => a.Endpoint == endpoint));
    }

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task List_uses_v2_buckets_newest_deadline_first_and_scores_completed_items()
    {
        await SeedAsync();
        var start = Now.AddDays(-7);

        var done = await WorksheetAsync("Kesirler testi");
        await AssignAsync(done, start, Now.AddDays(3));
        var doneInstance = await InstanceAsync(done, Now.AddMinutes(-40), WorksheetInstanceStatus.Completed, Now.AddMinutes(-15));
        await QuestionAsync(doneInstance, done, Pick.Correct);
        await QuestionAsync(doneInstance, done, Pick.Correct);
        await QuestionAsync(doneInstance, done, Pick.Wrong, "Ondalık");
        await QuestionAsync(doneInstance, done, Pick.Blank, null);

        var open = await WorksheetAsync("Açık uçlu", withSubject: false);
        await AssignAsync(open, start, null, toGrade: true);

        var missed = await WorksheetAsync("Kaçırılan");
        await AssignAsync(missed, start, Now.AddDays(-2), createUserId: null);

        var timedOut = await WorksheetAsync("Süresi doldu");
        await AssignAsync(timedOut, start, Now.AddDays(5));
        var timedOutInstance = await InstanceAsync(timedOut, Now.AddDays(-1), WorksheetInstanceStatus.Expired, Now.AddDays(-1).AddMinutes(30));
        await QuestionAsync(timedOutInstance, timedOut, Pick.Wrong);

        var inProgress = await WorksheetAsync("Devam ediyor");
        await AssignAsync(inProgress, start, Now.AddDays(1));
        var inProgressInstance = await InstanceAsync(inProgress, Now.AddHours(-1), WorksheetInstanceStatus.Started);
        await QuestionAsync(inProgressInstance, inProgress, Pick.Correct);

        // Görünmeyenler: başka öğrencinin ataması, gelecekteki atama, 30 günden eski gecikme.
        var others = await WorksheetAsync();
        await AssignAsync(others, start, Now.AddDays(2), studentId: _otherStudentId);
        var future = await WorksheetAsync();
        await AssignAsync(future, Now.AddDays(1), Now.AddDays(4));
        var tooOld = await WorksheetAsync();
        await AssignAsync(tooOld, Now.AddDays(-60), Now.AddDays(-40));

        var list = (await ListAsync()).ShouldNotBeNull();

        list.StudentId.ShouldBe(_studentId);
        list.Status.ShouldBeNull();
        list.Page.ShouldBe(1);
        list.PageSize.ShouldBe(ParentAssignmentService.PageSize);
        list.TotalCount.ShouldBe(5);
        list.Counts.Completed.ShouldBe(1);
        list.Counts.Overdue.ShouldBe(2);
        list.Counts.Pending.ShouldBe(2);
        list.Counts.WindowDays.ShouldBe(ParentAssignmentScope.WindowDays);

        // Teslim tarihsiz en üstte, sonra en yeni teslim tarihi.
        list.Items.Select(i => i.Title).ShouldBe(new[] { "Açık uçlu", "Süresi doldu", "Kesirler testi", "Devam ediyor", "Kaçırılan" });

        var doneRow = list.Items.Single(i => i.WorksheetId == done);
        doneRow.Status.ShouldBe(ParentAssignmentStatuses.Completed);
        doneRow.Subject.ShouldBe("Matematik");
        doneRow.TeacherName.ShouldBe("Zeynep Öğretmen");
        doneRow.StartAt.ShouldBe(start);
        doneRow.StartAt.Kind.ShouldBe(DateTimeKind.Utc);
        doneRow.Deadline.ShouldBe(Now.AddDays(3));
        doneRow.TestInstanceId.ShouldBe(doneInstance);
        var score = doneRow.Result.ShouldNotBeNull();
        score.CorrectCount.ShouldBe(2);
        score.WrongCount.ShouldBe(1);
        score.BlankCount.ShouldBe(1);
        score.TotalCount.ShouldBe(4);
        score.ScorePercent.ShouldBe(50);
        score.DurationSeconds.ShouldBe(25 * 60);

        var openRow = list.Items.Single(i => i.WorksheetId == open);
        openRow.Status.ShouldBe(ParentAssignmentStatuses.Pending);
        openRow.Deadline.ShouldBeNull();
        openRow.Subject.ShouldBeNull();
        openRow.TestInstanceId.ShouldBeNull();
        openRow.Result.ShouldBeNull();

        var missedRow = list.Items.Single(i => i.WorksheetId == missed);
        missedRow.Status.ShouldBe(ParentAssignmentStatuses.Overdue);
        missedRow.TeacherName.ShouldBeNull(); // legacy atama: atayan yok

        // Süresi dolan test Overdue kovasında ama sonucu var (öğrenci de kendi sonucunu görür, #396).
        var timedOutRow = list.Items.Single(i => i.WorksheetId == timedOut);
        timedOutRow.Status.ShouldBe(ParentAssignmentStatuses.Overdue);
        timedOutRow.TestInstanceId.ShouldBe(timedOutInstance);
        timedOutRow.Result!.WrongCount.ShouldBe(1);

        // Devam eden oturumun sonucu açılmaz.
        var inProgressRow = list.Items.Single(i => i.WorksheetId == inProgress);
        inProgressRow.Status.ShouldBe(ParentAssignmentStatuses.Pending);
        inProgressRow.TestInstanceId.ShouldBeNull();
        inProgressRow.Result.ShouldBeNull();
    }

    [Fact]
    public async Task List_counts_match_the_v2_summary()
    {
        await SeedAsync();
        var start = Now.AddDays(-7);
        var a = await WorksheetAsync();
        await AssignAsync(a, start, Now.AddDays(3));
        await InstanceAsync(a, Now.AddDays(-1), WorksheetInstanceStatus.Completed, Now.AddDays(-1));
        var b = await WorksheetAsync();
        await AssignAsync(b, start, Now.AddDays(-1));
        // Aynı worksheet iki kez atanmış (gecikmiş doğrudan + açık sınıf ataması) → tek satır, Pending.
        var c = await WorksheetAsync();
        await AssignAsync(c, Now.AddDays(-20), Now.AddDays(-10));
        await AssignAsync(c, start, Now.AddDays(6), toGrade: true);

        var list = (await ListAsync()).ShouldNotBeNull();
        await using var ctx = _db.NewContext();
        var summary = (await new ParentDashboardService(ctx, new ParentChildAccess(ctx), new ParentAccessAuditLog(ctx, _time), time: _time)
            .GetChildSummaryAsync(ParentUser, _studentId)).ShouldNotBeNull();

        list.Counts.Completed.ShouldBe(summary.Assignments.Completed);
        list.Counts.Overdue.ShouldBe(summary.Assignments.Overdue);
        list.Counts.Pending.ShouldBe(summary.Assignments.Pending);
        list.Items.Count.ShouldBe(3);
        list.Items.Single(i => i.WorksheetId == c).Deadline.ShouldBe(Now.AddDays(6)); // temsilci: açık atama
    }

    [Fact]
    public async Task Status_filter_and_pagination()
    {
        await SeedAsync();
        for (var i = 0; i < 23; i++)
        {
            var ws = await WorksheetAsync($"Geciken {i:00}");
            await AssignAsync(ws, Now.AddDays(-25), Now.AddDays(-1).AddMinutes(-i)); // i büyüdükçe daha eski teslim tarihi
        }

        var pending = await WorksheetAsync("Bekleyen");
        await AssignAsync(pending, Now.AddDays(-1), Now.AddDays(2));

        var page1 = (await ListAsync(ParentAssignmentBucket.Overdue, 1)).ShouldNotBeNull();
        page1.Status.ShouldBe(ParentAssignmentStatuses.Overdue);
        page1.TotalCount.ShouldBe(23);
        page1.Items.Count.ShouldBe(20);
        page1.Items.ShouldAllBe(i => i.Status == ParentAssignmentStatuses.Overdue);
        page1.Items.First().Title.ShouldBe("Geciken 00");
        page1.Counts.Pending.ShouldBe(1); // çip sayıları filtreden bağımsız
        page1.Counts.Overdue.ShouldBe(23);

        var page2 = (await ListAsync(ParentAssignmentBucket.Overdue, 2)).ShouldNotBeNull();
        page2.Items.Select(i => i.Title).ShouldBe(new[] { "Geciken 20", "Geciken 21", "Geciken 22" });

        (await ListAsync(ParentAssignmentBucket.Overdue, 3))!.Items.ShouldBeEmpty();

        var pendingOnly = (await ListAsync(ParentAssignmentBucket.Pending)).ShouldNotBeNull();
        pendingOnly.Items.ShouldHaveSingleItem().Title.ShouldBe("Bekleyen");
        (await ListAsync(ParentAssignmentBucket.Completed))!.Items.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("", true, null)]
    [InlineData("completed", true, ParentAssignmentBucket.Completed)]
    [InlineData("OVERDUE", true, ParentAssignmentBucket.Overdue)]
    [InlineData(" pending ", true, ParentAssignmentBucket.Pending)]
    [InlineData("done", false, null)]
    [InlineData("1", false, null)]
    public void TryParseStatus(string? value, bool ok, ParentAssignmentBucket? expected)
    {
        ParentAssignmentService.TryParseStatus(value, out var bucket).ShouldBe(ok);
        bucket.ShouldBe(expected);
    }

    [Fact]
    public async Task Teacher_lookup_failure_keeps_list_with_null_names()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-1), Now.AddDays(2));
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("auth-api down"));

        var list = (await ListAsync()).ShouldNotBeNull();

        list.Items.ShouldHaveSingleItem().TeacherName.ShouldBeNull();
    }

    // ---------------------------------------------------------------- gate / IDOR

    [Fact]
    public async Task Other_parents_child_and_pending_link_get_null_without_audit()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-1), Now.AddDays(2));
        var instance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1));

        (await ListAsync(parentUser: OtherParentUser)).ShouldBeNull();
        (await ListAsync(parentUser: PendingParentUser)).ShouldBeNull();
        (await ResultAsync(instance, OtherParentUser)).ShouldBe(ParentTestResultLookup.NoAccess);
        (await ResultAsync(instance, PendingParentUser)).ShouldBe(ParentTestResultLookup.NoAccess);
        // Diğer veli kendi çocuğunun id'siyle bizim öğrencinin oturumunu isteyemez.
        (await ResultAsync(instance, OtherParentUser, _otherStudentId)).Result.ShouldBeNull();

        (await AuditCountAsync(ParentAccessEndpoints.ChildAssignments)).ShouldBe(0);
        (await AuditCountAsync(ParentAccessEndpoints.ChildTestResult)).ShouldBe(1); // yalnızca kendi çocuğuna erişen diğer veli
    }

    [Theory]
    [InlineData(ParentStudentLinkStatus.Pending)]
    [InlineData(ParentStudentLinkStatus.Revoked)]
    public async Task Non_active_link_is_404(ParentStudentLinkStatus status)
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        var instance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1));
        await using (var ctx = _db.NewContext())
        {
            var link = await ctx.ParentStudentLinks.SingleAsync(l => l.ParentId == _parentId);
            link.Status = status;
            if (status == ParentStudentLinkStatus.Revoked)
                link.RevokedAt = Now.AddHours(-1);
            await ctx.SaveChangesAsync();
        }

        (await ListAsync()).ShouldBeNull();
        (await ResultAsync(instance)).ChildAccessible.ShouldBeFalse();
    }

    [Fact]
    public async Task Gate_failure_runs_no_data_query_and_no_audit()
    {
        await SeedAsync();
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((ParentChildAccessGrant?)null);
        var audit = Substitute.For<IParentAccessAuditLog>();
        var counter = new CommandCounter();

        await using var ctx = _db.NewContext(counter);
        var service = Service(ctx, access, audit);

        (await service.GetAssignmentsAsync(ParentUser, _studentId, null, 1)).ShouldBeNull();
        (await service.GetTestResultAsync(ParentUser, _studentId, 1)).ShouldBe(ParentTestResultLookup.NoAccess);

        counter.Count.ShouldBe(0);
        await audit.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default!, default);
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task Audit_is_written_before_any_data_query()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-1), Now.AddDays(2));
        var instance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1));
        await QuestionAsync(instance, ws, Pick.Correct);

        var grant = new ParentChildAccessGrant(1, _parentId, _studentId, StudentUser, _gradeId, _schoolId);
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(ParentUser, _studentId, Arg.Any<CancellationToken>()).Returns(grant);
        var audit = Substitute.For<IParentAccessAuditLog>();
        var counter = new CommandCounter { BeforeEach = () => audit.ReceivedCalls().Count() };

        await using var ctx = _db.NewContext(counter);
        var service = Service(ctx, access, audit);

        (await service.GetAssignmentsAsync(ParentUser, _studentId, null, 1)).ShouldNotBeNull();
        counter.Count.ShouldBeGreaterThan(0);
        counter.AuditCallsSeen.ShouldAllBe(n => n == 1);
        await audit.Received(1).RecordAsync(_parentId, _studentId, ParentAccessEndpoints.ChildAssignments, null, Arg.Any<CancellationToken>());

        audit.ClearReceivedCalls();
        counter.Reset();
        (await service.GetTestResultAsync(ParentUser, _studentId, instance)).Result.ShouldNotBeNull();
        // Test sonucu: kapsam çözümü (id/durum) audit'ten önce olabilir (404'te kaynaksız audit için); sonuç İÇERİĞİ
        // (cevap satırları, soru/konu) audit'ten SONRA okunur.
        var content = counter.Seen.Where(c => c.Sql.Contains("TestInstanceQuestions") || c.Sql.Contains("TestQuestions")).ToList();
        content.ShouldNotBeEmpty();
        content.ShouldAllBe(c => c.AuditCalls == 1);
        await audit.Received(1).RecordAsync(_parentId, _studentId, ParentAccessEndpoints.ChildTestResult, instance, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Successful_reads_are_audited_once_per_bucket_with_their_endpoint()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-1), Now.AddDays(2));
        var instance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1));
        var ws2 = await WorksheetAsync();
        await AssignAsync(ws2, Now.AddDays(-1), Now.AddDays(2));
        var instance2 = await InstanceAsync(ws2, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1));

        await ListAsync();
        await ListAsync(ParentAssignmentBucket.Completed);
        await ResultAsync(instance);
        await ResultAsync(instance);
        await ResultAsync(instance2); // farklı kaynak → aynı kovada ayrı satır
        await ResultAsync(999_999);   // 404 → kaynak null
        await ResultAsync(888_888);   // başka 404 → aynı (kaynaksız) satıra tekilleşir

        (await AuditCountAsync(ParentAccessEndpoints.ChildAssignments)).ShouldBe(1);
        (await AuditCountAsync(ParentAccessEndpoints.ChildTestResult)).ShouldBe(3);
        await using var ctx = _db.NewContext();
        ctx.ParentAccessAudits.Where(a => a.Endpoint == ParentAccessEndpoints.ChildTestResult)
            .Select(a => a.ResourceId).ToList().ShouldBe(new int?[] { instance, instance2, null }, ignoreOrder: true);
        ctx.ParentAccessAudits.Single(a => a.Endpoint == ParentAccessEndpoints.ChildAssignments).ResourceId.ShouldBeNull();
    }

    // ---------------------------------------------------------------- test result

    [Fact]
    public async Task Test_result_summary_with_topic_breakdown()
    {
        await SeedAsync();
        var ws = await WorksheetAsync("Kesirler testi");
        await AssignAsync(ws, Now.AddDays(-2), Now.AddDays(2));
        var started = Now.AddMinutes(-50); // 08:10:00
        var instance = await InstanceAsync(ws, started, WorksheetInstanceStatus.Completed, started.AddMinutes(32).AddSeconds(5));
        await QuestionAsync(instance, ws, Pick.Correct);
        await QuestionAsync(instance, ws, Pick.Wrong);
        await QuestionAsync(instance, ws, Pick.Correct);
        await QuestionAsync(instance, ws, Pick.Correct, "Ondalık");
        await QuestionAsync(instance, ws, Pick.Blank, "Ondalık");
        await QuestionAsync(instance, ws, Pick.Wrong, null);

        var lookup = await ResultAsync(instance);

        lookup.ChildAccessible.ShouldBeTrue();
        var result = lookup.Result.ShouldNotBeNull();
        result.StudentId.ShouldBe(_studentId);
        result.TestInstanceId.ShouldBe(instance);
        result.WorksheetId.ShouldBe(ws);
        result.Title.ShouldBe("Kesirler testi");
        result.Subject.ShouldBe("Matematik");
        result.Outcome.ShouldBe(ParentTestOutcomes.Completed);
        // Saate kesilir (review); süre kesin değerden.
        result.StartedAt.ShouldBe(new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc));
        result.FinishedAt.ShouldBe(new DateTime(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc));
        result.StartedAt.Kind.ShouldBe(DateTimeKind.Utc);
        result.Score.CorrectCount.ShouldBe(3);
        result.Score.WrongCount.ShouldBe(2);
        result.Score.BlankCount.ShouldBe(1);
        result.Score.TotalCount.ShouldBe(6);
        result.Score.ScorePercent.ShouldBe(50);
        result.Score.DurationSeconds.ShouldBe(32 * 60); // 32 dk 5 sn → dakikaya aşağı yuvarlanır

        result.Topics.Count.ShouldBe(3);
        var kesirler = result.Topics[0];
        kesirler.Name.ShouldBe("Kesirler");
        kesirler.TopicId.ShouldNotBeNull();
        (kesirler.CorrectCount, kesirler.WrongCount, kesirler.BlankCount, kesirler.TotalCount).ShouldBe((2, 1, 0, 3));
        var ondalik = result.Topics[1];
        ondalik.Name.ShouldBe("Ondalık");
        (ondalik.CorrectCount, ondalik.WrongCount, ondalik.BlankCount, ondalik.TotalCount).ShouldBe((1, 0, 1, 2));
        var unclassified = result.Topics[2];
        unclassified.TopicId.ShouldBeNull();
        unclassified.Name.ShouldNotBeNullOrWhiteSpace();
        (unclassified.CorrectCount, unclassified.WrongCount, unclassified.TotalCount).ShouldBe((0, 1, 1));
    }

    [Fact]
    public async Task Timed_out_session_has_a_result_but_in_progress_does_not()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-2), Now.AddDays(2));
        var expired = await InstanceAsync(ws, Now.AddDays(-1), WorksheetInstanceStatus.Expired, Now.AddDays(-1).AddMinutes(40));
        await QuestionAsync(expired, ws, Pick.Correct);
        var ws2 = await WorksheetAsync();
        await AssignAsync(ws2, Now.AddDays(-2), Now.AddDays(2));
        var running = await InstanceAsync(ws2, Now.AddMinutes(-5), WorksheetInstanceStatus.Started);
        await QuestionAsync(running, ws2, Pick.Correct);

        (await ResultAsync(expired)).Result!.Outcome.ShouldBe(ParentTestOutcomes.TimedOut);
        var runningLookup = await ResultAsync(running);
        runningLookup.ChildAccessible.ShouldBeTrue();
        runningLookup.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Legacy_started_with_end_time_is_completed_bucket_but_has_no_result()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-2), Now.AddDays(2));
        var legacy = await InstanceAsync(ws, Now.AddHours(-3), WorksheetInstanceStatus.Started, Now.AddHours(-2));

        var row = (await ListAsync())!.Items.ShouldHaveSingleItem();
        row.Status.ShouldBe(ParentAssignmentStatuses.Completed);
        row.TestInstanceId.ShouldBeNull();
        row.Result.ShouldBeNull();
        (await ResultAsync(legacy)).Result.ShouldBeNull();
    }

    [Fact]
    public async Task Not_found_results_are_audited_once_per_bucket_without_resource_id()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        var othersInstance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1), _otherStudentId);

        foreach (var id in new[] { othersInstance, 999_999, 0, othersInstance, 123 })
            (await ResultAsync(id)).Result.ShouldBeNull();

        await using var ctx = _db.NewContext();
        var row = ctx.ParentAccessAudits.Where(a => a.Endpoint == ParentAccessEndpoints.ChildTestResult).ToList().ShouldHaveSingleItem();
        row.ResourceId.ShouldBeNull();
        row.ParentId.ShouldBe(_parentId);
        row.StudentId.ShouldBe(_studentId);
    }

    [Fact]
    public async Task Instance_solved_before_the_assignment_opens_once_assigned()
    {
        // #367: atamadan önce kendi başına çözülen test, atama var olduğunda listede tamamlanmış görünür ve sonucu açılır.
        await SeedAsync();
        var ws = await WorksheetAsync();
        var instance = await InstanceAsync(ws, Now.AddDays(-5), WorksheetInstanceStatus.Completed, Now.AddDays(-5).AddMinutes(30));
        (await ResultAsync(instance)).Result.ShouldBeNull(); // henüz atama yok

        await AssignAsync(ws, Now.AddDays(-1), Now.AddDays(3));

        var row = (await ListAsync())!.Items.ShouldHaveSingleItem();
        row.Status.ShouldBe(ParentAssignmentStatuses.Completed);
        row.TestInstanceId.ShouldBe(instance);
        (await ResultAsync(instance)).Result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Test_result_is_limited_to_the_listed_scope()
    {
        await SeedAsync();
        // Kendi başına çözülen (atanmamış) test.
        var selfPractice = await WorksheetAsync();
        var selfInstance = await InstanceAsync(selfPractice, Now.AddHours(-3), WorksheetInstanceStatus.Completed, Now.AddHours(-2));
        // 30 gün penceresinin dışında kalan eski atama.
        var old = await WorksheetAsync();
        await AssignAsync(old, Now.AddDays(-60), Now.AddDays(-40));
        var oldInstance = await InstanceAsync(old, Now.AddDays(-50), WorksheetInstanceStatus.Completed, Now.AddDays(-50).AddMinutes(20));
        // Emekliye ayrılan (soft-delete) worksheet.
        var retired = await WorksheetAsync();
        await AssignAsync(retired, Now.AddDays(-2), Now.AddDays(2));
        var retiredInstance = await InstanceAsync(retired, Now.AddHours(-3), WorksheetInstanceStatus.Completed, Now.AddHours(-2));
        (await ResultAsync(retiredInstance)).Result.ShouldNotBeNull(); // emekliye ayrılmadan önce açılıyordu
        await using (var ctx = _db.NewContext())
        {
            (await ctx.Worksheets.SingleAsync(w => w.Id == retired)).IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        foreach (var id in new[] { selfInstance, oldInstance, retiredInstance })
        {
            var lookup = await ResultAsync(id);
            lookup.ChildAccessible.ShouldBeTrue();
            lookup.Result.ShouldBeNull();
        }

        (await ListAsync())!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Not_found_results_feed_the_probe_monitor()
    {
        await SeedAsync();
        var monitor = Substitute.For<IParentTestResultProbeMonitor>();
        await using var ctx = _db.NewContext();
        var service = new ParentAssignmentService(ctx, new ParentChildAccess(ctx), new ParentAccessAuditLog(ctx, _time), _authApi,
            time: _time, probeMonitor: monitor);

        (await service.GetTestResultAsync(ParentUser, _studentId, 999_999)).Result.ShouldBeNull();
        // Kapıda düşen istek sayılmaz.
        (await service.GetTestResultAsync(OtherParentUser, _studentId, 999_999)).ChildAccessible.ShouldBeFalse();

        monitor.Received(1).RecordNotFound(_parentId, _studentId);
        monitor.ReceivedCalls().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Other_childs_instance_missing_and_deleted_instances_are_404()
    {
        await SeedAsync();
        var ws = await WorksheetAsync();
        await AssignAsync(ws, Now.AddDays(-2), Now.AddDays(2));
        var othersInstance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1), _otherStudentId);
        var deleted = await InstanceAsync(ws, Now.AddHours(-3), WorksheetInstanceStatus.Completed, Now.AddHours(-2));
        await using (var ctx = _db.NewContext())
        {
            (await ctx.TestInstances.SingleAsync(i => i.Id == deleted)).IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        foreach (var id in new[] { othersInstance, deleted, 999_999, 0, -1 })
        {
            var lookup = await ResultAsync(id);
            lookup.ChildAccessible.ShouldBeTrue();
            lookup.Result.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Parent_result_numbers_match_the_student_detail_screen()
    {
        // M1 (review): aynı oturum öğrencinin kendi detay ekranından ve veli özetinden puanlanınca sayılar AYNI olmalı.
        await SeedAsync();
        var ws = await WorksheetAsync("Kesirler testi");
        await AssignAsync(ws, Now.AddDays(-2), Now.AddDays(2));
        var started = Now.AddMinutes(-47).AddSeconds(-13);
        var instance = await InstanceAsync(ws, started, WorksheetInstanceStatus.Completed, Now.AddMinutes(-3));
        await QuestionAsync(instance, ws, Pick.Correct);
        await QuestionAsync(instance, ws, Pick.Wrong);
        await QuestionAsync(instance, ws, Pick.Blank);
        await QuestionAsync(instance, ws, Pick.Correct, "Ondalık");
        await QuestionAsync(instance, ws, Pick.Wrong, "Ondalık");
        await QuestionAsync(instance, ws, Pick.Correct, null);
        await QuestionAsync(instance, ws, Pick.Blank, "Silinecek konu");
        await using (var ctx = _db.NewContext())
        {
            // Konusu silinmiş soru: iki ekran da aynı grup anahtarını kullanmalı.
            (await ctx.Topics.SingleAsync(t => t.Name == "Silinecek konu")).IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        WorksheetCompletedResultDto student;
        await using (var ctx = _db.NewContext())
        {
            var detail = await new ExamApp.Api.Services.Worksheets.WorksheetDetailService(ctx, _authApi)
                .GetWorksheetDetailAsync(ws, "Student", _studentId, StudentUser);
            student = detail.ShouldNotBeNull().CompletedResult.ShouldNotBeNull();
        }

        var parent = (await ResultAsync(instance)).Result.ShouldNotBeNull();

        parent.Score.ScorePercent.ShouldBe(student.ScorePercent);
        parent.Score.CorrectCount.ShouldBe(student.CorrectCount);
        parent.Score.WrongCount.ShouldBe(student.WrongCount);
        parent.Score.BlankCount.ShouldBe(student.EmptyCount);
        // Veli süresi dakikaya aşağı yuvarlanır (review); öğrencininki kesin.
        student.DurationSeconds.ShouldBe(44 * 60 + 13);
        parent.Score.DurationSeconds.ShouldBe(44 * 60);
        parent.Score.TotalCount.ShouldBe(7);
        parent.Topics.Select(t => (t.TopicId, t.Name, t.CorrectCount, t.TotalCount)).OrderBy(t => t.Name)
            .ShouldBe(student.TopicSuccess.Select(t => (t.TopicId, t.Name, t.CorrectCount, t.TotalCount)).OrderBy(t => t.Name));
    }

    // ---------------------------------------------------------------- DTO contract

    [Fact]
    public void Dto_field_lists_are_fixed_and_carry_no_question_or_answer_content()
    {
        static string[] Props<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Props<ParentChildAssignmentListDto>().ShouldBe(new[]
            { "Counts", "Items", "Page", "PageSize", "Status", "StudentId", "TotalCount" });
        Props<ParentChildAssignmentItemDto>().ShouldBe(new[]
            { "Deadline", "Result", "StartAt", "Status", "Subject", "TeacherName", "TestInstanceId", "Title", "WorksheetId" });
        Props<ParentTestScoreDto>().ShouldBe(new[]
            { "BlankCount", "CorrectCount", "DurationSeconds", "ScorePercent", "TotalCount", "WrongCount" });
        Props<ParentChildTestResultDto>().ShouldBe(new[]
            { "FinishedAt", "Outcome", "Score", "StartedAt", "StudentId", "Subject", "TestInstanceId", "Title", "Topics", "WorksheetId" });
        Props<ParentTestTopicResultDto>().ShouldBe(new[]
            { "BlankCount", "CorrectCount", "Name", "TopicId", "TotalCount", "WrongCount" });
    }

    [Fact]
    public async Task Serialized_responses_contain_no_question_text_images_options_or_contact_data()
    {
        await SeedAsync();
        var ws = await WorksheetAsync("Kesirler testi");
        await AssignAsync(ws, Now.AddDays(-1), Now.AddDays(2));
        var instance = await InstanceAsync(ws, Now.AddHours(-2), WorksheetInstanceStatus.Completed, Now.AddHours(-1));
        await QuestionAsync(instance, ws, Pick.Correct);
        await QuestionAsync(instance, ws, Pick.Wrong);

        var json = JsonSerializer.Serialize(await ListAsync()) + JsonSerializer.Serialize((await ResultAsync(instance)).Result);

        foreach (var secret in new[] { "GIZLI", "gizli", "secret@x.com", "Answer", "Selected", "Correct\":", "Image", "Description" })
            json.ShouldNotContain(secret);
        json.ShouldContain("Kesirler testi");
    }

    /// <summary>Çalıştırılan SQL komutlarını sayar; her komuttan önce <see cref="BeforeEach"/>'in değerini kaydeder.</summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public Func<int>? BeforeEach { get; init; }
        public List<int> AuditCallsSeen { get; } = new();
        public List<(string Sql, int AuditCalls)> Seen { get; } = new();

        public void Reset()
        {
            Count = 0;
            AuditCallsSeen.Clear();
            Seen.Clear();
        }

        private void Hit(DbCommand command)
        {
            Count++;
            if (BeforeEach != null)
            {
                var calls = BeforeEach();
                AuditCallsSeen.Add(calls);
                Seen.Add((command.CommandText, calls));
            }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Hit(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Hit(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Hit(command);
            return ValueTask.FromResult(result);
        }
    }
}
