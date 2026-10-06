using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #361 — gerçek pipeline + Postgres:
/// <list type="bullet">
/// <item>(Kayıt ucunun kendisi — refresh cookie + Keycloak gerektirir — birim testlerde: StudentSchoolVerificationAuthorityTests,
/// StudentControllerRegisterSchoolLockTests.) Kendi kaydında okul seçen öğrenci BEKLEMEDE: profil (soğuk önbellek → DB resolver) okulsuz, okul liderlik tablosu
/// 400; <c>GetStudentProfile</c> doğrulanmış okulu null, bekleyen okulu ayrı alanda döner.</item>
/// <item>Onay uçları: rol kapısı (Student 403, onaysız öğretmen 403), başka okulun öğretmeni 404 (IDOR yok), aynı okulun
/// öğretmeni ve platform admin 200; onay sonrası öğrencinin profil önbelleği düşer ve okul kapsamı açılır.</item>
/// </list>
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StudentSchoolVerificationEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int PendingUserId = 361_001;
    private const int VerifiedUserId = 361_002;
    private const int PendingBUserId = 361_003;
    private const int TeacherAUserId = 361_010;
    private const int TeacherBUserId = 361_011;
    private const int UnapprovedTeacherAUserId = 361_012;

    private sealed record World(int SchoolA, int SchoolB, int PendingStudentId, int PendingBStudentId);

    private async Task<World> SeedAsync()
    {
        var world = await WithDbAsync(async db =>
        {
            var a = new School { Name = "Okul A" };
            var b = new School { Name = "Okul B" };
            var grade = new Grade { Name = "8" };
            db.AddRange(a, b, grade);
            await db.SaveChangesAsync();

            var pending = new Student { UserId = PendingUserId, StudentNumber = "36100001", GradeId = grade.Id, SchoolId = a.Id };
            var verified = new Student
            {
                UserId = VerifiedUserId, StudentNumber = "36100002", GradeId = grade.Id, SchoolId = a.Id, SchoolVerifiedAt = DateTime.UtcNow
            };
            var pendingB = new Student { UserId = PendingBUserId, StudentNumber = "36100003", SchoolId = b.Id };
            db.Students.AddRange(pending, verified, pendingB);
            db.Teachers.Add(new Teacher { UserId = UnapprovedTeacherAUserId, SchoolId = a.Id, AccountApprovedAt = null });
            await db.SaveChangesAsync();
            return new World(a.Id, b.Id, pending.Id, pendingB.Id);
        });

        await SeedApprovedTeacherAsync(TeacherAUserId, world.SchoolA);
        await SeedApprovedTeacherAsync(TeacherBUserId, world.SchoolB);
        return world;
    }

    /// <summary>
    /// Profil önbelleğini SEED ETMEYEN öğrenci istemcisi: profil auth-api taklidinden gelir, SchoolId'yi exam API DB'den
    /// (UserSchoolResolver) kendisi çözer — üretimdeki gibi.
    /// </summary>
    private async Task<HttpClient> ColdStudentClientAsync(int userId)
    {
        var sub = $"kc-361-{userId}";
        var bearer = $"token-361-{userId}";
        await Factory.Services.GetRequiredService<IDistributedCache>().RemoveAsync(sub);
        Factory.Services.GetRequiredService<FakeAuthApiProfiles>().Register(bearer, new UserProfileDto
        {
            Id = userId, KeycloakId = sub, Role = "Student", FullName = "Öğrenci", Email = "s@t.local"
        });
        Factory.Services.GetRequiredService<FakeUserDirectory>()
            .Add(new UserLookupResultDto { Id = userId, KeycloakId = sub, FullName = $"Öğrenci {userId}" });

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", sub);
        client.DefaultRequestHeaders.Add("X-Test-Username", sub);
        client.DefaultRequestHeaders.Add("X-Test-Roles", "Student");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private Task<HttpClient> TeacherClientAsync(int userId, int? schoolId) =>
        ClientAsWithSchoolAsync(userId, "Teacher", $"kc-361-t-{userId}", schoolId, "Teacher");

    private static string Approve(int studentId) => $"/api/student-school-requests/{studentId}/approve";
    private static string Reject(int studentId) => $"/api/student-school-requests/{studentId}/reject";

    private Task<Student> StudentAsync(int studentId) =>
        WithDbAsync(db => db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId));

    [Fact]
    public async Task Pending_student_has_no_school_scope_until_a_same_school_teacher_approves()
    {
        var w = await SeedAsync();
        var student = await ColdStudentClientAsync(PendingUserId);

        (await student.GetAsync("/api/leaderboard?scope=school")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var verified = await ColdStudentClientAsync(VerifiedUserId);
        (await verified.GetAsync("/api/leaderboard?scope=school")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var teacherA = await TeacherClientAsync(TeacherAUserId, w.SchoolA);
        (await teacherA.PostAsync(Approve(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var row = await StudentAsync(w.PendingStudentId);
        row.SchoolVerifiedAt.ShouldNotBeNull();
        row.SchoolVerifiedByUserId.ShouldBe(TeacherAUserId);

        // Onay öğrencinin profil önbelleğini düşürdü → sonraki istek okulu DB'den (artık doğrulanmış) çözer.
        var after = await student.GetAsync("/api/leaderboard?scope=school");
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task Other_school_teacher_gets_404_and_cannot_see_the_request()
    {
        var w = await SeedAsync();
        var teacherB = await TeacherClientAsync(TeacherBUserId, w.SchoolB);

        (await teacherB.PostAsync(Approve(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacherB.PostAsync(Reject(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacherB.PostAsync(Approve(999_999), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await teacherB.GetFromJsonAsync<JsonElement>("/api/student-school-requests");
        var ids = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("studentId").GetInt32()).ToList();
        ids.ShouldBe(new[] { w.PendingBStudentId });

        var row = await StudentAsync(w.PendingStudentId);
        row.SchoolId.ShouldBe(w.SchoolA);
        row.SchoolVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Students_and_unapproved_teachers_are_forbidden()
    {
        var w = await SeedAsync();

        var student = await ClientAsAsync(VerifiedUserId, "Student", "kc-361-student", "Student");
        (await student.GetAsync("/api/student-school-requests")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsync(Approve(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var unapproved = await TeacherClientAsync(UnapprovedTeacherAUserId, null);
        (await unapproved.PostAsync(Approve(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await Anonymous().GetAsync("/api/student-school-requests")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await StudentAsync(w.PendingStudentId)).SchoolVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Admin_sees_all_schools_and_can_approve_and_reject_anywhere()
    {
        var w = await SeedAsync();
        var admin = await ClientAsAsync(361_900, "Admin", "kc-361-admin", "Admin");

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/student-school-requests");
        list.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var count = await admin.GetFromJsonAsync<JsonElement>("/api/student-school-requests/count");
        count.GetProperty("count").GetInt32().ShouldBe(2);

        (await admin.PostAsync(Approve(w.PendingBStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StudentAsync(w.PendingBStudentId)).SchoolVerifiedByUserId.ShouldBe(361_900);

        (await admin.PostAsync(Reject(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StudentAsync(w.PendingStudentId)).SchoolId.ShouldBeNull();

        // Karar verilmiş başvuru artık bekleyen değil → 404.
        (await admin.PostAsync(Approve(w.PendingStudentId), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Student_profile_query_returns_only_the_verified_school_on_postgres()
    {
        var w = await SeedAsync();

        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IStudentService>();
        var pending = await service.GetStudentProfile(PendingUserId);
        var verified = await service.GetStudentProfile(VerifiedUserId);

        pending.SchoolId.ShouldBeNull();
        pending.PendingSchoolId.ShouldBe(w.SchoolA);
        pending.PendingSchoolName.ShouldBe("Okul A");
        verified.SchoolId.ShouldBe(w.SchoolA);
        verified.PendingSchoolId.ShouldBeNull();
    }
}
