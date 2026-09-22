using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #191 — WorksheetAssignmentService.AssignWorksheetAsync, TeacherSharing=SchoolOnly:
/// aynı okuldaki öğretmen kendi okulunun öğrencisine atar (PublicAssignable ile aynı); farklı okul ve
/// okulsuz öğretmen için worksheet "bulunamadı" (varlık sızmaz — mesaj olmayan id ile aynı); admin atar.
/// </summary>
public class WorksheetAssignmentServiceSchoolOnlyTests : IDisposable
{
    private const int OwnerA = 1;
    private const int PeerA = 2;
    private const int OtherB = 3;
    private const int Independent = 4;
    private const int Admin = 555;
    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy());

    public void Dispose() => _db.Dispose();

    private async Task<(int wsId, int studentA, int studentB)> SeedAsync(WorksheetTeacherSharing sharing)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerA);

        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id, TeacherSharing = sharing };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        ctx.AddRange(ws, studentA, studentB,
            new Teacher { UserId = OwnerA, SchoolId = schoolA.Id },
            new Teacher { UserId = PeerA, SchoolId = schoolA.Id },
            new Teacher { UserId = OtherB, SchoolId = schoolB.Id },
            new Teacher { UserId = Independent, SchoolId = null, IsIndependentTutor = true });
        await ctx.SaveChangesAsync();

        return (ws.Id, studentA.Id, studentB.Id);
    }

    private static WorksheetAssignmentRequestDto Req(int wsId, int studentId) => new()
    {
        WorksheetId = wsId, StudentId = studentId, StartAt = Start,
    };

    [Fact]
    public async Task SameSchoolTeacher_CanAssignSchoolOnlyToOwnSchoolStudent()
    {
        var (ws, studentA, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentA), PeerA, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(OtherB)]
    [InlineData(Independent)]
    public async Task DifferentSchoolOrIndependentTeacher_SchoolOnlyLooksLikeNotFound(int requester)
    {
        var (ws, _, studentB) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentB), requester, isAdmin: false);
        var missing = await NewService(ctx).AssignWorksheetAsync(Req(999999, studentB), requester, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(missing.Message); // oracle kapalı: farklı okul == olmayan worksheet
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Admin_CanAssignSchoolOnlyToAnySchool()
    {
        var (ws, _, studentB) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentB), Admin, isAdmin: true);

        r.Success.ShouldBeTrue(r.Message);
    }

    [Fact]
    public async Task Owner_CanAssignOwnSchoolOnly()
    {
        var (ws, studentA, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentA), OwnerA, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
    }

    [Fact]
    public async Task Regression_PublicAssignable_DifferentSchoolTeacherStillAssignsToOwnStudents()
    {
        var (ws, _, studentB) = await SeedAsync(WorksheetTeacherSharing.PublicAssignable);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentB), OtherB, isAdmin: false);

        r.Success.ShouldBeTrue(r.Message);
    }

    [Fact]
    public async Task DifferentSchoolTeacher_WithApprovedGrant_StillNotFound()
    {
        // Saf kural (WorksheetAccess.CanAssign) grant'i okuldan bağımsız kabul eder; servis ise önce
        // CanView kapısından geçer ve farklı okul için orada "bulunamadı" döner — grant'e hiç bakılmaz.
        var (ws, _, studentB) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using (var seed = _db.NewContext())
        {
            seed.SetCurrentUser(OwnerA);
            seed.WorksheetAccessGrants.Add(new WorksheetAccessGrant { WorksheetId = ws, TeacherUserId = OtherB, GrantedByUserId = OwnerA, GrantedAt = Start });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentB), OtherB, isAdmin: false);
        var missing = await NewService(ctx).AssignWorksheetAsync(Req(999999, studentB), OtherB, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(missing.Message);
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    // ---- Öğrenci tarafı: atanmış SchoolOnly sınav görünür ve başlatılabilir (TeacherSharing öğrenciyi etkilemez) ----

    [Fact]
    public async Task Student_AssignedSchoolOnlyWorksheet_AppearsInActiveAssignments()
    {
        var (ws, studentA, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(ws, studentA), PeerA, isAdmin: false)).Success.ShouldBeTrue();

        await using var read = _db.NewContext();
        var studentRow = await read.Students.SingleAsync(s => s.Id == studentA);
        var active = await NewService(read).GetActiveAssignmentsForStudentAsync(
            new StudentProfileDto { Id = studentA, GradeId = studentRow.GradeId, SchoolId = studentRow.SchoolId });

        active.ShouldHaveSingleItem().WorksheetId.ShouldBe(ws);
    }

    [Fact]
    public async Task Student_AssignedSchoolOnlyWorksheet_CanStartTest()
    {
        var (ws, studentA, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(ws, studentA), PeerA, isAdmin: false)).Success.ShouldBeTrue();

        await using var read = _db.NewContext();
        var studentRow = await read.Students.SingleAsync(s => s.Id == studentA);
        var result = await new TestSessionService(read).StartTestAsync(ws, new StudentProfileDto { Id = studentA, GradeId = studentRow.GradeId });

        result.ShouldNotBeNull();
        result!.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Regression_PublicView_SameSchoolTeacher_StillNeedsGrant()
    {
        // SchoolOnly eklenmesi PublicView'ı "aynı okulda atanabilir" yapmaz.
        var (ws, studentA, _) = await SeedAsync(WorksheetTeacherSharing.PublicView);
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentA), PeerA, isAdmin: false);

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }
}
