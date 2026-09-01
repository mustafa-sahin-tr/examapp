using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

public class StudentAndTeacherEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private async Task<int> SeedGradeAsync(string name = "5")
        => await WithDbAsync(async db =>
        {
            var g = new Grade { Name = name };
            db.Grades.Add(g);
            await db.SaveChangesAsync();
            return g.Id;
        });

    // ---- StudentController ----

    [Fact]
    public async Task Grades_are_listed_for_authenticated_callers_and_gated_for_anonymous()
    {
        await SeedGradeAsync("7");
        (await Anonymous().GetAsync("/api/student/grades")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var client = await ClientAsAsync(1, "Student", "kc-g", "Student");
        var grades = await client.GetFromJsonAsync<List<Grade>>("/api/student/grades", Json);
        grades!.ShouldContain(g => g.Name == "7");
    }

    [Fact]
    public async Task Check_student_reports_whether_the_caller_has_a_student_record()
    {
        var gradeId = await SeedGradeAsync();
        var client = await ClientAsAsync(10, "Student", "kc-10", "Student");

        var before = await client.GetFromJsonAsync<CheckStudentResponse>("/api/student/check-student", Json);
        before!.HasStudentRecord.ShouldBeFalse();

        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 10, StudentNumber = "100", SchoolName = "S", GradeId = gradeId });
            await db.SaveChangesAsync();
        });

        var after = await client.GetFromJsonAsync<CheckStudentResponse>("/api/student/check-student", Json);
        after!.HasStudentRecord.ShouldBeTrue();
    }

    [Fact]
    public async Task Student_profile_is_404_until_the_record_exists_then_returns_it()
    {
        var gradeId = await SeedGradeAsync();
        var client = await ClientAsAsync(11, "Student", "kc-11", "Student");

        (await client.GetAsync("/api/student/profile")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 11, StudentNumber = "111", SchoolName = "S", GradeId = gradeId });
            await db.SaveChangesAsync();
        });

        var profile = await client.GetFromJsonAsync<StudentProfileDto>("/api/student/profile", Json);
        profile!.GradeId.ShouldBe(gradeId);
    }

    [Fact]
    public async Task Update_grade_moves_the_student_to_the_new_grade()
    {
        var g1 = await SeedGradeAsync("5");
        var g2 = await SeedGradeAsync("6");
        var client = await ClientAsAsync(12, "Student", "kc-12", "Student");

        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 12, StudentNumber = "121", SchoolName = "S", GradeId = g1 });
            await db.SaveChangesAsync();
        });

        (await client.PostAsJsonAsync("/api/student/update-grade", g2)).EnsureSuccessStatusCode();

        var profile = await client.GetFromJsonAsync<StudentProfileDto>("/api/student/profile", Json);
        profile!.GradeId.ShouldBe(g2);
    }

    [Fact]
    public async Task Student_lookup_is_teacher_only()
    {
        var gradeId = await SeedGradeAsync();
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 20, StudentNumber = "200", SchoolName = "S", GradeId = gradeId });
            await db.SaveChangesAsync();
        });

        var student = await ClientAsAsync(20, "Student", "kc-20", "Student");
        (await student.GetAsync("/api/student/lookup")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var teacher = await ClientAsAsync(21, "Teacher", "kc-21", "Teacher");
        var list = await teacher.GetFromJsonAsync<List<StudentLookupDto>>("/api/student/lookup", Json);
        list!.ShouldContain(s => s.StudentNumber == "200");
    }

    [Fact]
    public async Task Update_theme_persists_the_student_preference()
    {
        var gradeId = await SeedGradeAsync();
        var client = await ClientAsAsync(13, "Student", "kc-13", "Student");
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = 13, StudentNumber = "131", SchoolName = "S", GradeId = gradeId });
            await db.SaveChangesAsync();
        });

        var resp = await client.PostAsJsonAsync("/api/student/update-theme",
            new UpdateThemeDto { ThemePreset = "enhanced", ThemeCustomConfig = "{}" });
        resp.EnsureSuccessStatusCode();

        var saved = await WithDbAsync(async db =>
            (await db.Students.FirstAsync(s => s.UserId == 13)).ThemePreset);
        saved.ShouldBe("enhanced");
    }

    // ---- TeacherController ----

    [Fact]
    public async Task Check_teacher_reports_record_presence_and_update_theme_persists()
    {
        var client = await ClientAsAsync(30, "Teacher", "kc-30", "Teacher");

        var before = await client.GetFromJsonAsync<CheckTeacherResponse>("/api/teacher/check-teacher", Json);
        before!.HasTeacherRecord.ShouldBeFalse();

        await WithDbAsync(async db =>
        {
            db.Teachers.Add(new Teacher { UserId = 30, SchoolName = "Okul" });
            await db.SaveChangesAsync();
        });

        var after = await client.GetFromJsonAsync<CheckTeacherResponse>("/api/teacher/check-teacher", Json);
        after!.HasTeacherRecord.ShouldBeTrue();

        (await client.PostAsJsonAsync("/api/teacher/update-theme",
            new UpdateThemeDto { ThemePreset = "minimal" })).EnsureSuccessStatusCode();

        var preset = await WithDbAsync(async db =>
            (await db.Teachers.FirstAsync(t => t.UserId == 30)).ThemePreset);
        preset.ShouldBe("minimal");
    }

    [Fact]
    public async Task Teacher_endpoints_reject_anonymous_callers()
    {
        (await Anonymous().GetAsync("/api/teacher/check-teacher")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Anonymous().GetAsync("/api/student/check-student")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record CheckStudentResponse(bool HasStudentRecord);
    private sealed record CheckTeacherResponse(bool HasTeacherRecord);
}
