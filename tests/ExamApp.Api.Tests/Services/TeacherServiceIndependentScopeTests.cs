using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #222 savunma derinliği: karar A öncesi yazılmış SchoolId=null grade ataması olan bağımsız (okulsuz) öğretmenin
/// dashboard-summary / worksheets-overview / lagging-students görünümleri başka okulların öğrencilerini listelemez;
/// yalnızca Approved Booking'li direkt öğrenci hedefleri sayılır. Okullu öğretmen/admin davranışı değişmez.
/// </summary>
public class TeacherServiceIndependentScopeTests : IDisposable
{
    private const int TutorUserId = 2;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherServiceIndependentScopeTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>());
    }

    public void Dispose() => _db.Dispose();

    private TeacherService NewService(AppDbContext ctx) => new(ctx, _authApi, schoolAccessPolicy: new SchoolAccessPolicy(ctx));

    private static SchoolScope Tutor => SchoolScope.For(TutorUserId, null);

    private sealed record Seed(int WorksheetId, int StudentA, int StudentB, int StudentBooked);

    /// <summary>
    /// Tutor worksheet'i: legacy grade ataması (SchoolId=null) + kapsam dışı direkt atama (studentA, booking yok) +
    /// kapsam içi direkt atama (studentBooked, Approved Booking). Hepsi başlamış ve tamamlanmamış (geride kalan).
    /// studentA okul A, studentB okul B; üçü de aynı sınıfta.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();

        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var tutor = new Teacher { UserId = TutorUserId, SchoolId = null, IsIndependentTutor = true };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolName = "A", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolName = "B", SchoolId = schoolB.Id, GradeId = grade.Id };
        var studentBooked = new Student { UserId = 12, StudentNumber = "k", SchoolName = "B", SchoolId = schoolB.Id, GradeId = grade.Id };
        ctx.AddRange(tutor, studentA, studentB, studentBooked);
        await ctx.SaveChangesAsync();

        BookingSeed.Add(ctx, tutor.Id, studentBooked.Id, BookingStatus.Approved, 8);

        ctx.SetCurrentUser(TutorUserId);
        var ws = new Worksheet { Name = "Tutor WS", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();

        var startAt = DateTime.UtcNow.AddDays(-1);
        ctx.WorksheetAssignments.AddRange(
            new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = null, StartAt = startAt },
            new WorksheetAssignment { WorksheetId = ws.Id, StudentId = studentA.Id, StartAt = startAt },
            new WorksheetAssignment { WorksheetId = ws.Id, StudentId = studentBooked.Id, StartAt = startAt });
        await ctx.SaveChangesAsync();

        return new Seed(ws.Id, studentA.Id, studentB.Id, studentBooked.Id);
    }

    [Fact]
    public async Task Lagging_students_for_independent_teacher_lists_only_approved_booking_students()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetLaggingStudentsAsync(Tutor);

        result.Select(r => r.StudentId).ShouldBe(new[] { seed.StudentBooked });
    }

    [Fact]
    public async Task Dashboard_summary_for_independent_teacher_counts_only_approved_booking_students()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetDashboardSummaryAsync(Tutor);

        result.TotalWorksheets.ShouldBe(1);
        result.TotalUniqueStudents.ShouldBe(1);
    }

    [Fact]
    public async Task Worksheets_overview_for_independent_teacher_counts_only_approved_booking_students()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetWorksheetsOverviewAsync(Tutor);

        var row = result.ShouldHaveSingleItem();
        row.WorksheetId.ShouldBe(seed.WorksheetId);
        row.AssignedStudentCount.ShouldBe(1);
    }

    [Fact]
    public async Task Multi_role_scope_with_school_but_independent_teacher_record_gets_narrowest_view()
    {
        // security Ö2: scope okulu Students'tan gelmiş (okul B) ama öğretmen kaydı okulsuz → uyuşmazlık, en dar davranış.
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();
        var schoolB = await ctx.Students.Where(s => s.Id == seed.StudentB).Select(s => s.SchoolId).SingleAsync();
        var mixedScope = SchoolScope.For(TutorUserId, schoolB);

        var summary = await NewService(ctx).GetDashboardSummaryAsync(mixedScope);
        var lagging = await NewService(ctx).GetLaggingStudentsAsync(mixedScope);
        var overview = await NewService(ctx).GetWorksheetsOverviewAsync(mixedScope);

        summary.TotalUniqueStudents.ShouldBe(1);
        lagging.Select(r => r.StudentId).ShouldBe(new[] { seed.StudentBooked });
        overview.ShouldHaveSingleItem().AssignedStudentCount.ShouldBe(1);
    }

    [Fact]
    public async Task Same_legacy_data_is_expanded_for_an_unrestricted_requester_admin_behaviour_unchanged()
    {
        // Admin (Unrestricted) için davranış değişmez: legacy grade ataması sınıftaki tüm öğrencilere genişler.
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var summary = await NewService(ctx).GetDashboardSummaryAsync(SchoolScope.Unrestricted(TutorUserId));
        var lagging = await NewService(ctx).GetLaggingStudentsAsync(SchoolScope.Unrestricted(TutorUserId));

        summary.TotalUniqueStudents.ShouldBe(3);
        lagging.Count.ShouldBe(3);
    }
}
