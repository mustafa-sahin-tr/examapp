using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #191 — TeacherSharing=SchoolOnly, ExamService liste (includeShared) ve tekil detay.
/// Aktörler: OwnerA (okul A, sahip), PeerA (okul A), OtherB (okul B), Independent (okulsuz), Admin.
/// Liste filtresi sorgu düzeyinde (TotalCount == görünen satır sayısı; predicate SQL'e çevrilir).
/// </summary>
public class ExamServiceSchoolOnlyTests : IAsyncLifetime
{
    private const int OwnerA = 10;
    private const int PeerA = 20;
    private const int OtherB = 30;
    private const int Independent = 40;
    private const int Admin = 999;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private int _schoolA;
    private int _schoolB;
    private int _gradeId;

    public ExamServiceSchoolOnlyTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = OwnerA, FullName = "Ada Öğretmen" },
        });
    }

    public ValueTask InitializeAsync() => new(SeedActorsAsync());

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private ExamService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), _authApi);

    private static ExamFilterDto Filter(bool includeShared = true, int? id = null) => new()
    {
        id = id ?? 0, pageNumber = 1, pageSize = 50, sortBy = "alphabetical", includeShared = includeShared,
    };

    private static UserProfileDto Profile(int id) => new() { Id = id, Role = "Teacher" };

    private async Task SeedActorsAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();
        _gradeId = grade.Id;
        _schoolA = schoolA.Id;
        _schoolB = schoolB.Id;

        ctx.Teachers.AddRange(
            new Teacher { UserId = OwnerA, SchoolId = schoolA.Id },
            new Teacher { UserId = PeerA, SchoolId = schoolA.Id },
            new Teacher { UserId = OtherB, SchoolId = schoolB.Id },
            new Teacher { UserId = Independent, SchoolId = null, IsIndependentTutor = true });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> SeedWorksheetAsync(string name, int? ownerUserId, WorksheetTeacherSharing sharing)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = name, Description = "d", GradeId = _gradeId };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    // ---- Liste: includeShared=true ----

    [Fact]
    public async Task List_SameSchoolTeacher_SeesSchoolOnly_WithPublicAssignableSemantics()
    {
        var schoolOnly = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(PeerA), isAdmin: false);

        var dto = page.Items.ShouldHaveSingleItem();
        dto.Id.ShouldBe(schoolOnly);
        dto.TeacherSharing.ShouldBe(WorksheetTeacherSharing.SchoolOnly);
        dto.CanAssign.ShouldBeTrue();   // PublicAssignable ile aynı
        dto.CanEdit.ShouldBeFalse();
        dto.IsOwner.ShouldBeFalse();
        dto.OwnerName.ShouldBe("Ada Öğretmen");
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task List_DifferentSchoolTeacher_DoesNotSeeSchoolOnly_TotalCountAlsoExcludes()
    {
        await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);
        var pub = await SeedWorksheetAsync("Public", OwnerA, WorksheetTeacherSharing.PublicView);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(OtherB), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldBe(new[] { pub });
        page.TotalCount.ShouldBe(1); // filtre sorgu düzeyinde: CountAsync de SchoolOnly satırı saymaz
    }

    [Fact]
    public async Task List_IndependentTeacher_DoesNotSeeSchoolOnly_ButSeesPublic()
    {
        await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);
        var pubView = await SeedWorksheetAsync("PubView", OwnerA, WorksheetTeacherSharing.PublicView);
        var pubAssign = await SeedWorksheetAsync("PubAssign", OwnerA, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(Independent), isAdmin: false);

        page.Items.Select(i => i.Id).ShouldBe(new[] { pubView, pubAssign }, ignoreOrder: true);
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task List_TeacherWithoutTeacherRow_TreatedAsIndependent_DoesNotSeeSchoolOnly()
    {
        await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(777), isAdmin: false);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_Admin_SeesSchoolOnly()
    {
        var schoolOnly = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: false), Profile(Admin), isAdmin: true);

        page.Items.Select(i => i.Id).ShouldContain(schoolOnly);
    }

    [Fact]
    public async Task List_Owner_SeesOwnSchoolOnlyEvenWithoutIncludeShared()
    {
        var schoolOnly = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: false), Profile(OwnerA), isAdmin: false);

        var dto = page.Items.ShouldHaveSingleItem();
        dto.Id.ShouldBe(schoolOnly);
        dto.CanEdit.ShouldBeTrue();
        dto.IsOwner.ShouldBeTrue();
    }

    [Fact]
    public async Task List_IncludeSharedFalse_SameSchoolTeacher_DoesNotSeeSchoolOnly()
    {
        await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(includeShared: false), Profile(PeerA), isAdmin: false);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_ByIdBranch_DifferentSchoolTeacher_CannotFetchSchoolOnlyById()
    {
        var schoolOnly = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(id: schoolOnly), Profile(OtherB), isAdmin: false);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task List_LegacyOwnerlessSchoolOnly_NotVisibleToAnyone_ButAdmin()
    {
        var legacy = await SeedWorksheetAsync("LegacySchoolOnly", ownerUserId: null, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var peer = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(PeerA), isAdmin: false);
        var admin = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(Admin), isAdmin: true);

        peer.Items.ShouldBeEmpty();
        admin.Items.Select(i => i.Id).ShouldContain(legacy);
    }

    [Fact]
    public async Task List_Regression_PublicViewAndPublicAssignable_StillVisibleToDifferentSchoolAndIndependent()
    {
        var pubView = await SeedWorksheetAsync("PubView", OwnerA, WorksheetTeacherSharing.PublicView);
        var pubAssign = await SeedWorksheetAsync("PubAssign", OwnerA, WorksheetTeacherSharing.PublicAssignable);
        await SeedWorksheetAsync("Private", OwnerA, WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        foreach (var requester in new[] { PeerA, OtherB, Independent })
        {
            var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(), Profile(requester), isAdmin: false);
            page.Items.Select(i => i.Id).ShouldBe(new[] { pubView, pubAssign }, ignoreOrder: true, customMessage: $"requester={requester}");
            page.Items.Single(i => i.Id == pubAssign).CanAssign.ShouldBeTrue();
            page.Items.Single(i => i.Id == pubView).CanAssign.ShouldBeFalse();
        }
    }

    [Fact]
    public void List_Filter_IsAppliedInSql_NotInMemory()
    {
        // GetWorksheetsForTeacherAsync includeShared dalı bu predicate'i Where()'e verir; SQL'e
        // çevrildiğinin kanıtı: SchoolOnly (3) dalı ve Teachers alt sorgusu üretilen sorguda yer alır.
        using var ctx = _db.NewContext();
        var sql = ctx.Worksheets
            .Where(WorksheetAccess.VisibleToTeacherPredicate(ctx, PeerA, _schoolA))
            .ToQueryString();

        sql.ShouldContain("\"TeacherSharing\" = 3");
        sql.ShouldContain("FROM \"Teachers\"");
        sql.ShouldContain("\"CreateUserId\"");
    }

    // ---- Tekil: GetWorksheetByIdAsync ----

    [Fact]
    public async Task ById_SameSchoolTeacher_ReturnsDto_CanAssignTrue_CanEditFalse()
    {
        var id = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(id, Profile(PeerA), isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.CanAssign.ShouldBeTrue();
        dto.CanEdit.ShouldBeFalse();
        dto.IsOwner.ShouldBeFalse();
        dto.OwnerName.ShouldBe("Ada Öğretmen");
    }

    [Theory]
    [InlineData(OtherB)]
    [InlineData(Independent)]
    public async Task ById_DifferentSchoolOrIndependent_ReturnsNull(int requester)
    {
        var id = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(id, Profile(requester), isAdmin: false)).ShouldBeNull();
    }

    [Fact]
    public async Task ById_AdminAndOwner_ReturnDto()
    {
        var id = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(id, Profile(Admin), isAdmin: true)).ShouldNotBeNull();
        (await NewService(ctx).GetWorksheetByIdAsync(id, Profile(OwnerA), isAdmin: false)).ShouldNotBeNull();
    }

    [Fact]
    public async Task ById_StudentFlow_UnaffectedBySchoolOnly()
    {
        var id = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetByIdAsync(id, new UserProfileDto { Id = 5000, Role = "Student" }, isAdmin: false);

        dto.ShouldNotBeNull();
        dto!.CanAssign.ShouldBeFalse();
    }

    // ---- Öğretmen okul değiştirince (Teachers.SchoolId güncellenir) ----

    [Fact]
    public async Task OwnerMovesSchool_OldSchoolPeerStopsSeeing_NewSchoolPeerStartsSeeing()
    {
        var schoolOnly = await SeedWorksheetAsync("SchoolOnly", OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using (var before = _db.NewContext())
        {
            (await NewService(before).GetWorksheetsForTeacherAsync(Filter(), Profile(PeerA), false)).Items.Select(i => i.Id).ShouldBe(new[] { schoolOnly });
            (await NewService(before).GetWorksheetsForTeacherAsync(Filter(), Profile(OtherB), false)).Items.ShouldBeEmpty();
            (await NewService(before).GetWorksheetByIdAsync(schoolOnly, Profile(PeerA), false)).ShouldNotBeNull();
        }

        await using (var transfer = _db.NewContext())
        {
            var owner = await transfer.Teachers.SingleAsync(t => t.UserId == OwnerA);
            owner.SchoolId = _schoolB;
            await transfer.SaveChangesAsync();
        }

        await using var after = _db.NewContext();
        (await NewService(after).GetWorksheetsForTeacherAsync(Filter(), Profile(PeerA), false)).Items.ShouldBeEmpty();
        (await NewService(after).GetWorksheetByIdAsync(schoolOnly, Profile(PeerA), false)).ShouldBeNull();
        (await NewService(after).GetWorksheetsForTeacherAsync(Filter(), Profile(OtherB), false)).Items.Select(i => i.Id).ShouldBe(new[] { schoolOnly });
        (await NewService(after).GetWorksheetByIdAsync(schoolOnly, Profile(OtherB), false)).ShouldNotBeNull();
    }
}
