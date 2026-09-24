using ExamApp.Api.Data;
using ExamApp.Api.Migrations;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #287: öğretmen kaydı admin onayına düşer. Hesap onayı (<see cref="Teacher.AccountApprovedAt"/>) mevcut
/// başvuru durumundan (<see cref="Teacher.ApprovalStatus"/>) ayrıdır: her yeni kayıt Pending + null başlar, ilk admin
/// onayı set eder, sonraki geçişler (bağımsızlığa geçiş, ret) temizlemez.
/// </summary>
public class TeacherAccountApprovalTests : IDisposable
{
    private const int AdminUserId = 999;
    private const string AdminSub = "kc-admin-999";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherAccountApprovalTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserLookupResultDto>());
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedSchoolAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Okul" };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    private async Task<TeacherRegistrationResultDto> RegisterAsync(int userId, RegisterTeacherDto dto)
    {
        await using var ctx = _db.NewContext();
        return await new TeacherService(ctx, _authApi).Save(userId, dto);
    }

    private async Task<Teacher> TeacherOfAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Teachers.AsNoTracking().SingleAsync(t => t.UserId == userId);
    }

    private async Task<TeacherApprovalCheck> GuardAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await new ApprovedTeacherGuard(ctx).CheckAsync(userId);
    }

    private async Task<ResponseBaseDto> ApproveAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await new TeacherApprovalService(ctx, _authApi).ApproveAsync(teacherId, AdminUserId, AdminSub);
    }

    private async Task<ResponseBaseDto> RejectAsync(int teacherId, string reason = "Belge eksik")
    {
        await using var ctx = _db.NewContext();
        return await new TeacherApprovalService(ctx, _authApi).RejectAsync(teacherId, reason, AdminUserId, AdminSub);
    }

    private async Task<Teacher> SeedTeacherAsync(Teacher teacher)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        return teacher;
    }

    // ---------------- Kayıt: her yol Pending + AccountApprovedAt null ----------------

    public static TheoryData<string> RegistrationPaths => new() { "noSchool", "schoolRequest", "independent" };

    [Theory]
    [MemberData(nameof(RegistrationPaths))]
    public async Task Every_new_registration_path_starts_pending_without_account_approval(string path)
    {
        var schoolId = await SeedSchoolAsync();
        var dto = path switch
        {
            "noSchool" => new RegisterTeacherDto { IsIndependentTutor = false },
            "schoolRequest" => new RegisterTeacherDto { IsIndependentTutor = false, SchoolId = schoolId },
            _ => new RegisterTeacherDto { IsIndependentTutor = true },
        };

        var result = await RegisterAsync(100, dto);

        result.Success.ShouldBeTrue();
        result.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        result.AccountApproved.ShouldBeFalse();
        var teacher = await TeacherOfAsync(100);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.AccountApprovedAt.ShouldBeNull();
        teacher.SchoolId.ShouldBeNull();
        (await GuardAsync(100)).ShouldBe(TeacherApprovalCheck.NotApproved);
    }

    [Fact]
    public async Task No_school_registration_is_listed_as_a_pending_account_application()
    {
        await RegisterAsync(101, new RegisterTeacherDto { IsIndependentTutor = false });
        var teacherId = (await TeacherOfAsync(101)).Id;

        await using var ctx = _db.NewContext();
        var page = await new TeacherApprovalService(ctx, _authApi)
            .ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 20);

        var item = page.Items.ShouldHaveSingleItem();
        item.TeacherId.ShouldBe(teacherId);
        item.IsIndependentTutor.ShouldBeFalse();
        item.RequestedSchoolId.ShouldBeNull();
        item.RequiresAccountApproval.ShouldBeTrue();
    }

    // ---------------- Admin onayı ----------------

    [Fact]
    public async Task Approving_a_no_school_account_application_sets_account_approval_without_linking_a_school()
    {
        await RegisterAsync(102, new RegisterTeacherDto { IsIndependentTutor = false });
        var teacherId = (await TeacherOfAsync(102)).Id;
        var before = DateTime.UtcNow.AddSeconds(-1);

        (await ApproveAsync(teacherId)).Success.ShouldBeTrue();

        var teacher = await TeacherOfAsync(102);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        teacher.AccountApprovedAt.ShouldNotBeNull();
        teacher.AccountApprovedAt!.Value.ShouldBeGreaterThan(before);
        teacher.SchoolId.ShouldBeNull();
        (await GuardAsync(102)).ShouldBe(TeacherApprovalCheck.Approved);
    }

    [Fact]
    public async Task Approving_a_school_request_links_the_school_and_sets_account_approval()
    {
        var schoolId = await SeedSchoolAsync();
        await RegisterAsync(103, new RegisterTeacherDto { IsIndependentTutor = false, SchoolId = schoolId });

        (await ApproveAsync((await TeacherOfAsync(103)).Id)).Success.ShouldBeTrue();

        var teacher = await TeacherOfAsync(103);
        teacher.SchoolId.ShouldBe(schoolId);
        teacher.RequestedSchoolId.ShouldBeNull();
        teacher.AccountApprovedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Approving_an_independent_application_sets_account_approval()
    {
        await RegisterAsync(104, new RegisterTeacherDto { IsIndependentTutor = true });

        (await ApproveAsync((await TeacherOfAsync(104)).Id)).Success.ShouldBeTrue();

        (await TeacherOfAsync(104)).AccountApprovedAt.ShouldNotBeNull();
        (await GuardAsync(104)).ShouldBe(TeacherApprovalCheck.Approved);
    }

    [Fact]
    public async Task Rejecting_a_first_time_applicant_keeps_account_unapproved()
    {
        await RegisterAsync(105, new RegisterTeacherDto { IsIndependentTutor = false });

        (await RejectAsync((await TeacherOfAsync(105)).Id)).Success.ShouldBeTrue();

        var teacher = await TeacherOfAsync(105);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        teacher.AccountApprovedAt.ShouldBeNull();
        (await GuardAsync(105)).ShouldBe(TeacherApprovalCheck.NotApproved);
        TeacherApprovalState.From(teacher)
            .ShouldBe(new TeacherApprovalState(false, "Rejected", "Belge eksik"));
    }

    [Fact]
    public async Task Approving_a_later_application_does_not_move_the_original_account_approval_time()
    {
        var schoolId = await SeedSchoolAsync();
        var originallyApprovedAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        // Hesabı onaylı, okulsuz eski öğretmen yeni okul talebi açtı (#234) → Pending.
        var seeded = await SeedTeacherAsync(new Teacher
        {
            UserId = 106, RequestedSchoolId = schoolId, ApprovalStatus = TeacherApprovalStatus.Pending,
            AccountApprovedAt = originallyApprovedAt
        });

        (await ApproveAsync(seeded.Id)).Success.ShouldBeTrue();

        var teacher = await TeacherOfAsync(106);
        teacher.SchoolId.ShouldBe(schoolId);
        teacher.AccountApprovedAt.ShouldBe(originallyApprovedAt);
    }

    [Fact]
    public async Task Account_approved_teacher_without_any_request_is_not_an_application()
    {
        // Hesabı onaylı, talebi olmayan okulsuz öğretmen: Pending'e düşmüş olsa bile karar alınamaz (#287 öncesi davranış).
        var seeded = await SeedTeacherAsync(new Teacher
        {
            UserId = 107, ApprovalStatus = TeacherApprovalStatus.Pending, AccountApprovedAt = DateTime.UtcNow
        });

        var result = await ApproveAsync(seeded.Id);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();
    }

    // ---------------- security review L5: rolling deploy artığı (Approved + AccountApprovedAt null) ----------------

    [Fact]
    public async Task Approved_row_without_account_approval_is_a_pending_account_application_and_can_be_approved()
    {
        // Eski kod (deploy sırasında) okulsuz kaydı Approved başlattı; migration sonrası yazıldığı için backfill görmedi.
        var seeded = await SeedTeacherAsync(new Teacher { UserId = 120, ApprovalStatus = TeacherApprovalStatus.Approved });

        await using (var ctx = _db.NewContext())
        {
            var page = await new TeacherApprovalService(ctx, _authApi)
                .ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 20);
            var item = page.Items.ShouldHaveSingleItem();
            item.TeacherId.ShouldBe(seeded.Id);
            item.Status.ShouldBe("Pending");
            item.RequiresAccountApproval.ShouldBeTrue();
            item.DecidedAt.ShouldBeNull();
        }

        (await ApproveAsync(seeded.Id)).Success.ShouldBeTrue();

        var teacher = await TeacherOfAsync(120);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        teacher.AccountApprovedAt.ShouldNotBeNull();
        (await GuardAsync(120)).ShouldBe(TeacherApprovalCheck.Approved);

        // Artık karar verilmiş (hesabı onaylı, talebi yok): tekrar onaylanamaz.
        (await ApproveAsync(seeded.Id)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Approved_row_without_account_approval_can_also_be_rejected()
    {
        var seeded = await SeedTeacherAsync(new Teacher { UserId = 121, ApprovalStatus = TeacherApprovalStatus.Approved });

        (await RejectAsync(seeded.Id)).Success.ShouldBeTrue();

        var teacher = await TeacherOfAsync(121);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        teacher.AccountApprovedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Rejected_first_time_application_stays_final()
    {
        // Rejected kararı kesindir (#187 idempotency) — AccountApprovedAt null olsa bile yeniden onaylanmaz.
        var seeded = await SeedTeacherAsync(new Teacher { UserId = 122, ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "x" });

        (await ApproveAsync(seeded.Id)).Conflict.ShouldBeTrue();
    }

    // ---------------- Okul → bağımsız geçişi hesabı kapatmaz ----------------

    [Fact]
    public async Task School_teacher_switching_to_independent_keeps_teacher_access_while_tutor_application_is_pending()
    {
        var schoolId = await SeedSchoolAsync();
        var approvedAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedTeacherAsync(new Teacher
        {
            UserId = 108, SchoolId = schoolId, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = approvedAt
        });

        var result = await RegisterAsync(108, new RegisterTeacherDto { IsIndependentTutor = true });

        result.Success.ShouldBeTrue();
        result.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        result.AccountApproved.ShouldBeTrue();
        var teacher = await TeacherOfAsync(108);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        teacher.AccountApprovedAt.ShouldBe(approvedAt);
        (await GuardAsync(108)).ShouldBe(TeacherApprovalCheck.Approved);
        TeacherApprovalState.From(teacher).ShouldBe(new TeacherApprovalState(true, "Pending", null));

        // Tutor başvurusu reddedilse de hesap onayı korunur.
        (await RejectAsync(teacher.Id)).Success.ShouldBeTrue();
        (await TeacherOfAsync(108)).AccountApprovedAt.ShouldBe(approvedAt);
        (await GuardAsync(108)).ShouldBe(TeacherApprovalCheck.Approved);
    }

    // ---------------- Guard ----------------

    [Fact]
    public async Task Guard_decides_on_account_approval_not_application_status_and_caches_per_scope()
    {
        await SeedTeacherAsync(new Teacher { UserId = 110, ApprovalStatus = TeacherApprovalStatus.Approved }); // eski veri, backfill'siz
        await SeedTeacherAsync(new Teacher { UserId = 111, ApprovalStatus = TeacherApprovalStatus.Pending, AccountApprovedAt = DateTime.UtcNow });

        (await GuardAsync(110)).ShouldBe(TeacherApprovalCheck.NotApproved);
        (await GuardAsync(111)).ShouldBe(TeacherApprovalCheck.Approved);
        (await GuardAsync(112)).ShouldBe(TeacherApprovalCheck.NoTeacherProfile);

        // İstek (scope) başına önbellek: aynı guard örneği ikinci çağrıda DB'ye gitmez.
        await using var ctx = _db.NewContext();
        var guard = new ApprovedTeacherGuard(ctx);
        (await guard.CheckAsync(110)).ShouldBe(TeacherApprovalCheck.NotApproved);
        await using (var writer = _db.NewContext())
        {
            await writer.Teachers.Where(t => t.UserId == 110)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.AccountApprovedAt, DateTime.UtcNow));
        }
        (await guard.CheckAsync(110)).ShouldBe(TeacherApprovalCheck.NotApproved);
        (await GuardAsync(110)).ShouldBe(TeacherApprovalCheck.Approved);
    }

    // ---------------- Migration backfill ----------------

    [Fact]
    public async Task Migration_backfill_approves_currently_approved_and_school_linked_teachers_only()
    {
        var schoolId = await SeedSchoolAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.AddRange(
                new Teacher { UserId = 201, SchoolId = schoolId, ApprovalStatus = TeacherApprovalStatus.Approved },
                new Teacher { UserId = 202, ApprovalStatus = TeacherApprovalStatus.Approved }, // okulsuz geçiş dönemi kaydı
                new Teacher { UserId = 203, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved },
                // Onaylı okul öğretmeni bağımsızlığa geçti → Pending / Rejected; okul bağı korunur → erişim korunur.
                new Teacher { UserId = 204, SchoolId = schoolId, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending },
                new Teacher { UserId = 205, SchoolId = schoolId, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Rejected },
                // İlk başvurusu bekleyen / reddedilen → null kalır.
                new Teacher { UserId = 206, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending },
                new Teacher { UserId = 207, RequestedSchoolId = schoolId, ApprovalStatus = TeacherApprovalStatus.Pending },
                new Teacher { UserId = 208, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Rejected },
                new Teacher { UserId = 209, RequestedSchoolId = schoolId, ApprovalStatus = TeacherApprovalStatus.Rejected });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(AddTeacherAccountApprovedAt.BackfillSql);
            // İdempotent: ikinci çalıştırma değerleri değiştirmez.
            await ctx.Database.ExecuteSqlRawAsync(AddTeacherAccountApprovedAt.BackfillSql);
        }

        await using var check = _db.NewContext();
        var rows = await check.Teachers.AsNoTracking().ToDictionaryAsync(t => t.UserId);
        foreach (var approved in new[] { 201, 202, 203, 204, 205 })
            rows[approved].AccountApprovedAt.ShouldBe(rows[approved].CreateTime, $"UserId={approved} backfill edilmeli");
        foreach (var unapproved in new[] { 206, 207, 208, 209 })
            rows[unapproved].AccountApprovedAt.ShouldBeNull($"UserId={unapproved} onaysız kalmalı");
    }
}
