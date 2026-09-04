using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #4 — "öğretmen sadece kendi worksheet'lerini görür, admin hepsini".
/// ExamService.GetWorksheetsForTeacherAsync + GetWorksheetByIdAsync + CanEdit/CreatedBy alanları.
/// </summary>
public class ExamServiceWorksheetAuthTests : IDisposable
{
    private const int UserA = 10;
    private const int UserB = 20;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public ExamServiceWorksheetAuthTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = UserA, FullName = "Ada Öğretmen" },
            new() { Id = UserB, FullName = "Bora Öğretmen" },
        });
    }

    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), _authApi);

    private static ExamFilterDto Filter(int? id = null) => new()
    {
        id = id ?? 0, pageNumber = 1, pageSize = 50, sortBy = "alphabetical",
    };

    private static UserProfileDto Profile(int id) => new() { Id = id, Role = "Teacher" };

    /// <summary>Bir worksheet oluşturur; <paramref name="ownerUserId"/> null ise legacy kayıt.</summary>
    private async Task<int> SeedWorksheetAsync(string name, int? ownerUserId)
    {
        await using var ctx = _db.NewContext();
        var grade = await ctx.Grades.FirstOrDefaultAsync()
                    ?? new Grade { Name = "5" };
        if (grade.Id == 0) { ctx.Grades.Add(grade); await ctx.SaveChangesAsync(); }

        var ws = new Worksheet { Name = name, Description = "d", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId; // ApplyAuditInfo Added durumunda 0 yazar; Modified'de dokunmaz
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    // ---- GetWorksheetsForTeacherAsync ----

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Teacher_SeesOnlyOwnWorksheets()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);
        await SeedWorksheetAsync("Beta", UserB);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldBe(new[] { a });
        page.TotalCount.ShouldBe(1);
        page.Items.Single().CanEdit.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Teacher_LegacyWorksheetNotVisible()
    {
        await SeedWorksheetAsync("Legacy", ownerUserId: null);
        var mine = await SeedWorksheetAsync("Benimki", UserA);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldBe(new[] { mine });
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Teacher_CannotFetchForeignWorksheetById()
    {
        await SeedWorksheetAsync("Alfa", UserA);
        var b = await SeedWorksheetAsync("Beta", UserB);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(id: b), Profile(UserA), isAdmin: false);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Admin_SeesAllIncludingLegacy_WithCreatedByName()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);
        var b = await SeedWorksheetAsync("Beta", UserB);
        var legacy = await SeedWorksheetAsync("Legacy", ownerUserId: null);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(999), isAdmin: true);

        page.Items.Select(i => i.Id).ShouldBe(new[] { a, b, legacy }, ignoreOrder: true);
        page.Items.ShouldAllBe(i => i.CanEdit);
        page.Items.Single(i => i.Id == a).CreatedByName.ShouldBe("Ada Öğretmen");
        page.Items.Single(i => i.Id == legacy).CreatedByName.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Teacher_DoesNotResolveCreatedByName()
    {
        await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        page.Items.ShouldAllBe(i => i.CreatedByName == null);
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>());
    }

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_Teacher_CreatedByUserIdPopulatedForOwnWorksheets()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(UserA), isAdmin: false);

        page.Items.Single(i => i.Id == a).CreatedByUserId.ShouldBe(UserA);
    }

    // ---- GetWorksheetByIdAsync ----

    [Fact]
    public async Task GetWorksheetByIdAsync_TeacherOwner_ReturnsDto()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(a, Profile(UserA), isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.CanEdit.ShouldBeTrue();
        dto.CreatedByName.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_TeacherNotOwner_ReturnsNull()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(a, Profile(UserB), isAdmin: false)).ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_TeacherLegacyWorksheet_ReturnsNull()
    {
        var legacy = await SeedWorksheetAsync("Legacy", ownerUserId: null);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(legacy, Profile(UserA), isAdmin: false)).ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_Admin_ReturnsDtoWithCreatedByName()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(a, Profile(999), isAdmin: true);

        dto.ShouldNotBeNull();
        dto!.CanEdit.ShouldBeTrue();
        dto.CreatedByName.ShouldBe("Ada Öğretmen");
    }

    [Fact]
    public async Task GetWorksheetByIdAsync_Student_ExemptFromOwnershipCheck()
    {
        var a = await SeedWorksheetAsync("Alfa", UserA);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(a, new UserProfileDto { Id = 777, Role = "Student" }, isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.CreatedByUserId.ShouldBeNull(); // gizlilik: sahibi/admin değil
    }

    public void Dispose() => _db.Dispose();
}
