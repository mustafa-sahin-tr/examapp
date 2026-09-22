using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #191 — WorksheetAuthoringService ve SchoolOnly:
///  - UpdateVisibilityAsync: sahibin okulu yoksa SchoolOnly 400 (yeni localizer anahtarı); okullu sahip için kaydedilir.
///  - CopyWorksheetAsync: aynı okul kopyalar, farklı okul / okulsuz NotFound, admin kopyalar.
///  - DeleteWorksheetAsync / arka plan görseli: aynı okul görür ama düzenleyemez (403), farklı okul 404.
/// </summary>
public class WorksheetAuthoringSchoolOnlyTests : IAsyncLifetime
{
    private const int OwnerA = 10;
    private const int PeerA = 20;
    private const int OtherB = 30;
    private const int Independent = 40;
    private const int Admin = 999;

    private readonly TestDb _db = TestDb.Create();

    private static readonly string SchoolOnlyRequiresSchoolMessage =
        FallbackMessageLocalizer.Instance["worksheets.authoring.schoolOnlyRequiresSchool"].Value;

    public ValueTask InitializeAsync() => new(SeedActorsAsync());

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private WorksheetAuthoringService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private int _gradeId;

    private async Task SeedActorsAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();
        _gradeId = grade.Id;

        ctx.Teachers.AddRange(
            new Teacher { UserId = OwnerA, SchoolId = schoolA.Id },
            new Teacher { UserId = PeerA, SchoolId = schoolA.Id },
            new Teacher { UserId = OtherB, SchoolId = schoolB.Id },
            new Teacher { UserId = Independent, SchoolId = null, IsIndependentTutor = true });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> SeedWorksheetAsync(int? ownerUserId, WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = "Kaynak", Description = "d", GradeId = _gradeId };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private static UpdateWorksheetVisibilityDto Dto(WorksheetTeacherSharing sharing) => new()
    {
        TeacherSharing = sharing, StudentVisibility = WorksheetStudentVisibility.Normal,
    };

