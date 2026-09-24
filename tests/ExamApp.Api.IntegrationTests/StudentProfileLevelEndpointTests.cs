using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #243: GET /api/student/profile — Level saklanan <c>StudentPoints.Level</c> kolonundan değil, XP'den
/// (1 + floor(sqrt(XP/50))) okuma anında hesaplanır. Profil sorgusu SQLite'ta çevrilemediği (APPLY) için Postgres'te.
/// </summary>
public class StudentProfileLevelEndpointTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(450, 4)]
    [InlineData(49, 1)]
    [InlineData(5000, 11)]
    public async Task Profile_level_is_computed_from_xp_not_from_the_stored_column(int xp, int expectedLevel)
    {
        const int userId = 7431;
        await WithDbAsync(async db =>
        {
            var student = new Student { UserId = userId, StudentNumber = "L1" };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            db.StudentPoints.Add(new StudentPoint { StudentId = student.Id, XP = xp });
            await db.SaveChangesAsync();
        });
        var client = await ClientAsAsync(userId, "Student", "kc-level-1", "Student");

        var profile = await client.GetFromJsonAsync<StudentProfileDto>("/api/student/profile", Json);

        profile!.XP.ShouldBe(xp);
        profile.Level.ShouldBe(expectedLevel);
    }

    [Fact]
    public async Task Student_without_points_is_level_one()
    {
        const int userId = 7432;
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = userId, StudentNumber = "L2" });
            await db.SaveChangesAsync();
        });
        var client = await ClientAsAsync(userId, "Student", "kc-level-2", "Student");

        var profile = await client.GetFromJsonAsync<StudentProfileDto>("/api/student/profile", Json);

        profile!.XP.ShouldBe(0);
        profile.Level.ShouldBe(1);
    }
}
