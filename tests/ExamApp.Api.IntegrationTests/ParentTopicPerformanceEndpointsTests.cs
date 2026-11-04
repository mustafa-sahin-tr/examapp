using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>Issue #498: veli yalnız kendi çocuğunun konu performansını görür; erişim kaydı yazılır. Genel yetki matrisi `ParentEndpointCatalog`'dan da geçer.</summary>
[Collection(IntegrationCollection.Name)]
public class ParentTopicPerformanceEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int ParentUser = 47301;
    private const int OtherParentUser = 47302;
    private const int ChildUser = 47303;
    private const int OtherChildUser = 47304;

    private static string Url(int studentId, string query = "?minQuestions=1") => $"/api/parent/children/{studentId}/topic-performance{query}";

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private async Task<(int Child, int OtherChild)> SeedAsync()
    {
        var ids = await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "5. Sınıf" };
            var subject = new Subject { Name = "Matematik" };
            db.AddRange(grade, subject);
            await db.SaveChangesAsync();
            var angles = new Topic { Name = "Açılar", SubjectId = subject.Id, GradeId = grade.Id };
            var fractions = new Topic { Name = "Kesirler", SubjectId = subject.Id, GradeId = grade.Id };
            db.AddRange(angles, fractions);
            var child = new Student { UserId = ChildUser, StudentNumber = "P47303", SchoolName = "s", GradeId = grade.Id };
            var other = new Student { UserId = OtherChildUser, StudentNumber = "P47304", SchoolName = "s", GradeId = grade.Id };
            db.AddRange(child, other);
            db.Parents.AddRange(new Parent { UserId = ParentUser }, new Parent { UserId = OtherParentUser });
            await db.SaveChangesAsync();

            async Task AnswerAsync(Student student, Topic topic)
            {
                var question = new Question { Text = "Soru", Point = 5, DifficultyLevel = 5, InteractionType = "mcq", TopicId = topic.Id };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                var session = new PracticeSession { StudentId = student.Id, GradeId = grade.Id, StartTime = DateTime.UtcNow.AddDays(-1) };
                session.Questions.Add(new PracticeSessionQuestion { QuestionId = question.Id, ShownAt = DateTime.UtcNow.AddDays(-1), AnsweredAt = DateTime.UtcNow.AddDays(-1), IsCorrect = true, TimeTaken = 10 });
                db.PracticeSessions.Add(session);
                await db.SaveChangesAsync();
            }

            await AnswerAsync(child, angles);
            await AnswerAsync(other, fractions);
            return (child.Id, other.Id);
        });
        await SeedParentLinkAsync(ParentUser, ChildUser, active: true);
        return ids;
    }

    [Fact]
    public async Task Parent_sees_only_their_own_child_and_the_access_is_recorded()
    {
        var (child, _) = await SeedAsync();
        var parent = await ParentAsync(ParentUser);

        var response = await parent.GetAsync(Url(child));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var result = await response.Content.ReadFromJsonAsync<TopicPerformanceDto>(Json);
        var item = result!.Items.ShouldHaveSingleItem();
        (item.TopicName, item.Correct).ShouldBe(("Açılar", 1));

        await WithDbAsync(async db =>
        {
            var audit = await db.ParentAccessAudits.AsNoTracking().SingleAsync(a => a.Endpoint == ParentAccessEndpoints.ChildTopicPerformance);
            audit.StudentId.ShouldBe(child);
        });
    }

    [Fact]
    public async Task Another_parents_child_is_not_found_and_leaves_no_access_record()
    {
        var (child, otherChild) = await SeedAsync();

        (await (await ParentAsync(ParentUser)).GetAsync(Url(otherChild))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await ParentAsync(OtherParentUser)).GetAsync(Url(child))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await WithDbAsync(async db =>
            (await db.ParentAccessAudits.AsNoTracking().CountAsync(a => a.Endpoint == ParentAccessEndpoints.ChildTopicPerformance)).ShouldBe(0));
    }

    [Fact]
    public async Task A_pending_link_gives_no_access()
    {
        var (child, _) = await SeedAsync();
        await SeedParentLinkAsync(OtherParentUser, ChildUser, active: false);

        (await (await ParentAsync(OtherParentUser)).GetAsync(Url(child))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("?days=91", "InvalidDays")]
    [InlineData("?groupBy=grade", "InvalidGroupBy")]
    [InlineData("?minQuestions=0", "InvalidMinQuestions")]
    public async Task Parameters_follow_the_same_rules_as_the_student_endpoint(string query, string errorCode)
    {
        var (child, _) = await SeedAsync();

        var response = await (await ParentAsync(ParentUser)).GetAsync(Url(child, query));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(errorCode);
    }

    [Fact]
    public async Task Other_roles_cannot_use_it()
    {
        var (child, _) = await SeedAsync();

        (await Anonymous().GetAsync(Url(child))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var student = await ClientAsAsync(ChildUser, "Student", $"kc-{ChildUser}", "Student");
        (await student.GetAsync(Url(child))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
