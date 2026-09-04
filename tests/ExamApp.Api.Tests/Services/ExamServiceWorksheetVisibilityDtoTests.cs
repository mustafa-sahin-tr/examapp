using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #9 — WorksheetDto'ya taşınan görünürlük alanları (TeacherSharing,
/// StudentVisibility, IsOwner, OwnerName, CanAssign). Davranış (kimin neyi gördüğü)
/// değişmez; yalnız yeni alanların doğru map'lendiği doğrulanır.
/// </summary>
public class ExamServiceWorksheetVisibilityDtoTests : IDisposable
{
    private const int UserA = 10;
    private const int UserB = 20;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public ExamServiceWorksheetVisibilityDtoTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = UserA, FullName = "Ada Öğretmen" },
            new() { Id = UserB, FullName = "Bora Öğretmen" },
        });
    }

    private ExamService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), _authApi);

    private static ExamFilterDto Filter(int? id = null) => new()
    {
        id = id ?? 0, pageNumber = 1, pageSize = 50, sortBy = "alphabetical",
    };

    private static UserProfileDto Profile(int id) => new() { Id = id, Role = "Teacher" };

    private async Task<int> SeedWorksheetAsync(
        string name,
        int? ownerUserId,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private,
        WorksheetStudentVisibility studentVisibility = WorksheetStudentVisibility.Normal)
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync() ?? new Grade { Name = "5" };
        if (grade.Id == 0) { ctx.Grades.Add(grade); await ctx.SaveChangesAsync(); }

        var ws = new Worksheet { Name = name, Description = "d", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        ws.StudentVisibility = studentVisibility;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    // ---- GetWorksheetsForTeacherAsync ----

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_OwnerTeacher_SetsIsOwnerAndCanAssignTrue()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        var dto = page.Items.Single(i => i.Id == a);
        dto.IsOwner.ShouldBeTrue();
        dto.CanAssign.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Teacher_ForeignWorksheetNotInList()
    {
        await SeedWorksheetAsync("Alfa", UserA);
        var b = await SeedWorksheetAsync("Beta", UserB);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldNotContain(b);
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_LegacyWorksheet_AdminOnly_CanAssignTrueIsOwnerFalse()
    {
        var legacy = await SeedWorksheetAsync("Legacy", ownerUserId: null);

        await using var teacherCtx = _db.NewContext();
        var teacherPage = await NewService(teacherCtx)
            .GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);
        teacherPage.Items.ShouldBeEmpty();

        await using var adminCtx = _db.NewContext();
        var adminPage = await NewService(adminCtx)
            .GetWorksheetsForTeacherAsync(Filter(), Profile(999), isAdmin: true);

        var dto = adminPage.Items.Single(i => i.Id == legacy);
        dto.IsOwner.ShouldBeFalse();
        dto.CanAssign.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_SerializesTeacherSharingAndStudentVisibility()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA,
            WorksheetTeacherSharing.PublicAssignable, WorksheetStudentVisibility.Restricted);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        var dto = page.Items.Single(i => i.Id == a);
        dto.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicAssignable);
        dto.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_DefaultWorksheet_SharingPrivateVisibilityNormal()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        var dto = page.Items.Single(i => i.Id == a);
        dto.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        dto.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Normal);
    }

    // ---- GetWorksheetByIdAsync ----

    [Fact]
    public async Task GetWorksheetByIdAsync_Admin_PopulatesOwnerName()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(a, Profile(999), isAdmin: true);

        dto.ShouldNotBeNull();
        dto!.OwnerName.ShouldBe("Ada Öğretmen");
        dto.IsOwner.ShouldBeFalse();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_TeacherNotOwner_ReturnsNull()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(a, Profile(UserB), isAdmin: false)).ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_Owner_SetsIsOwnerAndCanAssignTrue()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA,
            WorksheetTeacherSharing.PublicView, WorksheetStudentVisibility.Restricted);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(a, Profile(UserA), isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.IsOwner.ShouldBeTrue();
        dto.CanAssign.ShouldBeTrue();
        dto.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicView);
        dto.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    // ---- GetLatestWorksheetsAsync / GetPopularWorksheetsAsync (keşif listeleri) ----

    private async Task AddInstanceAsync(int worksheetId, int gradeId)
    {
        await using var ctx = _db.NewContext();
        var student = new Student
        {
            UserId = Random.Shared.Next(1000, 9999), StudentNumber = "s", SchoolName = "x", GradeId = gradeId,
        };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        ctx.TestInstances.Add(new WorksheetInstance
        {
            WorksheetId = worksheetId, StudentId = student.Id,
            StartTime = DateTime.UtcNow.AddDays(-1), Status = WorksheetInstanceStatus.Completed,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> GradeIdAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Grades.FirstAsync()).Id;
    }

    [Fact]
    public async Task GetLatestWorksheetsAsync_OwnerUserId_OwnWorksheet_SetsIsOwnerAndCanAssignTrue()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA,
            WorksheetTeacherSharing.PublicView, WorksheetStudentVisibility.Restricted);

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetLatestWorksheetsAsync(1, 50, ownerUserId: UserA);

        var dto = list.Single(i => i.Id == a);
        dto.IsOwner.ShouldBeTrue();
        dto.CanAssign.ShouldBeTrue();
        dto.OwnerName.ShouldBeNull();
        dto.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicView);
        dto.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task GetLatestWorksheetsAsync_NullOwnerUserId_DiscoveryList_IsOwnerAndCanAssignFalse()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetLatestWorksheetsAsync(1, 50, ownerUserId: null);

        var dto = list.Single(i => i.Id == a);
        dto.IsOwner.ShouldBeFalse();
        dto.CanAssign.ShouldBeFalse();
        dto.OwnerName.ShouldBeNull();
    }

    [Fact]
    public async Task GetPopularWorksheetsAsync_OwnerUserId_OwnWorksheet_SetsIsOwnerAndCanAssignTrue()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA,
            WorksheetTeacherSharing.PublicAssignable, WorksheetStudentVisibility.Restricted);
        await AddInstanceAsync(a, await GradeIdAsync());

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetPopularWorksheetsAsync(null, 1, 50, sinceDays: 30, ownerUserId: UserA);

        var dto = list.Single(i => i.Id == a);
        dto.IsOwner.ShouldBeTrue();
        dto.CanAssign.ShouldBeTrue();
        dto.OwnerName.ShouldBeNull();
        dto.TeacherSharing.ShouldBe(WorksheetTeacherSharing.PublicAssignable);
        dto.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Restricted);
    }

    [Fact]
    public async Task GetPopularWorksheetsAsync_NullOwnerUserId_DiscoveryList_IsOwnerAndCanAssignFalse()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);
        await AddInstanceAsync(a, await GradeIdAsync());

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetPopularWorksheetsAsync(null, 1, 50, sinceDays: 30, ownerUserId: null);

        var dto = list.Single(i => i.Id == a);
        dto.IsOwner.ShouldBeFalse();
        dto.CanAssign.ShouldBeFalse();
        dto.OwnerName.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