    private async Task<WorksheetTeacherSharing> SharingOfAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return (await ctx.Worksheets.SingleAsync(w => w.Id == id)).TeacherSharing;
    }

    // ---- UpdateVisibilityAsync ----

    [Fact]
    public async Task UpdateVisibility_SchoolBoundOwner_SchoolOnlyPersisted()
    {
        var id = await SeedWorksheetAsync(OwnerA);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), OwnerA, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
        (await SharingOfAsync(id)).ShouldBe(WorksheetTeacherSharing.SchoolOnly);
    }

    [Fact]
    public async Task UpdateVisibility_IndependentOwner_SchoolOnlyRejectedWith400Shape()
    {
        var id = await SeedWorksheetAsync(Independent);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), Independent, isAdmin: false);

        // Controller: Success=false && !NotFound && !Forbidden → BadRequest(result)
        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeFalse();
        r.Forbidden.ShouldBeFalse();
        r.Message.ShouldBe(SchoolOnlyRequiresSchoolMessage);
        (await SharingOfAsync(id)).ShouldBe(WorksheetTeacherSharing.Private); // değişmedi
    }

    [Fact]
    public async Task UpdateVisibility_OwnerWithoutTeacherRow_SchoolOnlyRejected()
    {
        var id = await SeedWorksheetAsync(4242); // Teachers'ta satırı yok → okulsuz

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), 4242, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(SchoolOnlyRequiresSchoolMessage);
    }

    [Fact]
    public async Task UpdateVisibility_Admin_SchoolOnlyOnIndependentOwnersWorksheet_Rejected_OwnerSchoolDecides()
    {
        // Karar istekçinin (admin) değil, SAHİBİN okuluna göre: okulsuz sahip → 400.
        var id = await SeedWorksheetAsync(Independent);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), Admin, isAdmin: true);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(SchoolOnlyRequiresSchoolMessage);
    }

    [Fact]
    public async Task UpdateVisibility_Admin_SchoolOnlyOnSchoolBoundOwnersWorksheet_Succeeds()
    {
        var id = await SeedWorksheetAsync(OwnerA);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), Admin, isAdmin: true);

        r.Success.ShouldBeTrue(r.Message);
        (await SharingOfAsync(id)).ShouldBe(WorksheetTeacherSharing.SchoolOnly);
    }

    [Fact]
    public async Task UpdateVisibility_Admin_SchoolOnlyOnLegacyOwnerlessWorksheet_Rejected()
    {
        var id = await SeedWorksheetAsync(ownerUserId: null);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), Admin, isAdmin: true);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(SchoolOnlyRequiresSchoolMessage);
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.Private)]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public async Task UpdateVisibility_Regression_IndependentOwner_CanStillChooseEveryoneModes(WorksheetTeacherSharing sharing)
    {
        var id = await SeedWorksheetAsync(Independent, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(sharing), Independent, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
        (await SharingOfAsync(id)).ShouldBe(sharing);
    }

    [Fact]
    public async Task UpdateVisibility_NonOwnerSameSchool_StillForbidden()
    {
        var id = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.Private), PeerA, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Forbidden.ShouldBeTrue();
    }

    // ---- CopyWorksheetAsync ----

    [Fact]
    public async Task Copy_SameSchoolTeacher_Succeeds_CopyIsPrivateAndOwnedByCopier()
    {
        var sourceId = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).CopyWorksheetAsync(sourceId, PeerA, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
        var copy = await ctx.Worksheets.SingleAsync(w => w.Id == r.WorksheetId);
        copy.CreateUserId.ShouldBe(PeerA);
        copy.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        copy.SourceWorksheetId.ShouldBe(sourceId);
    }

    [Theory]
    [InlineData(OtherB)]
    [InlineData(Independent)]
    public async Task Copy_DifferentSchoolOrIndependent_NotFound(int requester)
    {
        var sourceId = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).CopyWorksheetAsync(sourceId, requester, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        (await ctx.Worksheets.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Copy_Admin_Succeeds()
    {
        var sourceId = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).CopyWorksheetAsync(sourceId, Admin, isAdmin: true);

        r.Success.ShouldBeTrue(r.Message);
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public async Task Copy_Regression_PublicSource_DifferentSchoolStillCopies(WorksheetTeacherSharing sharing)
    {
        var sourceId = await SeedWorksheetAsync(OwnerA, sharing);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).CopyWorksheetAsync(sourceId, OtherB, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
    }

    // ---- DeleteWorksheetAsync: görünürlük → 404 / 403 ayrımı ----

    [Fact]
    public async Task Delete_SameSchoolTeacher_VisibleButForbidden()
    {
        var id = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).DeleteWorksheetAsync(id, PeerA, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeFalse();
        r.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_DifferentSchoolTeacher_NotFound()
    {
        var id = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).DeleteWorksheetAsync(id, OtherB, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        r.Forbidden.ShouldBeFalse();
    }

    // ---- UpdateVisibilityAsync: Public* → SchoolOnly geçişinde okul dışı grant/talep iptali ----

    private async Task<(int grantSameSchool, int grantOtherSchool, int grantIndependent, int reqOtherSchool, int reqSameSchool)>
        SeedGrantsAndRequestsAsync(int worksheetId)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerA);
        var gSame = new WorksheetAccessGrant { WorksheetId = worksheetId, TeacherUserId = PeerA };
        var gOther = new WorksheetAccessGrant { WorksheetId = worksheetId, TeacherUserId = OtherB };
        var gIndep = new WorksheetAccessGrant { WorksheetId = worksheetId, TeacherUserId = Independent };
        var rOther = new WorksheetAccessRequest { WorksheetId = worksheetId, RequesterUserId = OtherB, Status = WorksheetAccessRequestStatus.Pending };
        var rSame = new WorksheetAccessRequest { WorksheetId = worksheetId, RequesterUserId = PeerA, Status = WorksheetAccessRequestStatus.Pending };
        ctx.AddRange(gSame, gOther, gIndep, rOther, rSame);
        await ctx.SaveChangesAsync();
        return (gSame.Id, gOther.Id, gIndep.Id, rOther.Id, rSame.Id);
    }

    [Fact]
    public async Task UpdateVisibility_PublicToSchoolOnly_RevokesOnlyOutOfSchoolGrants_RejectsOnlyOutOfSchoolPendingRequests()
    {
        var id = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.PublicView);
        var (gSame, gOther, gIndep, rOther, rSame) = await SeedGrantsAndRequestsAsync(id);

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), OwnerA, isAdmin: false);
            r.Success.ShouldBeTrue(r.Message);
        }

        await using var check = _db.NewContext();
        (await check.WorksheetAccessGrants.SingleAsync(g => g.Id == gSame)).RevokedAt.ShouldBeNull();       // aynı okul korunur
        (await check.WorksheetAccessGrants.SingleAsync(g => g.Id == gOther)).RevokedAt.ShouldNotBeNull();   // farklı okul iptal
        (await check.WorksheetAccessGrants.SingleAsync(g => g.Id == gIndep)).RevokedAt.ShouldNotBeNull();   // okulsuz iptal

        var reqOther = await check.WorksheetAccessRequests.SingleAsync(r => r.Id == rOther);
        reqOther.Status.ShouldBe(WorksheetAccessRequestStatus.Rejected);
        reqOther.DecidedByUserId.ShouldBe(OwnerA);
        reqOther.DecisionAt.ShouldNotBeNull();
        (await check.WorksheetAccessRequests.SingleAsync(r => r.Id == rSame)).Status.ShouldBe(WorksheetAccessRequestStatus.Pending);
    }

    [Fact]
    public async Task UpdateVisibility_SchoolOnlyBackToPublicView_RevokedGrantDoesNotResurrect()
    {
        var id = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.PublicView);
        var (gSame, gOther, _, _, _) = await SeedGrantsAndRequestsAsync(id);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.SchoolOnly), OwnerA, false)).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.PublicView), OwnerA, false)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.WorksheetAccessGrants.SingleAsync(g => g.Id == gOther)).RevokedAt.ShouldNotBeNull(); // dirilmez
        (await check.WorksheetAccessGrants.SingleAsync(g => g.Id == gSame)).RevokedAt.ShouldBeNull();     // dokunulmadı
        (await SharingOfAsync(id)).ShouldBe(WorksheetTeacherSharing.PublicView);
    }

    [Fact]
    public async Task UpdateVisibility_Regression_ToPrivate_StillRevokesAllGrantsIncludingSameSchool()
    {
        var id = await SeedWorksheetAsync(OwnerA, WorksheetTeacherSharing.PublicView);
        var (gSame, gOther, gIndep, rOther, rSame) = await SeedGrantsAndRequestsAsync(id);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).UpdateVisibilityAsync(id, Dto(WorksheetTeacherSharing.Private), OwnerA, false)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.WorksheetAccessGrants.Where(g => g.RevokedAt == null).CountAsync()).ShouldBe(0);
        (await check.WorksheetAccessRequests.Where(r => r.Status == WorksheetAccessRequestStatus.Pending).CountAsync()).ShouldBe(0);
    }
}
