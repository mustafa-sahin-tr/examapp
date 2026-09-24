using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #189: SchoolContextResolver davranışı — kullanıcının tenant (okul)
/// bağlamını DB'den doğrulamak.
///
/// Davranış tablosu (Rol × kayıt durumu → sonuç):
///   Teacher, Teachers'da UserId eşleşen satır var, SchoolId dolu   -> o SchoolId
///   Teacher, satır var, SchoolId null (bağımsız öğretmen)          -> null
///   Teacher, Teachers'da satır yok                                 -> null
///   Student, Students'da UserId eşleşen satır var, SchoolId dolu   -> o SchoolId
///   Student, satır var, SchoolId null                              -> null
///   Student, Students'da satır yok                                 -> null
///   Admin / Service / Parent / diğer roller                        -> null (sorgu atılmaz)
///   user null                                                       -> null
/// </summary>
public class SchoolContextResolverTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private SchoolContextResolver NewResolver(AppDbContext ctx) => new(ctx);

    private async Task<int> SeedSchoolAsync(string name = "Test Okulu")
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    // ---- Teacher role tests ----

    [Fact]
    public async Task ResolveSchoolIdAsync_Teacher_WithSchoolId_ReturnsSchoolId()
    {
        var schoolId = await SeedSchoolAsync("Öğretmen Okulu");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 101, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 101, KeycloakId = "kc-101", Role = "Teacher" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBe(schoolId);
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_Teacher_WithNullSchoolId_ReturnsNull()
    {
        await using (var ctx = _db.NewContext())
        {
            // Bağımsız öğretmen: SchoolId null
            ctx.Teachers.Add(new Teacher { UserId = 102, SchoolId = null, IsIndependentTutor = true });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 102, KeycloakId = "kc-102", Role = "Teacher" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_Teacher_NoRow_ReturnsNull()
    {
        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 103, KeycloakId = "kc-103", Role = "Teacher" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    // ---- Student role tests ----

    [Fact]
    public async Task ResolveSchoolIdAsync_Student_WithSchoolId_ReturnsSchoolId()
    {
        var schoolId = await SeedSchoolAsync("Öğrenci Okulu");
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;

            ctx.Students.Add(new Student { UserId = 201, StudentNumber = "S201", SchoolId = schoolId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 201, KeycloakId = "kc-201", Role = "Student" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBe(schoolId);
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_ProfileTeacher_WithoutTeacherRow_FallsBackToStudentRow()
    {
        // issue #277 review (security HIGH): profil rolü (auth-api) JWT'den geri kalabilir — Teacher/Student ayrımı okul
        // çözümünde karar vermez; öğretmen kaydı yoksa öğrenci kaydının okulu esas alınır.
        var schoolId = await SeedSchoolAsync("Öğrenci Okulu 2");
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "6" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            ctx.Students.Add(new Student { UserId = 277, StudentNumber = "S277", SchoolId = schoolId, GradeId = grade.Id });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var result = await NewResolver(testCtx).ResolveSchoolIdAsync(new UserProfileDto { Id = 277, KeycloakId = "kc-277", Role = "Teacher" });

        result.ShouldBe(schoolId);
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_Student_WithNullSchoolId_ReturnsNull()
    {
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "6" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;

            ctx.Students.Add(new Student { UserId = 202, StudentNumber = "S202", SchoolId = null, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 202, KeycloakId = "kc-202", Role = "Student" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_Student_NoRow_ReturnsNull()
    {
        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 203, KeycloakId = "kc-203", Role = "Student" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    // ---- Other roles tests ----

    [Fact]
    public async Task ResolveSchoolIdAsync_AdminRole_ReturnsNull()
    {
        var schoolId = await SeedSchoolAsync("Admin Okulu");
        // Admin rolü olan kullanıcı Teacher/Student tablosuna kaydedilmeseler de test var
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 301, SchoolId = schoolId });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 301, KeycloakId = "kc-301", Role = "Admin" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_ParentRole_ReturnsNull()
    {
        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 401, KeycloakId = "kc-401", Role = "Parent" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_ServiceRole_ReturnsNull()
    {
        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 501, KeycloakId = "kc-501", Role = "Service" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_UnknownRole_ReturnsNull()
    {
        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 601, KeycloakId = "kc-601", Role = "UnknownRole" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        result.ShouldBeNull();
    }

    // ---- Edge cases ----

    [Fact]
    public async Task ResolveSchoolIdAsync_NullUser_ReturnsNull()
    {
        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);

        var result = await resolver.ResolveSchoolIdAsync(null!);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_SoftDeletedAndLiveTeacherRows_ReturnsTheLiveRow_AndASecondLiveRowIsRejected()
    {
        var school1Id = await SeedSchoolAsync("Okul 1");
        var school2Id = await SeedSchoolAsync("Okul 2");

        // issue #259: Teachers.UserId canlı satırlar için unique (filtreli index) — soft-delete edilmiş eski satır
        // yanında tek canlı satır olabilir; resolver silinmiş satırı (global !IsDeleted filtresi) görmez.
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.AddRange(
                new Teacher { UserId = 701, SchoolId = school1Id, IsDeleted = true },
                new Teacher { UserId = 701, SchoolId = school2Id }
            );
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 701, SchoolId = school1Id });
            await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 701, KeycloakId = "kc-701", Role = "Teacher" };

        (await resolver.ResolveSchoolIdAsync(user)).ShouldBe(school2Id);
    }

    // ---- Security: Teachers row takes precedence over Students row (issue #234) ----

    [Fact]
    public async Task ResolveSchoolIdAsync_BothTeacherAndStudentRows_TeachersSchoolIdTakesPrecedence_WithNullSchoolId()
    {
        var studentSchoolId = await SeedSchoolAsync("Öğrenci Okulu");
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "7" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;

            // issue #234: Öğretmen satırı (SchoolId=null) ve Öğrenci satırı (SchoolId=X) varsa,
            // profil rolü "Student" olsa bile, çözülen okul Öğretmen satırından (null) gelir.
            ctx.Teachers.Add(new Teacher { UserId = 801, SchoolId = null, IsIndependentTutor = true });
            ctx.Students.Add(new Student { UserId = 801, StudentNumber = "S801", SchoolId = studentSchoolId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 801, KeycloakId = "kc-801", Role = "Student" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        // Teachers.SchoolId = null, bu değer dönmeli (Students.SchoolId değil)
        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveSchoolIdAsync_BothTeacherAndStudentRows_TeachersSchoolIdTakesPrecedence_WithSchoolId()
    {
        var teacherSchoolId = await SeedSchoolAsync("Öğretmen Okulu");
        var studentSchoolId = await SeedSchoolAsync("Öğrenci Okulu");
        int gradeId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            gradeId = grade.Id;

            // issue #234: Öğretmen satırı (SchoolId=Y) ve Öğrenci satırı (SchoolId=X) varsa,
            // profil rolü "Student" olsa bile, çözülen okul Öğretmen satırından (Y) gelir.
            ctx.Teachers.Add(new Teacher { UserId = 802, SchoolId = teacherSchoolId });
            ctx.Students.Add(new Student { UserId = 802, StudentNumber = "S802", SchoolId = studentSchoolId, GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var testCtx = _db.NewContext();
        var resolver = NewResolver(testCtx);
        var user = new UserProfileDto { Id = 802, KeycloakId = "kc-802", Role = "Student" };

        var result = await resolver.ResolveSchoolIdAsync(user);

        // Teachers.SchoolId = teacherSchoolId, bu değer dönmeli (studentSchoolId değil)
        result.ShouldBe(teacherSchoolId);
        result.ShouldNotBe(studentSchoolId);
    }
}
