using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class WorksheetAssignmentServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx);

    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private const int OwnerUserId = 1;

    private async Task<(int worksheetId, int studentId, int gradeId)> SeedAsync(
        WorksheetTeacherSharing sharing = WorksheetTeacherSharing.Private)
    {
        await using var ctx = _db.NewContext();
        // Öğretmen yalnızca kendi worksheet'ini atayabilir: fixture'ı atayan kullanıcı (userId 1) sahipliğinde seed et.
        ctx.SetCurrentUser(OwnerUserId);
        var grade = new Grade { Name = "8" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var ws = new Worksheet { Name = "Atanacak", Description = "", GradeId = grade.Id, TeacherSharing = sharing };
        var student = new Student { UserId = 1, StudentNumber = "n", SchoolName = "s", GradeId = grade.Id };
        ctx.AddRange(ws, student);
        await ctx.SaveChangesAsync();
        return (ws.Id, student.Id, grade.Id);
    }

    /// <summary>
    /// issue #12 senaryoları için: sahibi (userId=OwnerUserId) belirtilen sharing ile bir worksheet,
    /// ve iki okul + o okullara bağlı öğrenci/öğretmenler seed eder.
    /// </summary>
    private async Task<(int worksheetId, int gradeId, int schoolAId, int schoolBId,
        int studentInSchoolAId, int studentInSchoolBId, int nonOwnerTeacherUserId)>
        SeedWithSchoolsAsync(WorksheetTeacherSharing sharing)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerUserId);

        var grade = new Grade { Name = "8" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "Atanacak", Description = "", GradeId = grade.Id, TeacherSharing = sharing };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        const int nonOwnerTeacherUserId = 777;
        var nonOwnerTeacher = new Teacher { UserId = nonOwnerTeacherUserId, SchoolId = schoolA.Id };
        ctx.AddRange(ws, studentA, studentB, nonOwnerTeacher);
        await ctx.SaveChangesAsync();

        return (ws.Id, grade.Id, schoolA.Id, schoolB.Id, studentA.Id, studentB.Id, nonOwnerTeacherUserId);
    }

    private static WorksheetAssignmentRequestDto Req(int worksheetId, int? studentId = null, int? gradeId = null,
        DateTime? start = null, DateTime? end = null) => new()
    {
        WorksheetId = worksheetId, StudentId = studentId, GradeId = gradeId,
        StartAt = start ?? Start, EndAt = end,
    };

    [Fact]
    public async Task Requires_exactly_one_target()
    {
        var (ws, student, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.AssignWorksheetAsync(Req(ws), 1)).Message.ShouldContain("En az bir hedef");
        (await svc.AssignWorksheetAsync(Req(ws, studentId: student, gradeId: 1), 1)).Message.ShouldContain("birlikte seçilemez");
    }

    [Fact]
    public async Task Rejects_an_end_before_the_start()
    {
        var (ws, student, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(
            Req(ws, studentId: student, end: Start.AddHours(-1)), 1);
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Bitiş zamanı");
    }

    [Fact]
    public async Task Validates_the_worksheet_student_and_grade_exist()
    {
        var (ws, student, grade) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.AssignWorksheetAsync(Req(99999, studentId: student), 1)).Message.ShouldContain("Worksheet bulunamadı");
        (await svc.AssignWorksheetAsync(Req(ws, studentId: 99999), 1)).Message.ShouldContain("Öğrenci bulunamadı");
        (await svc.AssignWorksheetAsync(Req(ws, gradeId: 99999), 1)).Message.ShouldContain("Sınıf bulunamadı");
    }

    [Fact]
    public async Task Assigns_to_a_student_and_records_the_assigning_user()
    {
        var (ws, student, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            // userId 55 sahibi değil; atayabilmesi için admin. Amaç: atayan kullanıcının kaydedilmesi.
            var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: 55, isAdmin: true);
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var a = await check.WorksheetAssignments.SingleAsync();
        a.StudentId.ShouldBe(student);
        a.GradeId.ShouldBeNull();
        a.CreateUserId.ShouldBe(55);
    }

    [Fact]
    public async Task Assigns_to_a_grade()
    {
        var (ws, _, grade) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(ws, gradeId: grade), 1)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.WorksheetAssignments.SingleAsync()).GradeId.ShouldBe(grade);
    }

    [Fact]
    public async Task Refuses_an_overlapping_assignment_for_the_same_target()
    {
        var (ws, student, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student, end: Start.AddDays(7)), 1);

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).AssignWorksheetAsync(
                Req(ws, studentId: student, start: Start.AddDays(3), end: Start.AddDays(10)), 1);
            r.Success.ShouldBeFalse();
            r.Message.ShouldContain("mevcut bir atama");
        }
    }

    [Fact]
    public async Task Allows_a_non_overlapping_assignment_for_the_same_target()
    {
        var (ws, student, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student, end: Start.AddDays(2)), 1);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(
                Req(ws, studentId: student, start: Start.AddDays(5), end: Start.AddDays(9)), 1)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.WorksheetAssignments.CountAsync()).ShouldBe(2);
    }

    // ---- ownership (issue #4: öğretmen yalnızca kendi worksheet'ini atar) ----

    [Fact]
    public async Task AssignWorksheetAsync_TeacherNotOwner_IsRejected()
    {
        var (ws, student, _) = await SeedAsync(); // worksheet sahibi userId 1

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: 999, isAdmin: false);

        r.Success.ShouldBeFalse();
        // issue #12 (security review): seed varsayılanı Private — CanView'e göre sahibi olmayan
        // öğretmene worksheet hiç görünmez, bu yüzden CanAssign'e hiç bakılmadan "bulunamadı" döner
        // (varlık/paylaşım durumu sızdırılmaz). PublicView/PublicAssignable ayrımı diğer testlerde.
        r.Message.ShouldBe("Worksheet bulunamadı.");
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AssignWorksheetAsync_TeacherOwnsWorksheet_Succeeds()
    {
        var (ws, student, _) = await SeedAsync();

        await using var ctx = _db.NewContext();
        (await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: OwnerUserId, isAdmin: false))
            .Success.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignWorksheetAsync_AdminNotOwner_Succeeds()
    {
        var (ws, student, _) = await SeedAsync();

        await using var ctx = _db.NewContext();
        (await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: 999, isAdmin: true))
            .Success.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignWorksheetAsync_LegacyWorksheetNullOwner_TeacherRejected_AdminAllowed()
    {
        int wsId, studentId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "Legacy", Description = "", GradeId = grade.Id }; // CreateUserId null
            var student = new Student { UserId = 1, StudentNumber = "n", SchoolName = "s", GradeId = grade.Id };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();
            wsId = ws.Id; studentId = student.Id;
        }

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(wsId, studentId: studentId), userId: 1, isAdmin: false))
                .Success.ShouldBeFalse();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(wsId, studentId: studentId), userId: 1, isAdmin: true))
                .Success.ShouldBeTrue();
    }

    // ---- issue #12: PublicAssignable — sahibi olmayan öğretmen onaysız atama ----

    [Fact]
    public async Task AssignWorksheetAsync_PublicAssignable_NonOwnerTeacherSameSchoolStudent_Succeeds()
    {
        var (ws, _, schoolA, _, studentInSchoolA, _, nonOwnerTeacherUserId) =
            await SeedWithSchoolsAsync(WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(
            Req(ws, studentId: studentInSchoolA), userId: nonOwnerTeacherUserId, isAdmin: false);

        r.Success.ShouldBeTrue();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task AssignWorksheetAsync_PublicView_NonOwnerTeacher_IsRejectedWithAskOwnerMessage()
    {
        var (ws, student, _) = await SeedAsync(WorksheetTeacherSharing.PublicView);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: 999, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe("Bu testi atamak için sahibinden atama izni istemeniz gerekir.");
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AssignWorksheetAsync_Private_NonOwnerTeacher_IsRejectedAsNotFound()
    {
        var (ws, student, _) = await SeedAsync(WorksheetTeacherSharing.Private);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: 999, isAdmin: false);

        r.Success.ShouldBeFalse();
        // issue #12 (security review): Private paylaşım CanView=false → varlığı sızdırmadan "bulunamadı".
        r.Message.ShouldBe("Worksheet bulunamadı.");
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AssignWorksheetAsync_PublicAssignable_NonOwnerTeacherDifferentSchoolStudent_IsRejected()
    {
        var (ws, _, _, _, _, studentInSchoolB, nonOwnerTeacherUserId) =
            await SeedWithSchoolsAsync(WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(
            Req(ws, studentId: studentInSchoolB), userId: nonOwnerTeacherUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe("Bu sınava yalnızca kendi öğrencilerinizi atayabilirsiniz.");
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AssignWorksheetAsync_PublicAssignable_NonOwnerTeacherWithoutSchool_IsRejected()
    {
        var (ws, student, _) = await SeedAsync(WorksheetTeacherSharing.PublicAssignable);

        await using var ctx = _db.NewContext();
        // 888 kullanıcısı için Teacher kaydı yok (legacy/eksik profil) — SchoolId çözülemez.
        var r = await NewService(ctx).AssignWorksheetAsync(
            Req(ws, studentId: student), userId: 888, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe("Bu sınava yalnızca kendi öğrencilerinizi atayabilirsiniz.");
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.Private)]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public async Task AssignWorksheetAsync_OwnerOrAdmin_SucceedsRegardlessOfSharing(WorksheetTeacherSharing sharing)
    {
        var (ws, student, _) = await SeedAsync(sharing);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: OwnerUserId, isAdmin: false))
                .Success.ShouldBeTrue();

        var (ws2, student2, _) = await SeedAsync(sharing);
        await using var ctx2 = _db.NewContext();
        (await NewService(ctx2).AssignWorksheetAsync(Req(ws2, studentId: student2), userId: 12345, isAdmin: true))
            .Success.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignWorksheetAsync_OwnerAssignsToGrade_RecordsAssignmentSchoolIdFromOwnersSchool()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerUserId);
        var school = new School { Name = "Sahip Okulu" };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        var grade = new Grade { Name = "8" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var ws = new Worksheet { Name = "Atanacak", Description = "", GradeId = grade.Id };
        var ownerTeacher = new Teacher { UserId = OwnerUserId, SchoolId = school.Id };
        ctx.AddRange(ws, ownerTeacher);
        await ctx.SaveChangesAsync();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws.Id, gradeId: grade.Id), userId: OwnerUserId, isAdmin: false);
        r.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var assignment = await check.WorksheetAssignments.SingleAsync();
        assignment.GradeId.ShouldBe(grade.Id);
        assignment.SchoolId.ShouldBe(school.Id);
    }

    [Fact]
    public async Task GetActiveAssignmentsForStudentAsync_GradeAssignmentScopedToSchool_OnlyReturnedForSameSchoolStudent()
    {
        int worksheetId, gradeId, schoolAId, schoolBId, studentInSchoolAId, studentInSchoolBId;
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(OwnerUserId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var schoolA = new School { Name = "Okul A" };
            var schoolB = new School { Name = "Okul B" };
            ctx.AddRange(schoolA, schoolB);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "Atanacak", Description = "", GradeId = grade.Id };
            var studentA = new Student { UserId = 20, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
            var studentB = new Student { UserId = 21, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
            ctx.AddRange(ws, studentA, studentB);
            await ctx.SaveChangesAsync();

            var assignment = new WorksheetAssignment
            {
                WorksheetId = ws.Id,
                GradeId = grade.Id,
                SchoolId = schoolA.Id,
                StartAt = Start,
                EndAt = null
            };
            ctx.WorksheetAssignments.Add(assignment);
            await ctx.SaveChangesAsync();

            worksheetId = ws.Id; gradeId = grade.Id;
            schoolAId = schoolA.Id; schoolBId = schoolB.Id;
            studentInSchoolAId = studentA.Id; studentInSchoolBId = studentB.Id;
        }

        await using var ctx2 = _db.NewContext();
        var svc = NewService(ctx2);

        var resultForSchoolA = await svc.GetActiveAssignmentsForStudentAsync(new StudentProfileDto
        {
            Id = studentInSchoolAId, GradeId = gradeId, SchoolId = schoolAId
        });
        resultForSchoolA.ShouldContain(a => a.WorksheetId == worksheetId);

        var resultForSchoolB = await svc.GetActiveAssignmentsForStudentAsync(new StudentProfileDto
        {
            Id = studentInSchoolBId, GradeId = gradeId, SchoolId = schoolBId
        });
        resultForSchoolB.ShouldNotContain(a => a.WorksheetId == worksheetId);
    }

    public void Dispose() => _db.Dispose();
}
