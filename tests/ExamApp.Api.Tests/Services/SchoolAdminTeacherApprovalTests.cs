using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1277 (#1071 S2): okul yöneticisi yalnız kendi okuluna gelen öğretmen başvurularını görür ve karar verir; başka okul,
/// bağımsız öğretmen başvurusu ve kendi kaydı kapsam dışı (404). Yönetici olmayan öğretmen → 403. Audit aktörü yöneticidir.
/// </summary>
public class SchoolAdminTeacherApprovalTests : IDisposable
{
    private const int AdminUserId = 1277;
    private const string AdminSub = "kc-school-admin-1277";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public SchoolAdminTeacherApprovalTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"U{id}", Email = $"u{id}@okul.k12.tr" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolA, int SchoolB, int AdminTeacherId, int PendingA, int PendingB, int Independent);

    /// <summary>A okulunun yöneticisi + A'ya ve B'ye bekleyen birer okul talebi + bekleyen bağımsız başvuru.</summary>
    private async Task<Seed> SeedAsync(bool isSchoolAdmin = true)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        ctx.AddRange(a, b);
        await ctx.SaveChangesAsync();
        var admin = new Teacher
        {
            UserId = AdminUserId, SchoolId = a.Id, ApprovalStatus = TeacherApprovalStatus.Approved,
            AccountApprovedAt = DateTime.UtcNow.AddDays(-30), IsSchoolAdmin = isSchoolAdmin
        };
        var pendingA = new Teacher { UserId = 2001, RequestedSchoolId = a.Id, ApprovalStatus = TeacherApprovalStatus.Pending };
        var pendingB = new Teacher { UserId = 2002, RequestedSchoolId = b.Id, ApprovalStatus = TeacherApprovalStatus.Pending };
        var independent = new Teacher { UserId = 2003, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending };
        ctx.Teachers.AddRange(admin, pendingA, pendingB, independent);
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, admin.Id, pendingA.Id, pendingB.Id, independent.Id);
    }

    private SchoolApplicationScope Scope(Seed seed) => new(seed.SchoolA, AdminUserId);

    private TeacherApprovalService NewService(AppDbContext ctx) =>
        new(ctx, _authApi, auditService: new AdminUserActionAuditService(ctx));

    [Fact]
    public async Task Pending_list_and_count_only_show_own_school_requests()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var page = await service.ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 20, default, Scope(seed));

        page.Items.Select(i => i.TeacherId).ShouldBe([seed.PendingA]);
        (await service.CountPendingAsync(Scope(seed))).ShouldBe(1);

        // Platform admini (kapsamsız) hepsini görmeye devam eder.
        var all = await service.ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 20);
        all.Items.Select(i => i.TeacherId).ShouldBe([seed.PendingA, seed.PendingB, seed.Independent], ignoreOrder: true);
    }

    [Fact]
    public async Task Approve_links_own_school_request_and_audits_the_school_admin()
    {
        var seed = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).ApproveAsync(seed.PendingA, AdminUserId, AdminSub, default, Scope(seed));
            result.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var teacher = await check.Teachers.AsNoTracking().SingleAsync(t => t.Id == seed.PendingA);
        teacher.SchoolId.ShouldBe(seed.SchoolA);
        teacher.RequestedSchoolId.ShouldBeNull();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        var audit = (await check.AdminUserActionLogs.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
        audit.ActorKeycloakId.ShouldBe(AdminSub);
        audit.Action.ShouldBe(AdminUserAction.TeacherApproved);
        audit.TargetId.ShouldBe(seed.PendingA);
        audit.ToSchoolId.ShouldBe(seed.SchoolA);

        // Karar verilen başvuru "Tümü" geçmişinde okulda görünür.
        var history = await NewService(check).ListApplicationsAsync(TeacherApplicationStatusFilter.All, 1, 20, default, Scope(seed));
        history.Items.Select(i => i.TeacherId).ShouldBe([seed.PendingA]);
    }

    [Fact]
    public async Task Other_school_and_independent_applications_are_not_found_and_unchanged()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ApproveAsync(seed.PendingB, AdminUserId, AdminSub, default, Scope(seed))).NotFound.ShouldBeTrue();
        (await service.RejectAsync(seed.PendingB, "no", AdminUserId, AdminSub, default, Scope(seed))).NotFound.ShouldBeTrue();
        (await service.ApproveAsync(seed.Independent, AdminUserId, AdminSub, default, Scope(seed))).NotFound.ShouldBeTrue();
        (await service.GetApplicationAsync(seed.PendingB, default, Scope(seed))).ShouldBeNull();
        (await service.GetApplicationAsync(seed.PendingA, default, Scope(seed))).ShouldNotBeNull();

        await using var check = _db.NewContext();
        (await check.Teachers.AsNoTracking().SingleAsync(t => t.Id == seed.PendingB)).ApprovalStatus
            .ShouldBe(TeacherApprovalStatus.Pending);
        (await check.AdminUserActionLogs.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Reject_own_school_request_keeps_reason()
    {
        var seed = await SeedAsync();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).RejectAsync(seed.PendingA, " Not our staff ", AdminUserId, AdminSub, default, Scope(seed)))
                .Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var teacher = await check.Teachers.AsNoTracking().SingleAsync(t => t.Id == seed.PendingA);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        teacher.RejectionReason.ShouldBe("Not our staff");
        teacher.SchoolId.ShouldBeNull();
    }

    // ---- controller: yetki DB'den çözülür ----

    private SchoolAdminController NewController(AppDbContext ctx)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = AdminUserId, KeycloakId = AdminSub, Role = "Teacher" });
        var services = new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();
        return new SchoolAdminController(NewService(ctx), new SchoolAdminResolver(ctx), Substitute.For<IAdminDataAccessAuditService>(),
            new SchoolAdminOverviewService(ctx, _authApi))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, AdminSub)], "Test")),
                    RequestServices = services
                }
            }
        };
    }

    [Fact]
    public async Task Teacher_who_is_not_a_school_admin_gets_403()
    {
        var seed = await SeedAsync(isSchoolAdmin: false);
        await using var ctx = _db.NewContext();
        var controller = NewController(ctx);

        (await controller.GetSummary(default)).ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
        (await controller.Approve(seed.PendingA, default)).ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
        (await controller.GetTeacherApplications(ct: default)).Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
        (await controller.GetTeachers(ct: default)).Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
        (await controller.GetStudents(ct: default)).Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task School_admin_summary_counts_and_other_school_approve_is_404()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();
        var controller = NewController(ctx);

        var summary = (await controller.GetSummary(default)).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<SchoolAdminSummaryDto>();
        summary.SchoolId.ShouldBe(seed.SchoolA);
        summary.PendingTeacherApplications.ShouldBe(1);
        (await controller.Approve(seed.PendingB, default)).ShouldBeOfType<NotFoundObjectResult>();
        (await controller.Approve(seed.PendingA, default)).ShouldBeOfType<OkObjectResult>();
    }
}
