using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #499: gerçek Postgres ve HTTP üzerinden: yetki, öğrencinin sınıf varsayılanı, parametre sınırları.</summary>
[Collection(IntegrationCollection.Name)]
public class TaxonomySearchPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int StudentUser = 7201;
    private const string Api = "/api/subject/search";

    private async Task<(int Grade5, int Grade6)> SeedAsync()
    {
        return await WithDbAsync(async db =>
        {
            var grade5 = new Grade { Name = "5. Sınıf" };
            var grade6 = new Grade { Name = "6. Sınıf" };
            var math = new Subject { Name = "Matematik" };
            db.AddRange(grade5, grade6, math);
            await db.SaveChangesAsync();
            db.AddRange(
                new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade5.Id },
                new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade6.Id },
                new Student { UserId = StudentUser, StudentNumber = "S7201", SchoolName = "s", GradeId = grade5.Id });
            await db.SaveChangesAsync();
            return (grade5.Id, grade6.Id);
        });
    }

    private Task<HttpClient> StudentAsync() => ClientAsAsync(StudentUser, "Student", "kc-ts-student", "Student");

    [Fact]
    public async Task A_student_searches_their_own_grade_unless_another_is_asked_for()
    {
        var (grade5, grade6) = await SeedAsync();
        var student = await StudentAsync();

        var own = await student.GetFromJsonAsync<List<TaxonomySearchResultDto>>($"{Api}?q=MATEMAT%C4%B0K%20a%C3%A7%C4%B1lar", Json);
        own!.ShouldHaveSingleItem().GradeId.ShouldBe(grade5);
        own[0].Path.ShouldBe("Matematik › Açılar");

        var other = await student.GetFromJsonAsync<List<TaxonomySearchResultDto>>($"{Api}?q=aci&gradeId={grade6}", Json);
        other!.ShouldHaveSingleItem().GradeId.ShouldBe(grade6);
    }

    [Fact]
    public async Task Another_role_searches_every_grade()
    {
        await SeedAsync();
        await SeedApprovedTeacherAsync(7202);
        var teacher = await ClientAsAsync(7202, "Teacher", "kc-ts-teacher", "Teacher");

        var results = await teacher.GetFromJsonAsync<List<TaxonomySearchResultDto>>($"{Api}?q=aci", Json);

        results!.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("", "InvalidQuery")]
    [InlineData("?q=", "InvalidQuery")]
    [InlineData("?q=a", "InvalidQuery")]
    [InlineData("?q=matematik&take=21", "InvalidTake")]
    [InlineData("?q=matematik&take=0", "InvalidTake")]
    public async Task Bad_parameters_are_refused(string query, string errorCode)
    {
        await SeedAsync();
        var response = await (await StudentAsync()).GetAsync($"{Api}{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(errorCode);
    }

    [Fact]
    public async Task Sign_in_is_required()
    {
        (await Anonymous().GetAsync($"{Api}?q=matematik")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
