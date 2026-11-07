using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #848: test başlangıç sayfası öğrenciye kimin atadığını ve son tarihi söyler — yalnız öğrencinin AKTİF ataması
/// (başlatma kuralıyla aynı görünürlük ve zaman penceresi); öğrenciye özel atama sınıf atamasından önce gelir.
/// </summary>
public class WorksheetDetailServiceStudentAssignmentTests : IDisposable
{
    private const int TeacherUserId = 7100;
    private const int OtherTeacherUserId = 7101;
    private const int StudentUserId = 7200;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public WorksheetDetailServiceStudentAssignmentTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>()).Returns(new List<UserLookupResultDto>
        {
            new() { Id = TeacherUserId, FullName = "Ayşe Öğretmen" },
            new() { Id = OtherTeacherUserId, FullName = "Can Öğretmen" },
        });
    }

    public void Dispose() => _db.Dispose();

    private async Task<(int WorksheetId, int StudentId, int GradeId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var student = new Student { UserId = StudentUserId, StudentNumber = "S7200", SchoolName = "Sch", GradeId = grade.Id };
        var ws = new Worksheet { Name = "Sözcükte Anlam", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        ctx.AddRange(student, ws);
        await ctx.SaveChangesAsync();
        return (ws.Id, student.Id, grade.Id);
    }

    private async Task AssignAsync(int worksheetId, int teacherUserId, DateTime? endAt, int? studentId = null, int? gradeId = null)
    {
        await using var ctx = _db.NewContext();
        var a = new WorksheetAssignment
        {
            WorksheetId = worksheetId,
            StudentId = studentId,
            GradeId = gradeId,
            IsPlatformWide = gradeId.HasValue,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = endAt,
        };
        ctx.WorksheetAssignments.Add(a);
        await ctx.SaveChangesAsync();
        a.CreateUserId = teacherUserId;
        await ctx.SaveChangesAsync();
    }

    private async Task<WorksheetDetailDto?> DetailAsync(int worksheetId, int studentId)
    {
        await using var ctx = _db.NewContext();
        return await new WorksheetDetailService(ctx, _authApi).GetWorksheetDetailAsync(worksheetId, "Student", studentId, StudentUserId);
    }

    [Fact]
    public async Task ActiveStudentAssignment_ReturnsTeacherNameAndDueDate()
    {
        var (worksheetId, studentId, _) = await SeedAsync();
        var due = DateTime.UtcNow.AddDays(3);
        await AssignAsync(worksheetId, TeacherUserId, due, studentId: studentId);

        var dto = await DetailAsync(worksheetId, studentId);

        dto!.StudentAssignment.ShouldNotBeNull();
        dto.StudentAssignment!.AssignedByName.ShouldBe("Ayşe Öğretmen");
        dto.StudentAssignment.DueAtUtc!.Value.ShouldBe(due, TimeSpan.FromSeconds(1));
        dto.StudentAssignment.DueAtUtc.Value.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task StudentScopedAssignment_WinsOverGradeAssignment()
    {
        var (worksheetId, studentId, gradeId) = await SeedAsync();
        await AssignAsync(worksheetId, OtherTeacherUserId, DateTime.UtcNow.AddDays(1), gradeId: gradeId);
        await AssignAsync(worksheetId, TeacherUserId, DateTime.UtcNow.AddDays(5), studentId: studentId);

        var dto = await DetailAsync(worksheetId, studentId);

        dto!.StudentAssignment!.AssignedByName.ShouldBe("Ayşe Öğretmen");
    }

    [Fact]
    public async Task ExpiredOrMissingAssignment_ReturnsNull()
    {
        var (worksheetId, studentId, _) = await SeedAsync();
        (await DetailAsync(worksheetId, studentId))!.StudentAssignment.ShouldBeNull();

        await AssignAsync(worksheetId, TeacherUserId, DateTime.UtcNow.AddHours(-1), studentId: studentId);
        (await DetailAsync(worksheetId, studentId))!.StudentAssignment.ShouldBeNull();
    }

    [Fact]
    public async Task OpenEndedAssignment_HasNoDueDate_AndNameFailureDoesNotBreakPage()
    {
        var (worksheetId, studentId, _) = await SeedAsync();
        await AssignAsync(worksheetId, TeacherUserId, endAt: null, studentId: studentId);
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>())
            .Returns<Task<IReadOnlyList<UserLookupResultDto>>>(_ => throw new HttpRequestException("down"));

        var dto = await DetailAsync(worksheetId, studentId);

        dto!.StudentAssignment.ShouldNotBeNull();
        dto.StudentAssignment!.DueAtUtc.ShouldBeNull();
        dto.StudentAssignment.AssignedByName.ShouldBeNull();
    }
}
