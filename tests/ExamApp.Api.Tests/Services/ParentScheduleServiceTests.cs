using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #422 (epic #407 V4): veli "Program". Kapı → audit → veri; IDOR / Active olmayan bağlantı → null (404); aralık
/// doğrulaması (en fazla 31 gün, iki uç birlikte, yyyy-MM-dd); planlar yerel GÜN olarak (saat/hatırlatma ayarı yok), iptal
/// edilenler hariç; randevular öğretmen adı + saat + durum — görüşme bağlantısı, ücret, ret gerekçesi YOK.
/// </summary>
public class ParentScheduleServiceTests : IDisposable
{
    private const int ParentUser = 46001;
    private const int OtherParentUser = 46002;
    private const int PendingParentUser = 46003;
    private const int StudentUser = 46101;
    private const int OtherStudentUser = 46102;
    private const int TeacherUser = 46201;

    // Çarşamba 2026-10-07 12:00 Istanbul → bu hafta 5–11 Ekim.
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private int _studentId, _otherStudentId, _parentId, _teacherId, _seq;

    public ParentScheduleServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<UserLookupResultDto>)ci.Arg<IEnumerable<int>>()
                .Where(id => id == TeacherUser)
                .Select(id => new UserLookupResultDto { Id = id, FullName = "Zeynep Öğretmen", Email = "secret-teacher@x.com" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private LocalDayCalendar Calendar => new(LocalDayCalendar.DefaultTimeZoneId, _time);

    private async Task SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Matematik" };
        var grade = new Grade { Name = "7. Sınıf" };
        ctx.AddRange(subject, grade);
        var student = new Student { UserId = StudentUser, StudentNumber = "s1" };
        var other = new Student { UserId = OtherStudentUser, StudentNumber = "s2" };
        var teacher = new Teacher
        {
            UserId = TeacherUser, IsIndependentTutor = true, HourlyRate = 987.65m, AccountApprovedAt = Now.AddDays(-90)
        };
        var parent = new Parent { UserId = ParentUser };
        var otherParent = new Parent { UserId = OtherParentUser };
        var pendingParent = new Parent { UserId = PendingParentUser };
        ctx.AddRange(student, other, teacher, parent, otherParent, pendingParent);
        await ctx.SaveChangesAsync();

        ctx.ParentStudentLinks.AddRange(
            new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated,
                ParentId = parent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Active,
                CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
            },
            new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated,
                ParentId = otherParent.Id, StudentId = other.Id, Status = ParentStudentLinkStatus.Active,
                CreatedAt = Now.AddDays(-2), ActivatedAt = Now.AddDays(-2)
            },
            new ParentStudentLink
            {
                Origin = ParentStudentLinkOrigin.ParentCreated,
                ParentId = pendingParent.Id, StudentId = student.Id, Status = ParentStudentLinkStatus.Pending, CreatedAt = Now.AddHours(-2)
            });
        await ctx.SaveChangesAsync();

        (_studentId, _otherStudentId, _parentId, _teacherId) = (student.Id, other.Id, parent.Id, teacher.Id);

        // ---- planlar
        var withSubject = new Worksheet { Name = "Kesirler", Description = "GIZLI-ACIKLAMA", GradeId = grade.Id, SubjectId = subject.Id };
        var plain = new Worksheet { Name = "Oran Orantı", Description = "", GradeId = grade.Id };
        var cancelled = new Worksheet { Name = "İptal edilen", Description = "", GradeId = grade.Id };
        var earlier = new Worksheet { Name = "Önceki hafta", Description = "", GradeId = grade.Id };
        ctx.AddRange(withSubject, plain, cancelled, earlier);
        await ctx.SaveChangesAsync();
        ctx.WorksheetReminders.AddRange(
            Reminder(withSubject.Id, student.Id, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc), WorksheetReminderStatus.Pending),
            // 22:30Z = Istanbul 01:30 → planlanan YEREL gün 8 Ekim.
            Reminder(plain.Id, student.Id, new DateTime(2026, 10, 7, 22, 30, 0, DateTimeKind.Utc), WorksheetReminderStatus.Sent),
            Reminder(cancelled.Id, student.Id, new DateTime(2026, 10, 9, 10, 0, 0, DateTimeKind.Utc), WorksheetReminderStatus.Cancelled),
            // Aralık dışı: yerel Pazartesi 00:00'dan (UTC Pazar 21:00) önce.
            Reminder(earlier.Id, student.Id, new DateTime(2026, 10, 4, 20, 59, 0, DateTimeKind.Utc), WorksheetReminderStatus.Pending),
            Reminder(withSubject.Id, other.Id, new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc), WorksheetReminderStatus.Pending));
        await ctx.SaveChangesAsync();

        // ---- randevular
        await BookingAsync(ctx, student.Id, new DateOnly(2026, 10, 6), new TimeOnly(14, 0), new TimeOnly(15, 0), BookingStatus.Approved);
        await BookingAsync(ctx, student.Id, new DateOnly(2026, 10, 8), new TimeOnly(9, 0), new TimeOnly(10, 0), BookingStatus.Pending);
        await BookingAsync(ctx, student.Id, new DateOnly(2026, 10, 9), new TimeOnly(9, 0), new TimeOnly(10, 0), BookingStatus.Rejected,
            "GIZLI-RET-GEREKCESI");
        await BookingAsync(ctx, student.Id, new DateOnly(2026, 10, 20), new TimeOnly(9, 0), new TimeOnly(10, 0), BookingStatus.Approved);
        await BookingAsync(ctx, other.Id, new DateOnly(2026, 10, 6), new TimeOnly(16, 0), new TimeOnly(17, 0), BookingStatus.Approved);
    }

    private static WorksheetReminder Reminder(int worksheetId, int studentId, DateTime at, WorksheetReminderStatus status) => new()
    {
        WorksheetId = worksheetId, StudentId = studentId, ScheduledFor = at, Status = status, RemindBeforeMinutes = 4321,
        HangfireJobId = "GIZLI-JOB", StudentKeycloakId = "GIZLI-KC"
    };

    private async Task BookingAsync(AppDbContext ctx, int studentId, DateOnly date, TimeOnly start, TimeOnly end, BookingStatus status,
        string? rejection = null)
    {
        var slot = new TeacherAvailabilitySlot { TeacherId = _teacherId, Date = date, StartTime = start, EndTime = end, CreatedAt = Now.AddDays(-++_seq) };
        ctx.Add(slot);
        await ctx.SaveChangesAsync();
        ctx.Bookings.Add(new Booking
        {
            TeacherId = _teacherId, StudentId = studentId, AvailabilitySlotId = slot.Id, Status = status, CreatedAt = Now.AddDays(-3),
            RejectionReason = rejection
        });
        await ctx.SaveChangesAsync();
    }

    private ParentScheduleService Service(AppDbContext ctx, IParentChildAccess? access = null, IParentAccessAuditLog? audit = null)
        => new(ctx, access ?? new ParentChildAccess(ctx), audit ?? new ParentAccessAuditLog(ctx, _time), _authApi, Calendar);

    private async Task<ParentChildScheduleDto?> ScheduleAsync(int parentUser = ParentUser, DateOnly? from = null, DateOnly? to = null,
        int? studentId = null)
    {
        await using var ctx = _db.NewContext();
        var service = Service(ctx);
        service.TryResolveRange(null, null, out var week).ShouldBeTrue();
        return await service.GetScheduleAsync(parentUser, studentId ?? _studentId, from ?? week.From, to ?? week.To);
    }

    [Fact]
    public async Task Current_week_is_local_monday_to_sunday()
    {
        await using var ctx = _db.NewContext();
        Service(ctx).TryResolveRange(null, null, out var week).ShouldBeTrue();
        week.ShouldBe(new ParentScheduleRange.Range(Monday, Monday.AddDays(6)));
    }

    [Fact]
    public async Task Week_lists_plans_by_local_day_and_pending_or_approved_lessons_without_rejected_ones()
    {
        await SeedAsync();

        var dto = (await ScheduleAsync()).ShouldNotBeNull();

        dto.StudentId.ShouldBe(_studentId);
        (dto.From, dto.To).ShouldBe((Monday, Monday.AddDays(6)));
        dto.Plans.Select(p => (p.Title, p.Subject, p.PlannedOn)).ShouldBe(new[]
        {
            ("Kesirler", (string?)"Matematik", new DateOnly(2026, 10, 6)),
            ("Oran Orantı", (string?)null, new DateOnly(2026, 10, 8))
        });
        // 9 Ekim'deki reddedilen talep veliye gösterilmez.
        dto.Lessons.Select(l => (l.TeacherName, l.StartsOn, l.StartAt, l.EndAt, l.Status)).ShouldBe(new[]
        {
            ((string?)"Zeynep Öğretmen", new DateOnly(2026, 10, 6), new DateTime(2026, 10, 6, 14, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 6, 15, 0, 0, DateTimeKind.Utc), ParentLessonStatuses.Approved),
            ("Zeynep Öğretmen", new DateOnly(2026, 10, 8), new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc), ParentLessonStatuses.Pending)
        });
        dto.Lessons.ShouldNotContain(l => l.Status == "rejected");
    }

    [Fact]
    public async Task Lessons_are_placed_on_their_istanbul_day_and_one_overlapping_the_range_start_on_the_first_day()
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // 21:30Z = Istanbul 00:30 ertesi gün → yerel gün 7 Ekim (UTC günü 6 Ekim).
            await BookingAsync(ctx, _studentId, new DateOnly(2026, 10, 6), new TimeOnly(21, 30), new TimeOnly(22, 30), BookingStatus.Approved);
            // Yerel hafta başı 2026-10-04 21:00Z: 20:30–21:30Z randevu (yerel Pazar 23:30) aralığa taşar → Pazartesi'de.
            await BookingAsync(ctx, _studentId, new DateOnly(2026, 10, 4), new TimeOnly(20, 30), new TimeOnly(21, 30), BookingStatus.Pending);
            // Tamamen önceki gün (yerel Pazar 22:00–23:00) → aralık dışı.
            await BookingAsync(ctx, _studentId, new DateOnly(2026, 10, 4), new TimeOnly(19, 0), new TimeOnly(20, 0), BookingStatus.Approved);
        }

        var dto = (await ScheduleAsync()).ShouldNotBeNull();
        dto.Lessons.Select(l => (l.StartsOn, l.StartAt)).ShouldBe(new[]
        {
            (new DateOnly(2026, 10, 5), new DateTime(2026, 10, 4, 20, 30, 0, DateTimeKind.Utc)),
            (new DateOnly(2026, 10, 6), new DateTime(2026, 10, 6, 14, 0, 0, DateTimeKind.Utc)),
            (new DateOnly(2026, 10, 7), new DateTime(2026, 10, 6, 21, 30, 0, DateTimeKind.Utc)),
            (new DateOnly(2026, 10, 8), new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc))
        });
    }

    [Fact]
    public async Task Longer_range_includes_later_lessons()
    {
        await SeedAsync();
        var dto = (await ScheduleAsync(from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 31))).ShouldNotBeNull();
        dto.Lessons.Count.ShouldBe(3); // reddedilen hariç
        dto.Plans.Count.ShouldBe(3); // 4 Ekim 23:59 yerel planı da artık aralıkta
    }

    [Fact]
    public async Task Schedule_dto_has_no_meeting_link_price_notes_or_reminder_internals()
    {
        await SeedAsync();
        var json = JsonSerializer.Serialize(await ScheduleAsync());

        foreach (var secret in new[]
                 {
                     "GIZLI", "987", "4321", "secret-teacher", "http", "jitsi", "Jitsi", "meet", "Url", "Link", "Price", "Rate",
                     "Reason", "Note", "BookingId", "Keycloak", "RemindBefore", OtherStudentUser.ToString()
                 })
            json.ShouldNotContain(secret);
        json.ShouldContain("Zeynep"); // öğretmen adı var (JSON non-ASCII karakterleri kaçışlar)
    }

    [Fact]
    public async Task Teacher_name_lookup_failure_keeps_lessons_with_null_names()
    {
        await SeedAsync();
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>()).Throws(new HttpRequestException("down"));

        var dto = (await ScheduleAsync()).ShouldNotBeNull();
        dto.Lessons.Count.ShouldBe(2);
        dto.Lessons.ShouldAllBe(l => l.TeacherName == null);
    }

    [Fact]
    public async Task Other_parents_child_pending_link_and_unknown_student_get_null_without_audit()
    {
        await SeedAsync();

        (await ScheduleAsync(OtherParentUser)).ShouldBeNull();
        (await ScheduleAsync(PendingParentUser)).ShouldBeNull();
        (await ScheduleAsync(studentId: 999_999)).ShouldBeNull();
        // Diğer veli kendi çocuğunu görür, bizimkini değil.
        (await ScheduleAsync(OtherParentUser, studentId: _otherStudentId)).ShouldNotBeNull().Lessons.Count.ShouldBe(1);

        await using var ctx = _db.NewContext();
        (await ctx.ParentAccessAudits.CountAsync(a => a.StudentId == _studentId)).ShouldBe(0);
    }

    [Theory]
    [InlineData(ParentStudentLinkStatus.Pending)]
    [InlineData(ParentStudentLinkStatus.Revoked)]
    public async Task Non_active_link_is_null(ParentStudentLinkStatus status)
    {
        await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var link = await ctx.ParentStudentLinks.SingleAsync(l => l.ParentId == _parentId);
            link.Status = status;
            if (status == ParentStudentLinkStatus.Revoked)
                link.RevokedAt = Now.AddHours(-1);
            await ctx.SaveChangesAsync();
        }

        (await ScheduleAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task Gate_failure_runs_no_data_query_and_no_audit()
    {
        await SeedAsync();
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((ParentChildAccessGrant?)null);
        var audit = Substitute.For<IParentAccessAuditLog>();
        var counter = new ParentQueryCounter();

        await using var ctx = _db.NewContext(counter);
        (await Service(ctx, access, audit).GetScheduleAsync(ParentUser, _studentId, Monday, Monday.AddDays(6))).ShouldBeNull();

        counter.Count.ShouldBe(0);
        await audit.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default!, default);
        _authApi.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Audit_is_written_before_any_data_query_and_once_per_bucket()
    {
        await SeedAsync();
        var grant = new ParentChildAccessGrant(1, _parentId, _studentId, StudentUser, null, null);
        var access = Substitute.For<IParentChildAccess>();
        access.EnsureActiveChildAsync(ParentUser, _studentId, Arg.Any<CancellationToken>()).Returns(grant);
        var audit = Substitute.For<IParentAccessAuditLog>();
        var counter = new ParentQueryCounter { BeforeEach = () => audit.ReceivedCalls().Count() };

        await using (var ctx = _db.NewContext(counter))
        {
            (await Service(ctx, access, audit).GetScheduleAsync(ParentUser, _studentId, Monday, Monday.AddDays(6))).ShouldNotBeNull();
        }

        counter.Count.ShouldBeGreaterThan(0);
        counter.AuditCallsSeen.ShouldAllBe(n => n == 1);
        await audit.Received(1).RecordAsync(_parentId, _studentId, ParentAccessEndpoints.ChildSchedule, null, Arg.Any<CancellationToken>());

        await ScheduleAsync();
        await ScheduleAsync(from: Monday.AddDays(-7), to: Monday.AddDays(-1)); // önceki hafta: aynı kova, aynı satır
        await using var check = _db.NewContext();
        (await check.ParentAccessAudits.CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildSchedule)).ShouldBe(1);
    }

    [Theory]
    [InlineData(null, null, true, "2026-10-05", "2026-10-11")] // varsayılan: bu hafta
    [InlineData("2026-10-01", "2026-10-31", true, "2026-10-01", "2026-10-31")] // tam 31 gün
    [InlineData("2026-10-07", "2026-10-07", true, "2026-10-07", "2026-10-07")] // tek gün
    [InlineData(" 2026-10-01 ", "2026-10-02", true, "2026-10-01", "2026-10-02")]
    [InlineData("2026-10-01", "2026-11-01", false, null, null)] // 32 gün
    [InlineData("2026-10-10", "2026-10-09", false, null, null)] // to < from
    [InlineData("2026-10-01", null, false, null, null)] // tek uç
    [InlineData(null, "2026-10-01", false, null, null)]
    [InlineData("2026/10/01", "2026-10-02", false, null, null)]
    [InlineData("2026-10-1", "2026-10-02", false, null, null)]
    [InlineData("2026-02-30", "2026-03-02", false, null, null)]
    [InlineData("2026-10-01T00:00:00", "2026-10-02", false, null, null)]
    // Bugün 2026-10-07: en fazla bir yıl geri / ileri (uç yıllar 500 değil 400).
    [InlineData("2025-10-07", "2025-10-10", true, "2025-10-07", "2025-10-10")]
    [InlineData("2025-10-06", "2025-10-10", false, null, null)]
    [InlineData("2027-10-01", "2027-10-07", true, "2027-10-01", "2027-10-07")]
    [InlineData("2027-10-02", "2027-10-08", false, null, null)]
    [InlineData("9999-12-01", "9999-12-31", false, null, null)]
    [InlineData("0001-01-01", "0001-01-02", false, null, null)]
    public void Range_validation(string? from, string? to, bool ok, string? expectedFrom, string? expectedTo)
    {
        var result = ParentScheduleRange.TryResolve(from, to, new DateOnly(2026, 10, 7), out var range);

        result.ShouldBe(ok);
        if (ok)
            range.ShouldBe(new ParentScheduleRange.Range(DateOnly.Parse(expectedFrom!), DateOnly.Parse(expectedTo!)));
    }

    [Fact]
    public async Task Service_rejects_an_unvalidated_range_before_touching_anything()
    {
        await SeedAsync();
        var counter = new ParentQueryCounter();
        await using var ctx = _db.NewContext(counter);
        var service = Service(ctx);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.GetScheduleAsync(ParentUser, _studentId, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.GetScheduleAsync(ParentUser, _studentId, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.GetScheduleAsync(ParentUser, _studentId, new DateOnly(9999, 12, 1), new DateOnly(9999, 12, 31)));
        counter.Count.ShouldBe(0);
    }

    [Fact]
    public void Dto_field_lists_are_fixed()
    {
        static string[] Props<T>() => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Props<ParentChildScheduleDto>().ShouldBe(new[] { "From", "Lessons", "Plans", "StudentId", "To" });
        Props<ParentPlanItemDto>().ShouldBe(new[] { "PlannedOn", "Subject", "Title" });
        Props<ParentLessonItemDto>().ShouldBe(new[] { "EndAt", "StartAt", "StartsOn", "Status", "TeacherName" });
    }
}
