using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.DirectMessages;
using ExamApp.Api.Services;
using ExamApp.Api.Services.DirectMessages;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Leaderboards;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #361 (4b): doğrulanmamış (öğrencinin kendi seçtiği, <c>SchoolVerifiedAt</c> null) okul üyeliği HİÇBİR okul kapsamlı
/// yetki vermez. Aynı okul/sınıfta iki öğrenci: <c>Verified</c> (kontrol) ve <c>Pending</c>. Her temsilî yolda doğrulanmış
/// öğrenci okul kapsamını alır, beklemedeki almaz: tek okul tanımı (UserSchoolResolver → profil/SchoolScope), okul+sınıf
/// hedefli atama (liste ve test başlatma), #106 aynı okul DM yolu (B), okul liderlik tablosu, öğretmenin okul öğrenci
/// kapsamı (ApplyScope).
/// </summary>
public class StudentSchoolVerificationAuthorityTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private const int TeacherUserId = 1;
    private const int VerifiedUserId = 10;
    private const int PendingUserId = 11;
    private static readonly DateTime Now = DateTime.UtcNow;

    private sealed record World(int SchoolId, int GradeId, int WorksheetId, int VerifiedStudentId, int PendingStudentId, int TeacherId);

    public StudentSchoolVerificationAuthorityTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(
                ((IEnumerable<int>)call[0]).Select(id => new UserLookupResultDto { Id = id, FullName = $"User {id}" }).ToList()));
    }

    public void Dispose() => _db.Dispose();

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Okul A" };
        var grade = new Grade { Name = "8" };
        ctx.AddRange(school, grade);
        await ctx.SaveChangesAsync();

        var teacher = new Teacher { UserId = TeacherUserId, SchoolId = school.Id, AccountApprovedAt = Now.AddDays(-30) };
        var verified = new Student
        {
            UserId = VerifiedUserId, StudentNumber = "V", GradeId = grade.Id, SchoolId = school.Id, SchoolVerifiedAt = Now.AddDays(-1)
        };
        // Öğrencinin kendi kaydında seçtiği okul: SchoolId dolu, SchoolVerifiedAt null.
        var pending = new Student { UserId = PendingUserId, StudentNumber = "P", GradeId = grade.Id, SchoolId = school.Id };
        ctx.AddRange(teacher, verified, pending);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(TeacherUserId);
        var worksheet = new Worksheet
        {
            Name = "Okul sınavı", Description = "", GradeId = grade.Id, StudentVisibility = WorksheetStudentVisibility.Restricted
        };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        // Okul + sınıf hedefli atama (okul kapsamlı); Restricted → keşfet yolu yok, yalnız atama görünürlük verir.
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheet.Id, GradeId = grade.Id, SchoolId = school.Id, StartAt = Now.AddDays(-1), EndAt = Now.AddDays(7)
        });
        ctx.StudentPoints.AddRange(
            new StudentPoint { StudentId = verified.Id, XP = 100 },
            new StudentPoint { StudentId = pending.Id, XP = 900 });
        await ctx.SaveChangesAsync();

        return new World(school.Id, grade.Id, worksheet.Id, verified.Id, pending.Id, teacher.Id);
    }

    // ---- Tek okul tanımı (profil SchoolId → SchoolScope, #326 D3) ----

    [Fact]
    public async Task UserSchoolResolver_PendingStudent_IsSchoolless_VerifiedStudent_HasSchool()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var schools = await UserSchoolResolver.ResolveManyAsync(ctx, new[] { VerifiedUserId, PendingUserId });

        schools[VerifiedUserId].ShouldBe(w.SchoolId);
        schools[PendingUserId].ShouldBeNull();
    }

    [Fact]
    public async Task SchoolContextResolver_PendingStudent_ProfileSchoolIdIsNull()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var resolver = new SchoolContextResolver(ctx);

        (await resolver.ResolveSchoolIdAsync(new UserProfileDto { Id = PendingUserId, Role = "Student" })).ShouldBeNull();
        (await resolver.ResolveSchoolIdAsync(new UserProfileDto { Id = VerifiedUserId, Role = "Student" })).ShouldBe(w.SchoolId);
    }

    // ---- Okul + sınıf hedefli atamalar (#236) ----

    /// <summary>
    /// <c>StudentService.GetStudentProfile</c>'ın okul eşlemesi (SchoolId = doğrulanmış okul) — o sorgu SQLite'ta çevrilemiyor
    /// (APPLY), bu yüzden eşleme <see cref="Student.VerifiedSchoolId"/> ile kurulur; profil sorgusunun kendisi Postgres'te
    /// <c>StudentSchoolVerificationIntegrationTests</c>'te doğrulanır.
    /// </summary>
    private static async Task<StudentProfileDto> ProfileAsync(AppDbContext ctx, int userId)
    {
        var st = await ctx.Students.AsNoTracking().SingleAsync(s => s.UserId == userId);
        return new StudentProfileDto { Id = st.Id, GradeId = st.GradeId, SchoolId = st.VerifiedSchoolId };
    }

    [Fact]
    public async Task SchoolGradeAssignment_VisibleToVerified_NotToPending()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var assignments = new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx));

        var verifiedList = await assignments.GetActiveAssignmentsForStudentAsync(await ProfileAsync(ctx, VerifiedUserId));
        var pendingList = await assignments.GetActiveAssignmentsForStudentAsync(await ProfileAsync(ctx, PendingUserId));

        verifiedList.Select(a => a.WorksheetId).ShouldContain(w.WorksheetId);
        pendingList.ShouldBeEmpty();
    }

    [Fact]
    public async Task SchoolGradeAssignment_PendingStudentCannotStartRestrictedTest()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var pendingProfile = await ProfileAsync(ctx, PendingUserId);
        await Should.ThrowAsync<UnauthorizedAccessException>(() => new TestSessionService(ctx).StartTestAsync(w.WorksheetId, pendingProfile));

        await using var ctx2 = _db.NewContext();
        var verifiedResult = await new TestSessionService(ctx2).StartTestAsync(w.WorksheetId, await ProfileAsync(ctx2, VerifiedUserId));
        verifiedResult.Success.ShouldBeTrue(verifiedResult.Message);
    }

    // ---- #106 aynı okul DM yolu (B) ----

    [Fact]
    public async Task DirectMessages_SameSchoolPath_OpenForVerified_ClosedForPending()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var policy = new DirectMessagePolicy(ctx,
            options: Options.Create(new DirectMessagingOptions { AllowSameSchoolMessaging = true }));

        var verified = (await policy.ResolveStudentAsync(VerifiedUserId))!;
        var pending = (await policy.ResolveStudentAsync(PendingUserId))!;

        // Atama yolu (A) de var — B yolunu ayırt etmek için öğretmenin atamasını kapatıyoruz: yalnız okul eşitliği kalsın.
        await ctx.WorksheetAssignments.ExecuteDeleteAsync();

        var verifiedTeachers = await policy.RelatedTeachers(verified, DateTime.UtcNow).Select(r => r.TeacherUserId).ToListAsync();
        var pendingTeachers = await policy.RelatedTeachers(pending, DateTime.UtcNow).Select(r => r.TeacherUserId).ToListAsync();

        pending.SchoolId.ShouldBeNull();
        verifiedTeachers.ShouldContain(TeacherUserId);
        pendingTeachers.ShouldNotContain(TeacherUserId);
    }

    // ---- Okul liderlik tablosu (#193) ----

    [Fact]
    public async Task SchoolLeaderboard_ExcludesPendingStudent()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = new LeaderboardService(ctx, _authApi, Substitute.For<ILogger<LeaderboardService>>());

        var dto = await service.GetLeaderboardAsync(new LeaderboardRequest(LeaderboardScope.School, VerifiedUserId, w.SchoolId, 0, 20));

        dto.Success.ShouldBeTrue();
        dto.TotalCount.ShouldBe(1);
        dto.Entries.Select(e => e.FullName).ShouldBe(new[] { $"User {VerifiedUserId}" });
    }

    [Fact]
    public async Task SchoolLeaderboard_PendingRequester_HasNoSchoolScope()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        // Controller okul kapsamını profilden (UserSchoolResolver) alır — beklemedeki öğrenci için null.
        var requesterSchool = await UserSchoolResolver.ResolveAsync(ctx, PendingUserId);
        var service = new LeaderboardService(ctx, _authApi, Substitute.For<ILogger<LeaderboardService>>());

        var dto = await service.GetLeaderboardAsync(new LeaderboardRequest(LeaderboardScope.School, PendingUserId, requesterSchool, 0, 20));

        requesterSchool.ShouldBeNull();
        dto.Success.ShouldBeFalse();
        dto.SchoolScopeAvailable.ShouldBeFalse();
    }

    // ---- Öğretmenin okul öğrenci kapsamı (#190) ----

    [Fact]
    public async Task SchoolTeacherScope_DoesNotIncludePendingStudent()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var policy = new SchoolAccessPolicy(ctx);
        var teacherScope = SchoolScope.For(TeacherUserId, w.SchoolId);

        var visible = await policy.ApplyScope(ctx.Students.AsNoTracking(), teacherScope).Select(s => s.Id).ToListAsync();

        visible.ShouldBe(new[] { w.VerifiedStudentId });
        (await policy.CanAccessStudentAsync(teacherScope, w.PendingStudentId)).ShouldBeFalse();
        (await policy.CanAccessStudentAsync(teacherScope, w.VerifiedStudentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task SchoolTeacher_CannotAssignDirectlyToPendingStudent()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = new WorksheetAssignmentService(ctx, new SchoolAccessPolicy(ctx));

        var result = await service.AssignWorksheetAsync(
            new WorksheetAssignmentRequestDto { WorksheetId = w.WorksheetId, StudentId = w.PendingStudentId, StartAt = Now },
            SchoolScope.For(TeacherUserId, w.SchoolId));

        result.Success.ShouldBeFalse();
    }

    // ---- Kendi kaydı → beklemede ----

    [Fact]
    public async Task SelfRegister_WithSchool_StartsPending()
    {
        await using (var seed = _db.NewContext())
        {
            seed.Schools.Add(new School { Name = "Okul X" });
            seed.Grades.Add(new Grade { Name = "7" });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var schoolId = await ctx.Schools.Select(s => s.Id).SingleAsync();
        var gradeId = await ctx.Grades.Select(g => g.Id).SingleAsync();
        var service = new StudentService(ctx, _authApi, new SchoolAccessPolicy(ctx));

        var result = await service.Save(500, new RegisterStudentDto { StudentNumber = "123", SchoolId = schoolId, GradeId = gradeId });

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var row = await check.Students.SingleAsync(s => s.UserId == 500);
        row.SchoolId.ShouldBe(schoolId);
        row.SchoolVerifiedAt.ShouldBeNull();
        row.SchoolVerifiedByUserId.ShouldBeNull();
        (await UserSchoolResolver.ResolveAsync(check, 500)).ShouldBeNull();
    }

    [Fact]
    public async Task SelfRegister_ExistingSchoollessStudent_FirstSchoolChoice_StartsPending()
    {
        int schoolId, gradeId;
        await using (var seed = _db.NewContext())
        {
            var school = new School { Name = "Okul Y" };
            var grade = new Grade { Name = "7" };
            seed.AddRange(school, grade);
            seed.Students.Add(new Student { UserId = 501, StudentNumber = "1" });
            await seed.SaveChangesAsync();
            schoolId = school.Id;
            gradeId = grade.Id;
        }

        await using var ctx = _db.NewContext();
        var result = await new StudentService(ctx, _authApi, new SchoolAccessPolicy(ctx))
            .Save(501, new RegisterStudentDto { StudentNumber = "1", SchoolId = schoolId, GradeId = gradeId });

        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var row = await check.Students.SingleAsync(s => s.UserId == 501);
        row.SchoolId.ShouldBe(schoolId);
        row.SchoolVerifiedAt.ShouldBeNull();
    }
}
