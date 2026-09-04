using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #11 — includeShared: öğretmen kendi worksheet'lerine ek olarak başka
/// öğretmenlerin PublicView/PublicAssignable worksheet'lerini görebilir (yalnız görüntüleme,
/// düzenleme değil). GetWorksheetsForTeacherAsync + GetWorksheetByIdAsync + keşif listelerinin
/// (Latest/Popular) bundan etkilenmediği bu dosyada doğrulanır.
/// </summary>
public class ExamServiceIncludeSharedTests : IDisposable
{
    private const int Owner = 10;
    private const int OtherTeacher = 20;
    private const int Admin = 999;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public ExamServiceIncludeSharedTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = Owner, FullName = "Ada Öğretmen" },
            new() { Id = OtherTeacher, FullName = "Bora Öğretmen" },
        });
    }

    private ExamService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), _authApi);

    private static ExamFilterDto Filter(bool includeShared = false, int? id = null) => new()
    {
        id = id ?? 0, pageNumber = 1, pageSize = 50, sortBy = "alphabetical", includeShared = includeShared,
    };

    private static UserProfileDto Profile(int id) => new() { Id = id, Role = "Teacher" };

    private async Task<int> SeedWorksheetAsync(
        string name,
        int? ownerUserId,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private)
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync() ?? new Grade { Name = "5" };
        if (grade.Id == 0) { ctx.Grades.Add(grade); await ctx.SaveChangesAsync(); }

        var ws = new Worksheet { Name = name, Description = "d", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    // ---- GetWorksheetsForTeacherAsync — includeShared=false (default, unchanged) ----

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_IncludeSharedFalse_OnlyOwnWorksheetsEvenIfOthersArePublic()
    {
        var mine = await SeedWorksheetAsync("Mine", Owner);
        await SeedWorksheetAsync("PublicOfOther", OtherTeacher, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: false), Profile(Owner), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldBe(new[] { mine });
    }

    // ---- GetWorksheetsForTeacherAsync — includeShared=true ----

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_IncludeSharedTrue_IncludesOtherTeachersPublicViewAndPublicAssignable()
    {
        var mine = await SeedWorksheetAsync("Mine", Owner);
        var pubView = await SeedWorksheetAsync("PubView", OtherTeacher, WorksheetTeacherSharing.PublicView);
        var pubAssign = await SeedWorksheetAsync("PubAssign", OtherTeacher, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: true), Profile(Owner), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldBe(new[] { mine, pubView, pubAssign }, ignoreOrder: true);
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_IncludeSharedTrue_OtherTeachersPrivateWorksheetNotIncluded()
    {
        await SeedWorksheetAsync("Mine", Owner);
        var privateOther = await SeedWorksheetAsync("PrivateOfOther", OtherTeacher, WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: true), Profile(Owner), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldNotContain(privateOther);
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_IncludeSharedTrue_LegacyOwnerlessWorksheetNotIncludedEvenIfPublic()
    {
        await SeedWorksheetAsync("Mine", Owner);
        var legacyPublic = await SeedWorksheetAsync("LegacyPublic", ownerUserId: null, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: true), Profile(Owner), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldNotContain(legacyPublic);
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_IncludeSharedTrue_SharedRow_CanEditFalseCanAssignFalseIsOwnerFalseOwnerNamePopulated()
    {
        await SeedWorksheetAsync("Mine", Owner);
        var shared = await SeedWorksheetAsync("PubView", OtherTeacher, WorksheetTeacherSharing.PublicView);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: true), Profile(Owner), isAdmin: false);

        var dto = page.Items.Single(i => i.Id == shared);
        dto.CanEdit.ShouldBeFalse();
        dto.CanAssign.ShouldBeFalse();
        dto.IsOwner.ShouldBeFalse();
        dto.OwnerName.ShouldBe("Bora Öğretmen");
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_IncludeSharedTrue_OwnRow_CanEditTrueIsOwnerTrue()
    {
        var mine = await SeedWorksheetAsync("Mine", Owner);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: true), Profile(Owner), isAdmin: false);

        var dto = page.Items.Single(i => i.Id == mine);
        dto.CanEdit.ShouldBeTrue();
        dto.IsOwner.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Admin_SeesEverythingRegardlessOfIncludeShared()
    {
        var mine = await SeedWorksheetAsync("Mine", Owner);
        var privateOther = await SeedWorksheetAsync("PrivateOfOther", OtherTeacher, WorksheetTeacherSharing.Private);
        var legacy = await SeedWorksheetAsync("Legacy", ownerUserId: null);

        await using var falseCtx = _db.NewContext();
        var pageFalse = await NewService(falseCtx).GetWorksheetsForTeacherAsync(Filter(includeShared: false), Profile(Admin), isAdmin: true);
        pageFalse.Items.Select(i => i.Id).ShouldBe(new[] { mine, privateOther, legacy }, ignoreOrder: true);

        await using var trueCtx = _db.NewContext();
        var pageTrue = await NewService(trueCtx).GetWorksheetsForTeacherAsync(Filter(includeShared: true), Profile(Admin), isAdmin: true);
        pageTrue.Items.Select(i => i.Id).ShouldBe(new[] { mine, privateOther, legacy }, ignoreOrder: true);
    }

    // ---- GetWorksheetByIdAsync ----

    [Fact]
    public async Task GetWorksheetByIdAsync_NonOwnerTeacher_PublicViewWorksheet_ReturnsDtoWithSharedFlags()
    {
        var id = await SeedWorksheetAsync("PubView", Owner, WorksheetTeacherSharing.PublicView);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(id, Profile(OtherTeacher), isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.CanEdit.ShouldBeFalse();
        dto.IsOwner.ShouldBeFalse();
        dto.OwnerName.ShouldBe("Ada Öğretmen");
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_NonOwnerTeacher_PublicAssignableWorksheet_ReturnsDto()
    {
        var id = await SeedWorksheetAsync("PubAssign", Owner, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(id, Profile(OtherTeacher), isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.CanEdit.ShouldBeFalse();
        dto.IsOwner.ShouldBeFalse();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_NonOwnerTeacher_PrivateWorksheet_ReturnsNull()
    {
        var id = await SeedWorksheetAsync("Private", Owner, WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(id, Profile(OtherTeacher), isAdmin: false)).ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_NonOwnerTeacher_LegacyOwnerlessWorksheet_ReturnsNullEvenIfPublic()
    {
        var id = await SeedWorksheetAsync("LegacyPublic", ownerUserId: null, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(id, Profile(OtherTeacher), isAdmin: false)).ShouldBeNull();
    }

    // ---- GetLatestWorksheetsAsync / GetPopularWorksheetsAsync — regression: public sızmaz ----

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
    public async Task GetLatestWorksheetsAsync_OwnerScoped_DoesNotIncludeOtherTeachersPublicWorksheet()
    {
        var mine = await SeedWorksheetAsync("Mine", Owner);
        await SeedWorksheetAsync("PubOfOther", OtherTeacher, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetLatestWorksheetsAsync(1, 50, ownerUserId: Owner);

        list.Select(i => i.Id).ShouldBe(new[] { mine });
    }

    [Fact]
    public async Task GetPopularWorksheetsAsync_OwnerScoped_DoesNotIncludeOtherTeachersPublicWorksheet()
    {
        var mine = await SeedWorksheetAsync("Mine", Owner);
        var pubOfOther = await SeedWorksheetAsync("PubOfOther", OtherTeacher, WorksheetTeacherSharing.PublicView);
        var gradeId = await GradeIdAsync();
        await AddInstanceAsync(mine, gradeId);
        await AddInstanceAsync(pubOfOther, gradeId);

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetPopularWorksheetsAsync(null, 1, 50, sinceDays: 30, ownerUserId: Owner);

        list.Select(i => i.Id).ShouldBe(new[] { mine });
    }

    public void Dispose() => _db.Dispose();
}
