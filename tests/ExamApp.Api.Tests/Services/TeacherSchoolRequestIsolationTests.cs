using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 — okul izolasyonu takibi, TeacherService.Save tarafı:
/// (madde 1) okul talebi açıldığında <see cref="TeacherSchoolRequestSubmittedEvent"/> aynı transaction'da outbox'a yazılır;
/// (madde 2) retten sonra bekleme süresi dolmadan yeni okul talebi açılamaz (TooManyRequests);
/// (madde 3) okullu öğretmen bağımsıza geçince okul bağı HEMEN kaldırılır, hesap onayı (#287) korunur.
/// </summary>
public class TeacherSchoolRequestIsolationTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherSchoolRequestIsolationTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyList<UserLookupResultDto>)((IEnumerable<int>)ci[0])
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"Öğretmen {id}" }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private static string SchoolRequestType => OutboxEventRegistry.NameFor<TeacherSchoolRequestSubmittedEvent>();

    private TeacherService NewService(AppDbContext ctx, int cooldownHours = 24) =>
        new(ctx, _authApi, schoolRequestOptions: Options.Create(new TeacherSchoolRequestOptions { SchoolRequestCooldownHours = cooldownHours }));

    private async Task<int> SeedSchoolAsync(string name)
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    private async Task<int> SeedTeacherAsync(Teacher teacher)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        return teacher.Id;
    }

    private async Task<TeacherRegistrationResultDto> SaveAsync(int userId, RegisterTeacherDto dto, int cooldownHours = 24)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx, cooldownHours).Save(userId, dto);
    }

    private async Task<List<TeacherSchoolRequestSubmittedEvent>> SchoolRequestEventsAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.OutboxMessages.AsNoTracking().Where(m => m.Type == SchoolRequestType).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<TeacherSchoolRequestSubmittedEvent>(m.Content)!)
            .ToList();
    }

    private async Task<Teacher> TeacherOfAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Teachers.AsNoTracking().SingleAsync(t => t.UserId == userId);
    }

    // ---- madde 1: okul talebi admin bildirimi (outbox) ----

    [Fact]
    public void New_event_is_registered_for_the_outbox_publisher()
        => OutboxEventRegistry.Resolve("ExamApp.Foundation.Contracts.TeacherSchoolRequestSubmittedEvent")
            .ShouldBe(typeof(TeacherSchoolRequestSubmittedEvent));

    [Fact]
    public async Task New_registration_with_a_school_request_writes_one_school_request_event()
    {
        var school = await SeedSchoolAsync("A Okulu");

        var r = await SaveAsync(700, new RegisterTeacherDto { SchoolId = school });

        r.Success.ShouldBeTrue();
        r.SchoolApprovalPending.ShouldBeTrue();
        var e = (await SchoolRequestEventsAsync()).ShouldHaveSingleItem();
        e.EventId.ShouldNotBe(Guid.Empty);
        e.TeacherId.ShouldBe(r.ObjectId);
        e.UserId.ShouldBe(700);
        e.RequestedSchoolId.ShouldBe(school);
        e.RequestedSchoolName.ShouldBe("A Okulu");
        e.ApplicantName.ShouldBe("Öğretmen 700");
        e.IsNewRegistration.ShouldBeTrue();

        // Bağımsız başvuru event'i YAZILMAZ (onun tüketicisi metni "bağımsız öğretmen başvurusu" diye kurar).
        await using var check = _db.NewContext();
        (await check.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationSubmittedEvent>())).ShouldBe(0);
    }

    [Fact]
    public async Task Idempotent_resubmission_of_the_same_pending_request_writes_no_second_event()
    {
        var school = await SeedSchoolAsync("A Okulu");
        (await SaveAsync(701, new RegisterTeacherDto { SchoolId = school })).Success.ShouldBeTrue();

        var again = await SaveAsync(701, new RegisterTeacherDto { SchoolId = school });

        again.Success.ShouldBeTrue();
        (await SchoolRequestEventsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Registration_without_school_and_independent_registration_write_no_school_request_event()
    {
        (await SaveAsync(702, new RegisterTeacherDto())).Success.ShouldBeTrue();
        var school = await SeedSchoolAsync("A Okulu");
        // Bağımsız kayıtta okul talebi yok sayılır.
        (await SaveAsync(703, new RegisterTeacherDto { SchoolId = school, IsIndependentTutor = true })).Success.ShouldBeTrue();

        (await SchoolRequestEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Reopening_a_request_after_the_cooldown_writes_a_new_event_with_a_new_id()
    {
        var schoolA = await SeedSchoolAsync("A Okulu");
        var schoolB = await SeedSchoolAsync("B Okulu");
        await SeedTeacherAsync(new Teacher
        {
            UserId = 704, RequestedSchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Rejected,
            RejectionReason = "x", LastRejectedAt = DateTime.UtcNow.AddHours(-30), AccountApprovedAt = DateTime.UtcNow.AddDays(-3)
        });

        var r = await SaveAsync(704, new RegisterTeacherDto { SchoolId = schoolB });

        r.Success.ShouldBeTrue();
        var e = (await SchoolRequestEventsAsync()).ShouldHaveSingleItem();
        e.RequestedSchoolId.ShouldBe(schoolB);
        e.IsNewRegistration.ShouldBeFalse();
    }

    [Fact]
    public async Task School_request_event_is_written_when_the_auth_api_name_lookup_fails()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("down"));
        var school = await SeedSchoolAsync("A Okulu");

        (await SaveAsync(705, new RegisterTeacherDto { SchoolId = school })).Success.ShouldBeTrue();

        (await SchoolRequestEventsAsync()).ShouldHaveSingleItem().ApplicantName.ShouldBeNull();
    }

    // ---- madde 2: retten sonra bekleme süresi ----

    [Theory]
    [InlineData(1, true)]    // 1 saat önce reddedildi → 24 saat dolmadı
    [InlineData(23, true)]
    [InlineData(25, false)]  // bekleme bitti
    public async Task Rejected_teacher_cannot_open_a_new_school_request_before_the_cooldown(int rejectedHoursAgo, bool blocked)
    {
        var schoolA = await SeedSchoolAsync("A Okulu");
        var schoolB = await SeedSchoolAsync("B Okulu");
        var rejectedAt = DateTime.UtcNow.AddHours(-rejectedHoursAgo);
        await SeedTeacherAsync(new Teacher
        {
            UserId = 710, RequestedSchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Rejected,
            RejectionReason = "x", LastRejectedAt = rejectedAt
        });

        var r = await SaveAsync(710, new RegisterTeacherDto { SchoolId = schoolB });

        r.Success.ShouldBe(!blocked);
        r.TooManyRequests.ShouldBe(blocked);
        var teacher = await TeacherOfAsync(710);
        if (blocked)
        {
            r.RetryAfterUtc!.Value.ShouldBe(rejectedAt.AddHours(24), TimeSpan.FromSeconds(1));
            r.Message.ShouldContain($"{24 - rejectedHoursAgo} saat");
            teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected); // hiçbir alan değişmedi
            teacher.RequestedSchoolId.ShouldBe(schoolA);
            (await SchoolRequestEventsAsync()).ShouldBeEmpty();
        }
        else
        {
            teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
            teacher.RequestedSchoolId.ShouldBe(schoolB);
        }
    }

    [Fact]
    public async Task Cooldown_is_configurable_and_zero_disables_it()
    {
        var schoolA = await SeedSchoolAsync("A Okulu");
        await SeedTeacherAsync(new Teacher
        {
            UserId = 711, RequestedSchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Rejected,
            RejectionReason = "x", LastRejectedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        var r = await SaveAsync(711, new RegisterTeacherDto { SchoolId = schoolA }, cooldownHours: 0);

        r.Success.ShouldBeTrue();
        r.SchoolApprovalPending.ShouldBeTrue();
    }

    [Fact]
    public async Task Legacy_rejected_row_without_LastRejectedAt_is_not_blocked()
    {
        // Migration AddTeacherLastRejectedAt Rejected satırları doldurur; yine de null satır (ör. elle düzeltilmiş) kilitlenmez.
        var schoolA = await SeedSchoolAsync("A Okulu");
        await SeedTeacherAsync(new Teacher
        {
            UserId = 712, RequestedSchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "x"
        });

        (await SaveAsync(712, new RegisterTeacherDto { SchoolId = schoolA })).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Admin_rejection_records_LastRejectedAt()
    {
        var schoolA = await SeedSchoolAsync("A Okulu");
        var teacherId = await SeedTeacherAsync(new Teacher
        {
            UserId = 713, RequestedSchoolId = schoolA, ApprovalStatus = TeacherApprovalStatus.Pending
        });
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using (var ctx = _db.NewContext())
        {
            var result = await new TeacherApprovalService(ctx, _authApi).RejectAsync(teacherId, "eksik belge", 999, "kc-admin");
            result.Success.ShouldBeTrue();
        }

        (await TeacherOfAsync(713)).LastRejectedAt.ShouldNotBeNull().ShouldBeGreaterThan(before);
    }

    // ---- madde 3: bağımsıza geçişte okul bağı hemen kalkar ----

    [Fact]
    public async Task School_teacher_switching_to_independent_loses_the_school_link_immediately_but_keeps_account_approval()
    {
        var school = await SeedSchoolAsync("A Okulu");
        var approvedAt = DateTime.UtcNow.AddDays(-10);
        await SeedTeacherAsync(new Teacher
        {
            UserId = 720, SchoolId = school, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = approvedAt
        });

        var r = await SaveAsync(720, new RegisterTeacherDto { IsIndependentTutor = true });

        r.Success.ShouldBeTrue();
        r.SchoolId.ShouldBeNull(); // controller bu değeri önbelleğe/Keycloak school_id'ye taşır
        var teacher = await TeacherOfAsync(720);
        teacher.SchoolId.ShouldBeNull();
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.AccountApprovedAt.ShouldNotBeNull().ShouldBe(approvedAt, TimeSpan.FromMilliseconds(1)); // #287 hesap onayı korunur

        await using (var ctx = _db.NewContext())
        {
            // Öğretmen özellikleri açık kalır (#287 guard AccountApprovedAt'e bakar)...
            (await new ApprovedTeacherGuard(ctx).CheckAsync(720)).ShouldBe(TeacherApprovalCheck.Approved);
            // ...ama okul kapsamı yok: tenant çözümü artık okulsuz.
            (await new SchoolContextResolver(ctx).ResolveSchoolIdAsync(new UserProfileDto { Id = 720, Role = "Teacher" }))
                .ShouldBeNull();
        }
    }

    [Fact]
    public async Task Switched_teacher_no_longer_sees_old_school_students_in_the_dashboard()
    {
        var school = await SeedSchoolAsync("A Okulu");
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "7" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;
            ctx.Students.Add(new Student { UserId = 9001, StudentNumber = "1", SchoolId = school, GradeId = gradeId });
            ctx.Teachers.Add(new Teacher { UserId = 721, SchoolId = school, AccountApprovedAt = DateTime.UtcNow });
            ctx.SetCurrentUser(721);
            var ws = new Worksheet { Name = "W", Description = "", GradeId = gradeId };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            // Okul dönemi sınıf ataması — veri silinmez, atamanın SchoolId'si korunur.
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = gradeId, SchoolId = school, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).GetDashboardSummaryAsync(SchoolScope.For(721, school))).TotalUniqueStudents.ShouldBe(1);

        (await SaveAsync(721, new RegisterTeacherDto { IsIndependentTutor = true })).Success.ShouldBeTrue();

        // Bağımsız öğretmenin scope'u artık okulsuz (profil önbelleği register sonrası DB'den yeniden çözülür).
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).GetDashboardSummaryAsync(SchoolScope.For(721, null))).TotalUniqueStudents.ShouldBe(0);
            // Eski okul scope'u (bayat önbellek) ile gelse bile öğretmen kaydıyla uyuşmadığı için dar kapsam.
            (await NewService(ctx).GetDashboardSummaryAsync(SchoolScope.For(721, school))).TotalUniqueStudents.ShouldBe(0);
            (await ctx.WorksheetAssignments.CountAsync(a => a.SchoolId == school)).ShouldBe(1); // veri korunur
        }
    }

    [Fact]
    public async Task Switched_teacher_cannot_rejoin_a_school_through_register()
    {
        var school = await SeedSchoolAsync("A Okulu");
        await SeedTeacherAsync(new Teacher
        {
            UserId = 722, SchoolId = school, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow
        });
        (await SaveAsync(722, new RegisterTeacherDto { IsIndependentTutor = true })).Success.ShouldBeTrue();

        var back = await SaveAsync(722, new RegisterTeacherDto { SchoolId = school });

        back.Success.ShouldBeFalse();
        back.Conflict.ShouldBeTrue();
        (await TeacherOfAsync(722)).SchoolId.ShouldBeNull();
    }
}
