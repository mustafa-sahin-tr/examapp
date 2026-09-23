using System.Net;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #246: GET api/admin/students|teachers — gerçek pipeline + Postgres üzerinde
/// (1) her başarılı çağrının PII'siz audit satırı, (2) kullanıcı (sub) başına rate limit, 429 + Retry-After.
/// </summary>
public class AdminUserListAuditAndRateLimitTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static string NewSub(string prefix = "kc-admin") => $"{prefix}-{Guid.NewGuid():N}";

    private AdminUserListRateLimitOptions Limits =>
        Factory.Services.GetRequiredService<IOptionsMonitor<AdminUserListRateLimitOptions>>().CurrentValue;
    [Fact]
    public async Task Each_successful_list_call_writes_an_audit_row_without_pii()
    {
        var schoolId = await WithDbAsync(async db =>
        {
            var school = new School { Name = "Ankara Lisesi" };
            db.Schools.Add(school);
            await db.SaveChangesAsync();
            for (var i = 0; i < 3; i++)
                db.Students.Add(new Student { UserId = 1000 + i, StudentNumber = $"S{i}", SchoolId = school.Id });
            db.Teachers.Add(new Teacher { UserId = 2000 });
            await db.SaveChangesAsync();
            return school.Id;
        });
        var adminSub = NewSub();
        var admin = await ClientAsAsync(2, "Admin", adminSub, realmRoles: "Admin");

        (await admin.GetAsync($"/api/admin/students?schoolId={schoolId}&page=1&pageSize=2")).EnsureSuccessStatusCode();
        (await admin.GetAsync("/api/admin/teachers?unassigned=true&pageSize=1000")).EnsureSuccessStatusCode();
        // 400 (filtre çakışması) ve 403 (admin değil) veri döndürmez → audit yok.
        (await admin.GetAsync("/api/admin/students?schoolId=1&unassigned=true")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var teacher = await ClientAsAsync(1, "Teacher", NewSub("kc-teacher"), realmRoles: "Teacher");
        (await teacher.GetAsync("/api/admin/students")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var rows = await WithDbAsync(db => db.AdminDataAccessLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync());
        rows.Count.ShouldBe(2);

        var students = rows[0];
        students.ActorKeycloakId.ShouldBe(adminSub);
        students.Resource.ShouldBe(AdminDataAccessResource.StudentList);
        students.SchoolIdFilter.ShouldBe(schoolId);
        students.UnassignedFilter.ShouldBeFalse();
        students.Page.ShouldBe(1);
        students.PageSize.ShouldBe(2);
        students.ReturnedCount.ShouldBe(2);
        students.TotalCount.ShouldBe(3);

        var teachers = rows[1];
        teachers.Resource.ShouldBe(AdminDataAccessResource.TeacherList);
        teachers.SchoolIdFilter.ShouldBeNull();
        teachers.UnassignedFilter.ShouldBeTrue();
        teachers.PageSize.ShouldBe(100); // normalize edilmiş değer
        teachers.ReturnedCount.ShouldBe(1);
        teachers.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Rate_limit_is_per_user_shared_across_both_lists_and_returns_429_with_retry_after()
    {
        var limits = Limits;
        var firstSub = NewSub();
        var first = await ClientAsAsync(2, "Admin", firstSub, realmRoles: "Admin");

        // İki uç tek kovayı paylaşır: öğrenci/öğretmen dönüşümlü çağrılır.
        for (var i = 0; i < limits.PermitLimit; i++)
        {
            var path = i % 2 == 0 ? "/api/admin/students" : "/api/admin/teachers";
            (await first.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.OK, $"call {i + 1}/{limits.PermitLimit} should pass");
        }

        var rejected = await first.GetAsync("/api/admin/teachers");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        rejected.Headers.RetryAfter!.Delta.ShouldNotBeNull();
        var retryAfterSeconds = rejected.Headers.RetryAfter.Delta!.Value.TotalSeconds;
        retryAfterSeconds.ShouldBeGreaterThan(0);
        retryAfterSeconds.ShouldBeLessThanOrEqualTo(limits.WindowSeconds);
        // Policy'ye özgü metin (jenerik common.tooManyRequests değil). Varsayılan kültür tr.
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Kısa sürede çok fazla liste isteği yapıldı. Lütfen biraz bekleyip tekrar deneyin.");

        // Başka bir admin kendi kovasıyla etkilenmez (partition = sub, IP değil — TestServer'da hepsi aynı "IP").
        var second = await ClientAsAsync(4, "Admin", NewSub(), realmRoles: "Admin");
        (await second.GetAsync("/api/admin/students")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Limit yalnızca bu iki uca uygulanır; aynı admin diğer admin uçlarını kullanmaya devam eder.
        (await first.GetAsync("/api/admin/schools")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Reddedilen istek veri döndürmediği için audit'lenmez.
        (await WithDbAsync(db => db.AdminDataAccessLogs.CountAsync(r => r.ActorKeycloakId == firstSub)))
            .ShouldBe(limits.PermitLimit);
    }

    [Fact]
    public async Task Unauthorized_and_forbidden_requests_do_not_consume_the_bucket()
    {
        var limits = Limits;
        var sub = NewSub();

        // Aynı sub, Admin rolü olmadan: 403'ler kovayı tüketmez (limiter yetkilendirmeden sonra çalışır).
        var forbidden = await ClientAsAsync(1, "Teacher", sub, realmRoles: "Teacher");
        for (var i = 0; i < limits.PermitLimit + 2; i++)
            (await forbidden.GetAsync("/api/admin/students")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Kimliksiz istekler 401 alır, hiçbir zaman 429 değil.
        var anonymous = Anonymous();
        for (var i = 0; i < limits.PermitLimit + 2; i++)
            (await anonymous.GetAsync("/api/admin/teachers")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Aynı sub Admin rolüyle hâlâ tam kotaya sahip.
        var admin = await ClientAsAsync(2, "Admin", sub, realmRoles: "Admin");
        for (var i = 0; i < limits.PermitLimit; i++)
            (await admin.GetAsync("/api/admin/students")).StatusCode.ShouldBe(HttpStatusCode.OK, $"call {i + 1}");
    }

    [Fact]
    public async Task List_json_contains_only_masked_emails()
    {
        // FakeUserDirectory Respawn ile sıfırlanmaz → benzersiz, büyük UserId'ler.
        var baseId = 900_000 + Random.Shared.Next(0, 90_000) * 10;
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = baseId, FullName = "Ali Veli", Email = "ali.veli@okul.k12.tr", Enabled = true });
        directory.Add(new() { Id = baseId + 1, FullName = "Ayşe Yılmaz", Email = "ayse@okul.k12.tr", Enabled = true });
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = baseId, StudentNumber = "M1" });
            db.Teachers.Add(new Teacher { UserId = baseId + 1 });
            await db.SaveChangesAsync();
        });
        var admin = await ClientAsAsync(2, "Admin", NewSub(), realmRoles: "Admin");

        var studentsJson = await admin.GetStringAsync("/api/admin/students");
        var teachersJson = await admin.GetStringAsync("/api/admin/teachers");

        studentsJson.ShouldContain("\"email\":\"a***@okul.k12.tr\"");
        studentsJson.ShouldContain("Ali Veli"); // gerçekten sahte rehberden çözüldü (fail-soft değil)
        studentsJson.ShouldNotContain("ali.veli@");
        teachersJson.ShouldContain("\"email\":\"a***@okul.k12.tr\"");
        teachersJson.ShouldNotContain("ayse@");
    }
}
