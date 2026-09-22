using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #191 — atama izni talebi (issue #13) SchoolOnly ile kesişimi (kapsam: yalnızca mevcut
/// kuralların bozulmaması): aynı okul zaten atayabildiği için talep açılmaz ("alreadyAssignable");
/// farklı okul / okulsuz için worksheet görünmez → NotFound. Talep akışı SchoolOnly için hiç başlamaz.
/// </summary>
public class WorksheetAccessRequestServiceSchoolOnlyTests : IDisposable
{
    private const int OwnerA = 10;
    private const int PeerA = 20;
    private const int OtherB = 30;
    private const int Independent = 40;

    private readonly TestDb _db = TestDb.Create();

    private WorksheetAccessRequestService NewService(AppDbContext ctx) =>
        new(ctx, Substitute.For<IAuthApiClient>(), NullLogger<WorksheetAccessRequestService>.Instance);

    private async Task<int> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        ctx.Teachers.AddRange(
            new Teacher { UserId = OwnerA, SchoolId = schoolA.Id },
            new Teacher { UserId = PeerA, SchoolId = schoolA.Id },
            new Teacher { UserId = OtherB, SchoolId = schoolB.Id },
            new Teacher { UserId = Independent, SchoolId = null, IsIndependentTutor = true });

        var ws = new Worksheet { Name = "W", Description = "d", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = OwnerA;
        ws.TeacherSharing = WorksheetTeacherSharing.SchoolOnly;
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    [Fact]
    public async Task SameSchoolTeacher_SchoolOnly_AlreadyAssignable_NoRequestCreated()
    {
        var id = await SeedAsync();

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).CreateRequestAsync(id, null, PeerA, "kc-peer", isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeFalse();
        r.Message.ShouldBe(FallbackMessageLocalizer.Instance["worksheets.accessRequest.alreadyAssignable"].Value);
        (await ctx.WorksheetAccessRequests.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(OtherB)]
    [InlineData(Independent)]
    public async Task DifferentSchoolOrIndependent_SchoolOnly_NotFound(int requester)
    {
        var id = await SeedAsync();

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).CreateRequestAsync(id, null, requester, "kc-x", isAdmin: false);

        r.Success.ShouldBeFalse();
        r.NotFound.ShouldBeTrue();
        (await ctx.WorksheetAccessRequests.CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
