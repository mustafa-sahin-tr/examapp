using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// GitHub issue #191 — WorksheetDetailService.GetWorksheetDetailAsync, TeacherSharing=SchoolOnly:
/// aynı okul görür (CanAssign=true, CanEdit=false), farklı okul / okulsuz görmez (null → 404), admin görür,
/// öğrenci akışı etkilenmez.
/// </summary>
public class WorksheetDetailServiceSchoolOnlyTests : IDisposable
{
    private const int OwnerA = 6000;
    private const int PeerA = 6001;
    private const int OtherB = 6002;
    private const int Independent = 6003;
    private const int Admin = 6999;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public WorksheetDetailServiceSchoolOnlyTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = OwnerA, FullName = "Ada Öğretmen" },
        });
    }

    private WorksheetDetailService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private async Task<(int worksheetId, int studentId)> SeedAsync(WorksheetTeacherSharing sharing, int? ownerUserId = OwnerA)
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
        var student = new Student { UserId = 7000, StudentNumber = "s1", GradeId = grade.Id, SchoolId = schoolB.Id };
        ctx.Students.Add(student);

        var ws = new Worksheet { Name = "Test", Description = "d", GradeId = grade.Id, MaxDurationSeconds = 600 };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        ws.CreateUserId = ownerUserId;
        ws.TeacherSharing = sharing;
        await ctx.SaveChangesAsync();
        return (ws.Id, student.Id);
    }

    [Fact]
    public async Task SameSchoolTeacher_SeesDetail_CanAssignTrue_CanEditFalse()
    {
        var (id, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, PeerA);

        dto.ShouldNotBeNull();
        dto!.Worksheet.CanAssign.ShouldBeTrue();
        dto.Worksheet.CanEdit.ShouldBeFalse();
        dto.Worksheet.IsOwner.ShouldBeFalse();
        dto.Worksheet.OwnerName.ShouldBe("Ada Öğretmen");
        dto.Worksheet.TeacherSharing.ShouldBe(WorksheetTeacherSharing.SchoolOnly);
        dto.TeacherInsights.ShouldBeNull(); // sahiplik gerektirir, paylaşımla gelmez
    }

    [Theory]
    [InlineData(OtherB)]
    [InlineData(Independent)]
    [InlineData(8888)] // Teacher satırı hiç yok → okulsuz sayılır
    public async Task DifferentSchoolOrIndependent_ReturnsNull(int requester)
    {
        var (id, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, requester)).ShouldBeNull();
    }

    [Fact]
    public async Task Admin_SeesDetail()
    {
        var (id, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, Admin, isAdmin: true);

        dto.ShouldNotBeNull();
        dto!.Worksheet.CanAssign.ShouldBeTrue();
        dto.Worksheet.CanEdit.ShouldBeTrue();
    }

    [Fact]
    public async Task Owner_SeesDetail_WithInsights()
    {
        var (id, _) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OwnerA);

        dto.ShouldNotBeNull();
        dto!.Worksheet.IsOwner.ShouldBeTrue();
        dto.Worksheet.CanEdit.ShouldBeTrue();
        dto.TeacherInsights.ShouldNotBeNull();
    }

    [Fact]
    public async Task Student_FromAnySchool_StillSeesDetail()
    {
        var (id, studentId) = await SeedAsync(WorksheetTeacherSharing.SchoolOnly);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Student", studentId, 7000);

        dto.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView, false)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable, true)]
    public async Task Regression_PublicSharing_VisibleToDifferentSchoolTeacher(WorksheetTeacherSharing sharing, bool expectedCanAssign)
    {
        var (id, _) = await SeedAsync(sharing);

        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).GetWorksheetDetailAsync(id, "Teacher", null, OtherB);

        dto.ShouldNotBeNull();
        dto!.Worksheet.CanAssign.ShouldBe(expectedCanAssign);
    }

    public void Dispose() => _db.Dispose();
}
