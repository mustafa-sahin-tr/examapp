using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #222 (ürün kararı A): bağımsız (okulsuz) öğretmen sınıf bazlı atama yapamaz, yalnızca Approved Booking'i olan
/// öğrenciye öğrenci bazlı atar. Okullu öğretmen ve admin davranışı değişmez. Teacher view (assignments/overview)
/// karar A öncesi yazılmış SchoolId=null grade atamasını bağımsız istek sahibi için öğrencilere genişletmez ve kapsam
/// dışı direkt-öğrenciyi göstermez.
/// </summary>
public class WorksheetAssignmentServiceIndependentGradeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    private const int SchoolTeacherUserId = 1;
    private const int TutorUserId = 2;
    private const int AdminUserId = 99;
    private const string IndependentGradeForbiddenTr =
        "Bağımsız öğretmenler sınıf bazlı atama yapamaz; yalnızca randevusu olan öğrencilerinizi seçerek atayabilirsiniz.";

    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _db.Dispose();

    private sealed record Seed(
        int GradeId, int SchoolA, int SchoolB,
        int TutorWs, int SchoolWs,
        int StudentA, int StudentB, int StudentBooked);

    /// <summary>
    /// Okul A'da okullu öğretmen (SchoolWs sahibi), bağımsız tutor (TutorWs sahibi). Aynı sınıfta: studentA (okul A),
    /// studentB (okul B), studentBooked (okul B, tutor ile Approved Booking).
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();

        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var schoolTeacher = new Teacher { UserId = SchoolTeacherUserId, SchoolId = schoolA.Id };
        var tutor = new Teacher { UserId = TutorUserId, SchoolId = null, IsIndependentTutor = true };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        var studentBooked = new Student { UserId = 12, StudentNumber = "k", SchoolId = schoolB.Id, GradeId = grade.Id };
        ctx.AddRange(schoolTeacher, tutor, studentA, studentB, studentBooked);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(TutorUserId);
        var tutorWs = new Worksheet { Name = "Tutor WS", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(tutorWs);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(SchoolTeacherUserId);
        var schoolWs = new Worksheet { Name = "Okul WS", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(schoolWs);
        await ctx.SaveChangesAsync();

        BookingSeed.Add(ctx, tutor.Id, studentBooked.Id, BookingStatus.Approved, 8);
        await ctx.SaveChangesAsync();

        return new Seed(grade.Id, schoolA.Id, schoolB.Id, tutorWs.Id, schoolWs.Id, studentA.Id, studentB.Id, studentBooked.Id);
    }

    private static WorksheetAssignmentRequestDto GradeReq(int wsId, int gradeId) => new()
    {
        WorksheetId = wsId, GradeId = gradeId, StartAt = Start,
    };

    private static WorksheetAssignmentRequestDto StudentReq(int wsId, int studentId) => new()
    {
        WorksheetId = wsId, StudentId = studentId, StartAt = Start,
    };

    // ---- (a) bağımsız öğretmen grade ataması reddedilir ----

    [Fact]
    public async Task Independent_teacher_grade_assignment_is_rejected_and_nothing_is_written()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(seed.TutorWs, seed.GradeId), SchoolScope.For(TutorUserId, null));

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(IndependentGradeForbiddenTr);
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    // ---- security Ö2: scope okulu öğretmen kaydıyla doğrulanır ----

    [Fact]
    public async Task Multi_role_account_whose_scope_school_comes_from_student_profile_cannot_grade_assign_as_independent_teacher()
    {
        // Profil rolü Student çıkan çok rollü hesap: GetSchoolScopeAsync okulu Students tablosundan çözebilir (okul B),
        // ama öğretmen kaydı okulsuz. Scope'a güvenilseydi grade ataması okul B'ye yazılırdı → fail-closed red.
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(seed.TutorWs, seed.GradeId), SchoolScope.For(TutorUserId, seed.SchoolB));

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Requester_without_teacher_record_cannot_assign()
    {
        var seed = await SeedAsync();
        const int noTeacherUserId = 4242;
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(noTeacherUserId);
            setup.Worksheets.Add(new Worksheet { Name = "Kayıtsız", Description = "", GradeId = seed.GradeId });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var wsId = await ctx.Worksheets.Where(w => w.CreateUserId == noTeacherUserId).Select(w => w.Id).SingleAsync();
        var r = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(wsId, seed.GradeId), SchoolScope.For(noTeacherUserId, seed.SchoolA));

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Independent_teacher_grade_request_on_invisible_worksheet_still_looks_like_not_found()
    {
        // Guard worksheet erişim kontrolünden SONRA: başkasının Private worksheet'i için "bulunamadı" oracle'ı korunur.
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(seed.SchoolWs, seed.GradeId), SchoolScope.For(TutorUserId, null));
        var missing = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(999999, seed.GradeId), SchoolScope.For(TutorUserId, null));

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(missing.Message);
    }

    [Theory]
    [InlineData(false, false)] // issue #277 (madde 7): SchoolId=null tek başına artık "herkese açık" DEĞİL (fail-closed)
    [InlineData(true, true)]   // yalnızca açıkça platform geneli işaretli satır tüm okullara açık
    public async Task Issue277_NullSchoolGradeAssignment_IsVisibleAcrossSchoolsOnlyWhenExplicitlyPlatformWide(
        bool isPlatformWide, bool expectVisible)
    {
        // issue #236: bağımsız öğretmenin #222 öncesi SchoolId=null sınıf atamaları migration ile soft-delete edildi.
        // issue #277 (madde 7): predikat daraltıldı — SchoolId=null ve IsPlatformWide=false satır (ör. migration'ın
        // kaçırdığı/elle yazılmış legacy satır) başka okulun öğrencisine SIZMAZ. Mevcut null satırlar
        // AddWorksheetAssignmentIsPlatformWide migration'ında IsPlatformWide=true'ya çekildi (davranış korunur).
        var seed = await SeedAsync();
        var activeStart = DateTime.UtcNow.AddDays(-1);
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(TutorUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = seed.TutorWs, GradeId = seed.GradeId, SchoolId = null, IsPlatformWide = isPlatformWide,
                StartAt = activeStart,
            });
            await setup.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var forStudentB = await NewService(read).GetActiveAssignmentsForStudentAsync(
            new StudentProfileDto { Id = seed.StudentB, GradeId = seed.GradeId, SchoolId = seed.SchoolB });

        if (expectVisible)
            forStudentB.ShouldHaveSingleItem().WorksheetId.ShouldBe(seed.TutorWs);
        else
            forStudentB.ShouldBeEmpty();
    }

    // ---- (b) okullu öğretmen grade ataması değişmedi ----

    [Fact]
    public async Task School_teacher_grade_assignment_succeeds_and_is_scoped_to_the_teachers_school()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(seed.SchoolWs, seed.GradeId), SchoolScope.For(SchoolTeacherUserId, seed.SchoolA));

        r.Success.ShouldBeTrue();
        var assignment = await ctx.WorksheetAssignments.SingleAsync();
        assignment.GradeId.ShouldBe(seed.GradeId);
        assignment.SchoolId.ShouldBe(seed.SchoolA);
    }

    // ---- (c) admin grade ataması değişmedi ----

    [Fact]
    public async Task Admin_grade_assignment_succeeds_platform_wide()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            GradeReq(seed.TutorWs, seed.GradeId), SchoolScope.Unrestricted(AdminUserId));

        r.Success.ShouldBeTrue();
        var assignment = await ctx.WorksheetAssignments.SingleAsync();
        assignment.GradeId.ShouldBe(seed.GradeId);
        assignment.SchoolId.ShouldBeNull();
    }

    // ---- (d) bağımsız öğretmen Approved Booking öğrencisine atayabilir ----

    [Fact]
    public async Task Independent_teacher_can_assign_to_student_with_approved_booking()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            StudentReq(seed.TutorWs, seed.StudentBooked), SchoolScope.For(TutorUserId, null));

        r.Success.ShouldBeTrue();
        var assignment = await ctx.WorksheetAssignments.SingleAsync();
        assignment.StudentId.ShouldBe(seed.StudentBooked);
        assignment.GradeId.ShouldBeNull();
        assignment.SchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Independent_teacher_cannot_assign_to_student_without_booking()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(
            StudentReq(seed.TutorWs, seed.StudentB), SchoolScope.For(TutorUserId, null));

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    // ---- (f) teacher view: legacy grade + kapsam dışı direkt öğrenci ----

    [Fact]
    public async Task Teacher_view_for_independent_teacher_hides_legacy_grade_expansion_and_out_of_scope_direct_students()
    {
        var seed = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(TutorUserId);
            setup.WorksheetAssignments.AddRange(
                // Karar A öncesi yazılmış legacy grade ataması (SchoolId=null → eskiden tüm okullara genişliyordu).
                new WorksheetAssignment { WorksheetId = seed.TutorWs, GradeId = seed.GradeId, StartAt = Start },
                // Kapsam dışı direkt öğrenci (booking yok) — legacy veri.
                new WorksheetAssignment { WorksheetId = seed.TutorWs, StudentId = seed.StudentA, StartAt = Start.AddDays(1) },
                // Kapsam içi direkt öğrenci (Approved Booking).
                new WorksheetAssignment { WorksheetId = seed.TutorWs, StudentId = seed.StudentBooked, StartAt = Start.AddDays(2) });
            setup.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = seed.TutorWs, StudentId = seed.StudentA, StartTime = Start.AddDays(1).AddHours(1),
                Status = WorksheetInstanceStatus.Completed,
            });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(seed.TutorWs, SchoolScope.For(TutorUserId, null));

        overview.Assignments.Count.ShouldBe(3);
        var listedStudentIds = overview.Assignments.SelectMany(a => a.Students).Select(s => s.StudentId).ToList();
        listedStudentIds.ShouldBe(new[] { seed.StudentBooked });
        overview.Summary.TotalStudents.ShouldBe(1);
        overview.Summary.CompletedCount.ShouldBe(0); // kapsam dışı studentA'nın instance'ı hiçbir sayaca girmez
    }

    [Fact]
    public async Task Teacher_view_for_school_teacher_still_expands_grade_assignment_to_own_school()
    {
        var seed = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(SchoolTeacherUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = seed.SchoolWs, GradeId = seed.GradeId, SchoolId = seed.SchoolA, StartAt = Start,
            });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(
            seed.SchoolWs, SchoolScope.For(SchoolTeacherUserId, seed.SchoolA));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.Students.Select(s => s.StudentId).ShouldBe(new[] { seed.StudentA });
    }
}
