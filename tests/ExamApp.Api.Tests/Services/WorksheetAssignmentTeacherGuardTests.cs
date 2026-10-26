using ExamApp.Api.Data;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573 (review): <c>GetWorksheetAssignmentsForTeacherAsync</c> — ataması olmayan ve sayfanın sahibi/admin olmayan istek
/// sahibine ad ve tür dönülmez (boş DTO, "worksheet yok" yanıtıyla aynı); sahip ad + türü alır.
/// </summary>
public class WorksheetAssignmentTeacherGuardTests : IDisposable
{
    private const int Owner = 5101;
    private const int Stranger = 5102;
    private const int StudentUserId = 5103;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static WorksheetAssignmentService NewAssignments(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    /// <summary>Öğretmenin etkinlik sayfası; öğrenciye atanmamış (atama yok).</summary>
    private async Task<int> SeedUnassignedActivityAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "6" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var ws = new Worksheet { Name = "Gizli etkinlik adı", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Activity };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        // Sahibi açıkça yaz (kayıt sırasında ambient kullanıcı CreateUserId'yi ezebilir).
        await ctx.Worksheets.Where(w => w.Id == ws.Id).ExecuteUpdateAsync(u => u.SetProperty(w => w.CreateUserId, (int?)Owner));
        return ws.Id;
    }

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_NonOwnerWithoutAssignments_ReturnsEmptyDto()
    {
        var wsId = await SeedUnassignedActivityAsync();

        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.For(Stranger, schoolId: null));

        view.WorksheetId.ShouldBe(wsId);
        view.WorksheetName.ShouldBe(string.Empty);
        view.WorksheetKind.ShouldBe(WorksheetKind.Test); // varsayılan; gerçek tür (Activity) sızmaz
        view.Assignments.ShouldBeEmpty();
        view.Summary.TotalAssignments.ShouldBe(0);
    }

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_OwnerWithoutAssignments_ReturnsNameAndKind()
    {
        var wsId = await SeedUnassignedActivityAsync();

        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.For(Owner, schoolId: null));

        view.WorksheetName.ShouldBe("Gizli etkinlik adı");
        view.WorksheetKind.ShouldBe(WorksheetKind.Activity);
        view.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_UnrestrictedAdminWithoutAssignments_ReturnsNameAndKind()
    {
        var wsId = await SeedUnassignedActivityAsync();

        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.Unrestricted(Stranger));

        view.WorksheetName.ShouldBe("Gizli etkinlik adı");
        view.WorksheetKind.ShouldBe(WorksheetKind.Activity);
    }

    [Fact]
    public async Task GetWorksheetAssignmentsForTeacherAsync_MissingWorksheet_ReturnsSameEmptyShapeAsForbidden()
    {
        // "Yok" ile "yetkisiz" aynı yanıtı verir: id taramasıyla fark edilemez.
        await using var ctx = _db.NewContext();
        var view = await NewAssignments(ctx).GetWorksheetAssignmentsForTeacherAsync(987_654, SchoolScope.For(Stranger, schoolId: null));

        view.WorksheetName.ShouldBe(string.Empty);
        view.WorksheetKind.ShouldBe(WorksheetKind.Test);
        view.Assignments.ShouldBeEmpty();
    }
}
