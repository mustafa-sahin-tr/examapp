using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.ParentLinks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #420 (epic #407 V2) — veli paneli özeti gerçek Postgres'te: kodla bağlan → çocuk listesinde studentId → özet
/// (JSON sözleşmesi alan listesiyle kilitli: yalnızca toplamlar, içerik/iletişim bilgisi yok) + Cache-Control no-store +
/// ParentAccessAudit satırı. Yetki: başka veli, bekleyen bağlantı, koparılmış bağlantı, silinmiş öğrenci, olmayan id → 404
/// (audit yazılmaz); yanlış rol → 403.
/// </summary>
public class ParentDashboardEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private sealed record Seeded(int StudentId, int GradeId, int SchoolId, int[] ParentIds);

    private async Task<Seeded> SeedAsync(int studentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk Ortaokulu" };
            var grade = new Grade { Name = "7. Sınıf" };
            db.AddRange(school, grade);
            await db.SaveChangesAsync();

            var student = new Student
            {
                UserId = studentUserId, StudentNumber = $"P{studentUserId}", SchoolId = school.Id,
                SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
            };
            db.Students.Add(student);
            var parents = parentUserIds.Select(u => new Parent { UserId = u }).ToList();
            db.Parents.AddRange(parents);
            await db.SaveChangesAsync();
            return new Seeded(student.Id, grade.Id, school.Id, parents.Select(p => p.Id).ToArray());
        });
    }

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    /// <summary>issue #436: veli-öncelikli bağlantı (doğrudan Active; ilk veli birincil). Bağlantı id'sini döner.</summary>
    private Task<int> LinkAsync(int studentUser, int parentUser) => SeedParentLinkAsync(parentUser, studentUser);

    private static string SummaryUrl(int studentId) => $"/api/parent/children/{studentId}/summary";

    private Task<int> AuditCountAsync() => WithDbAsync(db => db.ParentAccessAudits.CountAsync());

    [Fact]
    public async Task Linked_parent_gets_aggregate_summary_and_access_is_audited()
    {
        const int studentUser = 42901, parentUser = 42902;
        var seeded = await SeedAsync(studentUser, parentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        await LinkAsync(studentUser, parentUser);

        // Çocuk listesi artık panelin anahtarını (studentId) taşır.
        var childrenResponse = await parent.GetAsync("/api/parent-links/my-children");
        childrenResponse.Headers.CacheControl!.NoStore.ShouldBeTrue(); // review: çocuk listesi önbelleğe alınmaz
        var child = (await childrenResponse.Content.ReadFromJsonAsync<List<LinkedChildDto>>(Json))!.ShouldHaveSingleItem();
        child.StudentId.ShouldBe(seeded.StudentId);

        var now = DateTime.UtcNow;
        await WithDbAsync(async db =>
        {
            var done = new Worksheet { Name = "Kesirler", Description = "gizli içerik", GradeId = seeded.GradeId };
            var open = new Worksheet { Name = "Ondalık", Description = "", GradeId = seeded.GradeId };
            var missed = new Worksheet { Name = "Oran", Description = "", GradeId = seeded.GradeId };
            db.AddRange(done, open, missed);
            await db.SaveChangesAsync();

            db.WorksheetAssignments.AddRange(
                new WorksheetAssignment { WorksheetId = done.Id, StudentId = seeded.StudentId, StartAt = now.AddDays(-3), EndAt = now.AddDays(3) },
                new WorksheetAssignment { WorksheetId = open.Id, GradeId = seeded.GradeId, SchoolId = seeded.SchoolId, StartAt = now.AddDays(-1), EndAt = now.AddDays(4) },
                new WorksheetAssignment { WorksheetId = missed.Id, StudentId = seeded.StudentId, StartAt = now.AddDays(-5), EndAt = now.AddDays(-1) });

            var instance = new WorksheetInstance
            {
                StudentId = seeded.StudentId, WorksheetId = done.Id, StartTime = now.AddMinutes(-30),
                EndTime = now.AddMinutes(-10), Status = WorksheetInstanceStatus.Completed
            };
            db.TestInstances.Add(instance);
            await db.SaveChangesAsync();

            for (var i = 0; i < 2; i++)
            {
                var question = new Question { Text = $"Soru {i}", Point = 1 };
                db.Questions.Add(question);
                await db.SaveChangesAsync();
                var answer = new Answer { QuestionId = question.Id, Text = "A", Tag = "A" };
                var wq = new WorksheetQuestion { TestId = done.Id, QuestionId = question.Id, Order = i + 1 };
                db.AddRange(answer, wq);
                await db.SaveChangesAsync();
                db.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
                {
                    WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id, SelectedAnswerId = answer.Id,
                    IsCorrect = true, UpdateTime = now.AddMinutes(-20 + i)
                });
            }

            db.StudentPoints.Add(new StudentPoint { StudentId = seeded.StudentId, XP = 340 });
            await db.SaveChangesAsync();
        });

        var response = await parent.GetAsync(SummaryUrl(seeded.StudentId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        // Sözleşme: yalnızca bu alanlar — içerik, cevap anahtarı, ad/e-posta/telefon yok.
        root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ShouldBe(new[]
        {
            "assignments", "lastActivityAt", "questionsSolvedThisWeek", "studentId", "totalPoints", "weekStart"
        });
        root.GetProperty("assignments").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(new[] { "completed", "overdue", "pending", "windowDays" });
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("Kesirler");

        root.GetProperty("studentId").GetInt32().ShouldBe(seeded.StudentId);
        var weekStart = DateOnly.Parse(root.GetProperty("weekStart").GetString()!);
        weekStart.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        root.GetProperty("questionsSolvedThisWeek").GetInt32().ShouldBe(2);
        root.GetProperty("totalPoints").GetInt32().ShouldBe(340);
        var assignments = root.GetProperty("assignments");
        assignments.GetProperty("completed").GetInt32().ShouldBe(1);
        assignments.GetProperty("pending").GetInt32().ShouldBe(1);
        assignments.GetProperty("overdue").GetInt32().ShouldBe(1);
        // Saate kesilmiş (review): dakika hassasiyetinde hareket izi dönmez.
        var lastActivity = root.GetProperty("lastActivityAt").GetDateTime().ToUniversalTime();
        var expectedActivity = now.AddMinutes(-10);
        lastActivity.ShouldBe(new DateTime(expectedActivity.Year, expectedActivity.Month, expectedActivity.Day,
            expectedActivity.Hour, 0, 0, DateTimeKind.Utc));

        // Aynı 10 dk kovada tekrar açmak yeni audit satırı yazmaz (kova sınırına denk gelirse 2 olabilir).
        (await parent.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuditCountAsync()).ShouldBeInRange(1, 2);

        var audit = (await WithDbAsync(db => db.ParentAccessAudits.AsNoTracking().OrderBy(a => a.Id).ToListAsync())).First();
        audit.ParentId.ShouldBe(seeded.ParentIds[0]);
        audit.StudentId.ShouldBe(seeded.StudentId);
        audit.Endpoint.ShouldBe(ParentAccessEndpoints.ChildSummary);
        audit.At.ShouldBeInRange(now.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Summary_is_404_without_active_link_and_403_for_other_roles()
    {
        const int studentUser = 42911, parentUser = 42912, otherParentUser = 42913, pendingParentUser = 42914;
        var seeded = await SeedAsync(studentUser, parentUser, otherParentUser, pendingParentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        var otherParent = await ParentAsync(otherParentUser);
        var pendingParent = await ParentAsync(pendingParentUser);
        var teacher = await ClientAsAsync(42915, "Teacher", "kc-42915", "Teacher");

        var linkId = await LinkAsync(studentUser, parentUser);

        // Bekleyen ikinci veli isteği (birincil veli onaylamadı) erişim vermez — #436: istek birincil velinin kodu ile açılır.
        using (var codeDoc = JsonDocument.Parse(await (await parent.PostAsync($"/api/parent-links/{linkId}/second-parent-code", null))
                   .Content.ReadAsStringAsync()))
        {
            var redeem = await pendingParent.PostAsJsonAsync("/api/parent-links/redeem", new { code = codeDoc.RootElement.GetProperty("code").GetString() });
            redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.StudentId.ShouldBeNull(); // onaydan önce öğrenci id'si açılmaz
        }

        (await pendingParent.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var notFound = await otherParent.GetAsync(SummaryUrl(seeded.StudentId));
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using (var doc = JsonDocument.Parse(await notFound.Content.ReadAsStringAsync()))
            doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.NotFound);
        (await parent.GetAsync(SummaryUrl(999_999))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await student.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Anonymous().GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuditCountAsync()).ShouldBe(0);

        // Bağlı veli erişir.
        (await parent.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AuditCountAsync()).ShouldBe(1);

        // #436: öğrenci bağlantı koparamaz; tek veli ayrılamaz (409); admin koparınca erişim anında biter.
        (await student.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await parent.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var admin = await ClientAsAsync(42919, "Admin", "kc-admin-42919", "Admin");
        (await admin.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await parent.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuditCountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Soft_deleted_student_is_404()
    {
        const int studentUser = 42921, parentUser = 42922;
        var seeded = await SeedAsync(studentUser, parentUser);
        var parent = await ParentAsync(parentUser);
        await LinkAsync(studentUser, parentUser);
        (await parent.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await WithDbAsync(async db =>
        {
            var student = await db.Students.SingleAsync(s => s.Id == seeded.StudentId);
            student.IsDeleted = true;
            await db.SaveChangesAsync();
        });

        (await parent.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuditCountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// issue #436 (review M3): ikinci veli uçtan uca — birincil velinin kodu → redeem (Pending: özet 404) → birincil veli onayı →
    /// ikinci veli çocuğun özetini görür (V2 yalnız <c>Status == Active</c>'e bakar; kuruluş yolu önemsiz).
    /// </summary>
    [Fact]
    public async Task Second_parent_sees_the_summary_after_primary_parent_approval()
    {
        const int studentUser = 42931, primaryUser = 42932, secondUser = 42933;
        var seeded = await SeedAsync(studentUser, primaryUser, secondUser);
        var primary = await ParentAsync(primaryUser);
        var second = await ParentAsync(secondUser);
        var primaryLink = await LinkAsync(studentUser, primaryUser);

        string code;
        using (var codeDoc = JsonDocument.Parse(await (await primary.PostAsync($"/api/parent-links/{primaryLink}/second-parent-code", null))
                   .Content.ReadAsStringAsync()))
            code = codeDoc.RootElement.GetProperty("code").GetString()!;
        var redeem = await second.PostAsJsonAsync("/api/parent-links/redeem", new { code });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pendingLink = (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.LinkId;
        (await second.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await primary.PostAsync($"/api/parent-links/{pendingLink}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await second.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await primary.GetAsync(SummaryUrl(seeded.StudentId))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
