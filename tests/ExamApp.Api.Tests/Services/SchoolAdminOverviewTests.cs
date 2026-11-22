using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1278 (#1071 S3): okul paneli yalnız yöneticinin okulunu gösterir — başka okulun öğretmeni / öğrencisi, bağımsız ve
/// onaysız öğretmen, bekleyen öğrenci listede yok; sayılar aynı kapsamda.
/// </summary>
public class SchoolAdminOverviewTests : IDisposable
{
    private const int AdminUserId = 1278;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public SchoolAdminOverviewTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>()
                .Select(id => new UserLookupResultDto { Id = id, FullName = $"U{id}" })
                .ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolA, int SchoolB, int Admin, int TeacherA, int SuspendedA, int StudentA5, int StudentA6,
        int Grade5);

    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        var g5 = new Grade { Name = "5. Sınıf" };
        var g6 = new Grade { Name = "6. Sınıf" };
        ctx.AddRange(a, b, g5, g6);
        await ctx.SaveChangesAsync();

        var approved = DateTime.UtcNow.AddDays(-10);
        var admin = new Teacher { UserId = AdminUserId, SchoolId = a.Id, AccountApprovedAt = approved.AddDays(-5), IsSchoolAdmin = true };
        var teacherA = new Teacher { UserId = 3001, SchoolId = a.Id, AccountApprovedAt = approved };
        var suspendedA = new Teacher { UserId = 3002, SchoolId = a.Id, AccountApprovedAt = approved, AccountSuspendedAt = DateTime.UtcNow };
        var unapprovedA = new Teacher { UserId = 3003, SchoolId = a.Id };
        var teacherB = new Teacher { UserId = 3004, SchoolId = b.Id, AccountApprovedAt = approved };
        var pendingApplicant = new Teacher { UserId = 3005, RequestedSchoolId = a.Id, ApprovalStatus = TeacherApprovalStatus.Pending };
        ctx.Teachers.AddRange(admin, teacherA, suspendedA, unapprovedA, teacherB, pendingApplicant);

        var s5 = new Student { UserId = 4001, StudentNumber = "20241234", SchoolId = a.Id, SchoolVerifiedAt = approved, GradeId = g5.Id };
        var s6 = new Student { UserId = 4002, StudentNumber = "20245678", SchoolId = a.Id, SchoolVerifiedAt = approved, GradeId = g6.Id };
        var pendingA = new Student { UserId = 4003, StudentNumber = "1", SchoolId = a.Id, GradeId = g5.Id };
        var studentB = new Student { UserId = 4004, StudentNumber = "2", SchoolId = b.Id, SchoolVerifiedAt = approved, GradeId = g5.Id };
        var pendingB = new Student { UserId = 4005, StudentNumber = "3", SchoolId = b.Id, GradeId = g5.Id };
        ctx.Students.AddRange(s5, s6, pendingA, studentB, pendingB);
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, admin.Id, teacherA.Id, suspendedA.Id, s5.Id, s6.Id, g5.Id);
    }

    [Fact]
    public async Task Summary_counts_only_own_school()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var summary = await new SchoolAdminOverviewService(ctx, _authApi).GetSummaryAsync(seed.SchoolA, AdminUserId, 1);

        summary.SchoolName.ShouldBe("Okul A");
        summary.TeacherCount.ShouldBe(3); // yönetici + öğretmen + askıdaki; onaysız ve B hariç
        summary.StudentCount.ShouldBe(2);
        summary.PendingStudentRequests.ShouldBe(1);
        summary.PendingTeacherApplications.ShouldBe(1);
    }

    [Fact]
    public async Task Teacher_list_is_school_scoped_admins_first_and_marks_suspended()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var page = await new SchoolAdminOverviewService(ctx, _authApi).ListTeachersAsync(seed.SchoolA, 1, 20);

        page.TotalCount.ShouldBe(3);
        page.Items.Select(t => t.TeacherId).First().ShouldBe(seed.Admin);
        page.Items.Select(t => t.TeacherId).ShouldBe([seed.Admin, seed.TeacherA, seed.SuspendedA], ignoreOrder: true);
        page.Items.Single(t => t.TeacherId == seed.Admin).IsSchoolAdmin.ShouldBeTrue();
        page.Items.Single(t => t.TeacherId == seed.SuspendedA).IsSuspended.ShouldBeTrue();
        page.Items.Single(t => t.TeacherId == seed.TeacherA).FullName.ShouldBe("U3001");
    }

    [Fact]
    public async Task Student_list_shows_verified_members_only_masked_and_filters_by_grade()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = new SchoolAdminOverviewService(ctx, _authApi);

        var all = await service.ListStudentsAsync(seed.SchoolA, null, 1, 20);
        all.Items.Select(s => s.StudentId).ShouldBe([seed.StudentA5, seed.StudentA6], ignoreOrder: true);
        all.Items.Single(s => s.StudentId == seed.StudentA5).StudentNumber.ShouldBe("****1234");

        var grade5 = await service.ListStudentsAsync(seed.SchoolA, seed.Grade5, 1, 20);
        grade5.Items.Select(s => s.StudentId).ShouldBe([seed.StudentA5]);
        grade5.Items[0].GradeName.ShouldBe("5. Sınıf");
    }

    [Fact]
    public async Task Names_missing_when_auth_api_fails_but_list_still_returns()
    {
        var seed = await SeedAsync();
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("down"));
        await using var ctx = _db.NewContext();

        var page = await new SchoolAdminOverviewService(ctx, _authApi).ListTeachersAsync(seed.SchoolA, 1, 20);

        page.TotalCount.ShouldBe(3);
        page.Items.ShouldAllBe(t => t.FullName == string.Empty);
    }
}
