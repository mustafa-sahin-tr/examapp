using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #11 — WorksheetDetailService.GetWorksheetDetailAsync artık:
///  (a) Public* paylaşımlı worksheet'leri sahibi olmayan öğretmenlere de gösterir (CanView genişledi),
///  (b) OwnerName'i çözer (öncesinde her zaman null'du),
///  (c) CanEdit'i doğru hesaplar (öncesinde her zaman false'tu — sahibi için de; latent bug fix).
/// </summary>
public class WorksheetDetailServiceOwnerNameTests : IDisposable
{
    private const int OwnerTeacherUserId = 6000;
    private const int OtherTeacherUserId = 6001;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public WorksheetDetailServiceOwnerNameTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = OwnerTeacherUserId, FullName = "Ada Öğretmen" },
        });
    }

    private WorksheetDetailService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private async Task<int> SeedWorksheetAsync(
        int? ownerUserId,
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "Test", Description = "d", GradeId = grade.Id, MaxDurationSeconds = 600 };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_NonOwnerTeacher_PublicViewWorksheet_ReturnsDtoNotNull()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.PublicView);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherTeacherUserId);

        dto.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_NonOwnerTeacher_PublicAssignableWorksheet_ReturnsDtoNotNull()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherTeacherUserId);

        dto.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_NonOwnerTeacher_PrivateWorksheet_ReturnsNull()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherTeacherUserId);

        dto.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_NonOwnerTeacher_SharedWorksheet_OwnerNamePopulated()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.PublicView);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherTeacherUserId);

        dto.ShouldNotBeNull();
        dto!.Worksheet.OwnerName.ShouldBe("Ada Öğretmen");
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_NonOwnerTeacher_SharedWorksheet_CanEditIsFalse()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherTeacherUserId);

        dto.ShouldNotBeNull();
        dto!.Worksheet.CanEdit.ShouldBeFalse();
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_Owner_CanEditIsTrue()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OwnerTeacherUserId);

        dto.ShouldNotBeNull();
        dto!.Worksheet.CanEdit.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWorksheetDetailAsync_Admin_CanEditIsTrueEvenNotOwner()
    {
        var id = await SeedWorksheetAsync(OwnerTeacherUserId, WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherTeacherUserId, isAdmin: true);

        dto.ShouldNotBeNull();
        dto!.Worksheet.CanEdit.ShouldBeTrue();
    }

    public void Dispose() => _db.Dispose();
}
