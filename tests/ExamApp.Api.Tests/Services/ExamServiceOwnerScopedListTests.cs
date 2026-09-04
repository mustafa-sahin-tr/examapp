using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #4 — GetLatestWorksheetsAsync / GetPopularWorksheetsAsync artık opsiyonel
/// <c>ownerUserId</c> alıyor: dolu ise yalnız o kullanıcının worksheet'leri, null ise hepsi.
/// </summary>
public class ExamServiceOwnerScopedListTests : IDisposable
{
    private const int UserA = 10;
    private const int UserB = 20;

    private readonly TestDb _db = TestDb.Create();
    private ExamService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), Substitute.For<IAuthApiClient>());

    private sealed record World(int GradeId, int WsA, int WsB);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var wsA = new Worksheet { Name = "Alfa", Description = "d", GradeId = grade.Id };
        var wsB = new Worksheet { Name = "Beta", Description = "d", GradeId = grade.Id };
        ctx.AddRange(wsA, wsB);
        await ctx.SaveChangesAsync();
        wsA.CreateUserId = UserA;
        wsB.CreateUserId = UserB;
        await ctx.SaveChangesAsync();
        return new World(grade.Id, wsA.Id, wsB.Id);
    }

    private async Task AddInstanceAsync(int worksheetId, int gradeId)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = Random.Shared.Next(1000, 9999), StudentNumber = "s", SchoolName = "x", GradeId = gradeId };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        ctx.TestInstances.Add(new WorksheetInstance
        {
            WorksheetId = worksheetId, StudentId = student.Id,
            StartTime = DateTime.UtcNow.AddDays(-1), Status = WorksheetInstanceStatus.Completed,
        });
        await ctx.SaveChangesAsync();
    }

    // ---- GetLatestWorksheetsAsync ----

    [Fact]
    public async Task GetLatestWorksheetsAsync_WithOwnerUserId_ReturnsOnlyThatOwnersWorksheets()
    {
        var w = await SeedAsync();

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetLatestWorksheetsAsync(1, 50, ownerUserId: UserA);

        list.Select(i => i.Id).ShouldBe(new[] { w.WsA });
    }

    [Fact]
    public async Task GetLatestWorksheetsAsync_NullOwnerUserId_ReturnsAll()
    {
        var w = await SeedAsync();

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetLatestWorksheetsAsync(1, 50, ownerUserId: null);

        list.Select(i => i.Id).ShouldBe(new[] { w.WsA, w.WsB }, ignoreOrder: true);
    }

    // ---- GetPopularWorksheetsAsync ----

    [Fact]
    public async Task GetPopularWorksheetsAsync_WithOwnerUserId_ReturnsOnlyThatOwnersWorksheets()
    {
        var w = await SeedAsync();
        await AddInstanceAsync(w.WsA, w.GradeId);
        await AddInstanceAsync(w.WsB, w.GradeId);

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetPopularWorksheetsAsync(null, 1, 50, sinceDays: 30, ownerUserId: UserA);

        list.Select(i => i.Id).ShouldBe(new[] { w.WsA });
    }

    [Fact]
    public async Task GetPopularWorksheetsAsync_NullOwnerUserId_ReturnsAll()
    {
        var w = await SeedAsync();
        await AddInstanceAsync(w.WsA, w.GradeId);
        await AddInstanceAsync(w.WsB, w.GradeId);

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetPopularWorksheetsAsync(null, 1, 50, sinceDays: 30, ownerUserId: null);

        list.Select(i => i.Id).ShouldBe(new[] { w.WsA, w.WsB }, ignoreOrder: true);
    }

    public void Dispose() => _db.Dispose();
}
