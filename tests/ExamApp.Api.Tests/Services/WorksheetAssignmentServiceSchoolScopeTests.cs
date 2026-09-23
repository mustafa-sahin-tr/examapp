using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #190 (güvenlik incelemesi): worksheet SAHİBİ de başka okulun öğrencisine atama yapamaz;
/// red mesajı "öğrenci bulunamadı" ile aynıdır (var/yok oracle'ı kapalı). Aynı okul ve admin serbest.
/// assignments/overview: öğrenci listesi istek sahibinin okuluyla sınırlı.
/// issue #192: bağımsız (okulsuz) sahip yalnızca kendi Approved Booking'i olan öğrenciye atar / overview'da onları görür.
/// </summary>
public class WorksheetAssignmentServiceSchoolScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    private const int OwnerUserId = 1;
    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _db.Dispose();

    private async Task<(int wsId, int gradeId, int schoolA, int schoolB, int studentA, int studentB, int studentNone)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerUserId);

        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        var studentNone = new Student { UserId = 12, StudentNumber = "n", SchoolId = null, GradeId = grade.Id };
        var owner = new Teacher { UserId = OwnerUserId, SchoolId = schoolA.Id };
        ctx.AddRange(ws, studentA, studentB, studentNone, owner);
        await ctx.SaveChangesAsync();

        return (ws.Id, grade.Id, schoolA.Id, schoolB.Id, studentA.Id, studentB.Id, studentNone.Id);
    }

    private static WorksheetAssignmentRequestDto Req(int wsId, int studentId) => new()
    {
        WorksheetId = wsId, StudentId = studentId, StartAt = Start,
    };

    [Fact]
    public async Task Owner_CannotAssignToStudentOfDifferentSchool_LooksLikeNotFound()
    {
        var (ws, _, _, _, _, studentB, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentB), OwnerUserId, isAdmin: false);
        var missing = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, 99999), OwnerUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(missing.Message); // oracle kapalı: farklı okul == olmayan öğrenci
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Owner_CannotAssignToIndependentStudent_WhenOwnerIsSchoolBound()
    {
        var (ws, _, _, _, _, _, studentNone) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentNone), OwnerUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Owner_CanAssignToStudentOfSameSchool()
    {
        var (ws, _, _, _, studentA, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentA), OwnerUserId, isAdmin: false);

        r.Success.ShouldBeTrue();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Admin_CanAssignToStudentOfAnySchool()
    {
        var (ws, _, _, _, _, studentB, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentB), userId: 555, isAdmin: true);

        r.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Overview_GradeAssignment_ListsOnlyRequestersSchoolStudents()
    {
        var (ws, gradeId, schoolA, _, studentA, _, _) = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(OwnerUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws, GradeId = gradeId, StartAt = Start });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(ws, SchoolScope.For(OwnerUserId, schoolA));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.Students.Select(s => s.StudentId).ShouldBe(new[] { studentA });
    }

    // ---- issue #192: bağımsız (okulsuz) worksheet sahibi ----

    private const int TutorUserId = 2;
    private const int OtherTutorUserId = 3;

    /// <summary>
    /// Bağımsız sahip (TutorUserId) kendi worksheet'i "T" ile: studentA Approved, studentB Pending, studentNone booking yok.
    /// studentNone'a başka bir tutor'un Approved booking'i var (yalnızca kendi booking'i sayılmalı).
    /// </summary>
    private async Task<(int wsId, int gradeId, int studentA, int studentB, int studentNone)> SeedIndependentAsync()
    {
        var (_, gradeId, _, _, studentA, studentB, studentNone) = await SeedAsync();
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(TutorUserId);

        var tutor = new Teacher { UserId = TutorUserId, SchoolId = null, IsIndependentTutor = true };
        var other = new Teacher { UserId = OtherTutorUserId, SchoolId = null, IsIndependentTutor = true };
        var ws = new Worksheet { Name = "T", Description = "", GradeId = gradeId };
        ctx.AddRange(tutor, other, ws);
        await ctx.SaveChangesAsync();

        BookingSeed.Add(ctx, tutor.Id, studentA, BookingStatus.Approved, 8);
        BookingSeed.Add(ctx, tutor.Id, studentB, BookingStatus.Pending, 9);
        BookingSeed.Add(ctx, other.Id, studentNone, BookingStatus.Approved, 8);
        await ctx.SaveChangesAsync();

        return (ws.Id, gradeId, studentA, studentB, studentNone);
    }

    [Fact]
    public async Task IndependentOwner_CanAssignToStudentWithApprovedBooking_EvenIfStudentIsSchoolBound()
    {
        var (ws, _, studentA, _, _) = await SeedIndependentAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentA), TutorUserId, isAdmin: false);

        r.Success.ShouldBeTrue();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task IndependentOwner_CannotAssignToPendingBookingStudent_LooksLikeNotFound()
    {
        var (ws, _, _, studentB, _) = await SeedIndependentAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentB), TutorUserId, isAdmin: false);
        var missing = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, 99999), TutorUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(missing.Message);
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task IndependentOwner_CannotAssignToIndependentStudentWithoutOwnBooking()
    {
        // #190'da okulsuz→okulsuz izinliydi; #192: başka tutor'un Approved booking'i de yetmez.
        var (ws, _, _, _, studentNone) = await SeedIndependentAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx, Req(ws, studentNone), TutorUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task IndependentOwner_GradeAssignment_IsRejected_See222()
    {
        // issue #222 (ürün kararı A): #192'de sabitlenen "SchoolId=null grade ataması" davranışı kapatıldı — bağımsız
        // sahip sınıf bazlı atama yapamaz (tüm okulların o sınıfına genişliyordu). Hiç kayıt yazılmaz.
        var (ws, gradeId, _, _, _) = await SeedIndependentAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignAsUserAsync(ctx,
            new WorksheetAssignmentRequestDto { WorksheetId = ws, GradeId = gradeId, StartAt = Start }, TutorUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Bağımsız öğretmenler sınıf bazlı atama yapamaz");
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Overview_IndependentRequester_LegacyGradeAssignment_IsNotExpandedToStudents()
    {
        // issue #222: karar A öncesi yazılmış (SchoolId=null) grade ataması bağımsız istek sahibi için öğrencilere
        // genişletilmez — Approved Booking'li studentA dahi grade yoluyla listelenmez; yalnızca direkt atama görünür.
        var (ws, gradeId, _, _, _) = await SeedIndependentAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(TutorUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws, GradeId = gradeId, StartAt = Start });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(ws, SchoolScope.For(TutorUserId, null));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.Students.ShouldBeEmpty();
        overview.Summary.TotalStudents.ShouldBe(0);
    }

    [Fact]
    public async Task Overview_Unrestricted_ListsAllStudentsInGrade()
    {
        var (ws, gradeId, _, _, studentA, studentB, studentNone) = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(OwnerUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws, GradeId = gradeId, StartAt = Start });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(ws, SchoolScope.Unrestricted(OwnerUserId));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.Students.Select(s => s.StudentId).ShouldBe(new[] { studentA, studentB, studentNone }, ignoreOrder: true);
    }
}
