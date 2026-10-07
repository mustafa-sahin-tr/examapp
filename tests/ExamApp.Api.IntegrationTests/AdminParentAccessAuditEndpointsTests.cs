using System.Net;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #424 (epic #407 V6) — admin veli erişim kaydı okuma yüzeyi: <c>GET api/admin/parent-access-audits</c> gerçek Postgres'te.
/// Yalnızca Admin (Parent/Student/Teacher 403, anonim 401); veli/öğrenci/tarih filtreleri, en yeni önce sıralama, sayfalama
/// (sayfa boyutu kırpılır), ters / 180 günden uzun aralık 400, <c>no-store</c>, meta-audit (<c>AdminDataAccessLogs</c>) ve 429.
/// </summary>
public class AdminParentAccessAuditEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string Url = "/api/admin/parent-access-audits";

    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private Task SeedAsync() => WithDbAsync(async db =>
    {
        db.ParentAccessAudits.AddRange(
            new ParentAccessAudit { ParentId = 1, StudentId = 10, Endpoint = ParentAccessEndpoints.ChildSummary, At = T0 },
            new ParentAccessAudit { ParentId = 1, StudentId = 10, Endpoint = ParentAccessEndpoints.ChildTestResult, ResourceId = 77, At = T0.AddDays(1) },
            new ParentAccessAudit { ParentId = 1, StudentId = 11, Endpoint = ParentAccessEndpoints.ChildProgress, At = T0.AddDays(2) },
            new ParentAccessAudit { ParentId = 2, StudentId = 10, Endpoint = ParentAccessEndpoints.ChildSchedule, At = T0.AddDays(3) },
            new ParentAccessAudit { ParentId = 2, StudentId = 12, Endpoint = ParentAccessEndpoints.ChildAssignments, At = T0.AddDays(4) });
        await db.SaveChangesAsync();
    });

    private Task<HttpClient> AdminAsync(int userId) => ClientAsAsync(userId, "Admin", $"kc-admin-{userId}", "Admin");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static int[] StudentIds(JsonElement page)
        => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("studentId").GetInt32()).ToArray();

    [Fact]
    public async Task Admin_lists_parent_access_audits_newest_first_with_filters_and_paging()
    {
        await SeedAsync();
        var admin = await AdminAsync(49201);

        var all = await ReadAsync(await admin.GetAsync(Url));
        all.GetProperty("totalCount").GetInt32().ShouldBe(5);
        all.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        all.GetProperty("pageSize").GetInt32().ShouldBe(20);
        StudentIds(all).ShouldBe(new[] { 12, 10, 11, 10, 10 }); // en yeni önce
        var row = all.GetProperty("items").EnumerateArray().ElementAt(3);
        row.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(new[] { "at", "endpoint", "id", "parentId", "resourceId", "studentId" });
        row.GetProperty("endpoint").GetString().ShouldBe(ParentAccessEndpoints.ChildTestResult);
        row.GetProperty("resourceId").GetInt32().ShouldBe(77);
        row.GetProperty("at").GetDateTime().ToUniversalTime().ShouldBe(T0.AddDays(1));

        StudentIds(await ReadAsync(await admin.GetAsync($"{Url}?parentId=1"))).ShouldBe(new[] { 11, 10, 10 });
        StudentIds(await ReadAsync(await admin.GetAsync($"{Url}?studentId=10"))).ShouldBe(new[] { 10, 10, 10 });
        StudentIds(await ReadAsync(await admin.GetAsync($"{Url}?parentId=2&studentId=10"))).ShouldBe(new[] { 10 });

        // from dahil, to hariç (UTC; 'Z'li ve saat dilimsiz değer aynı sonucu verir).
        var range = $"from={T0.AddDays(1):yyyy-MM-ddTHH:mm:ss}Z&to={T0.AddDays(3):yyyy-MM-ddTHH:mm:ss}Z";
        StudentIds(await ReadAsync(await admin.GetAsync($"{Url}?{range}"))).ShouldBe(new[] { 11, 10 });
        StudentIds(await ReadAsync(await admin.GetAsync($"{Url}?from={T0.AddDays(1):yyyy-MM-ddTHH:mm:ss}&to={T0.AddDays(3):yyyy-MM-ddTHH:mm:ss}")))
            .ShouldBe(new[] { 11, 10 });

        // Sayfalama: pageSize kırpılır, toplamı aşan sayfa boş.
        var page2 = await ReadAsync(await admin.GetAsync($"{Url}?pageSize=2&page=2"));
        StudentIds(page2).ShouldBe(new[] { 11, 10 });
        page2.GetProperty("totalCount").GetInt32().ShouldBe(5);
        (await ReadAsync(await admin.GetAsync($"{Url}?pageSize=1000"))).GetProperty("pageSize").GetInt32().ShouldBe(100);
        StudentIds(await ReadAsync(await admin.GetAsync($"{Url}?page=99"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_filters_are_400()
    {
        var admin = await AdminAsync(49202);
        (await admin.GetAsync($"{Url}?from=2026-09-05T00:00:00Z&to=2026-09-01T00:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"{Url}?from=2026-09-05T00:00:00Z&to=2026-09-05T00:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"{Url}?parentId=0")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"{Url}?studentId=-1")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"{Url}?from=not-a-date")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Aralık en fazla 180 gün: tam 180 geçer, 180 gün + 1 sn 400.
        (await admin.GetAsync($"{Url}?from=2026-01-01T00:00:00Z&to=2026-06-30T00:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var tooLong = await admin.GetAsync($"{Url}?from=2026-01-01T00:00:00Z&to=2026-06-30T00:00:01Z");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync()).ShouldContain("180");
    }

    [Fact]
    public async Task Every_served_read_is_meta_audited_and_rate_limited()
    {
        await SeedAsync();
        var admin = await AdminAsync(49203);

        (await admin.GetAsync($"{Url}?parentId=1&pageSize=2")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync($"{Url}?from=2026-09-05T00:00:00Z&to=2026-09-01T00:00:00Z")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Yalnızca veri dönen istek audit'lenir (400 yazmaz); satır PII taşımaz, sayılar normalize edilmiş değerler.
        var row = (await WithDbAsync(db => db.AdminDataAccessLogs.AsNoTracking().ToListAsync())).ShouldHaveSingleItem();
        row.ActorKeycloakId.ShouldBe("kc-admin-49203");
        row.Resource.ShouldBe(AdminDataAccessResource.ParentAccessAudits);
        row.Outcome.ShouldBe(AdminDataAccessOutcome.Served);
        (row.Page, row.PageSize, row.ReturnedCount, row.TotalCount).ShouldBe((1, 2, 2, 3));

        // Admin kullanıcı listeleriyle aynı kova (testte 10 / saat): 11. istek 429 ve RateLimited satırı.
        for (var i = 2; i < 10; i++)
            (await admin.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var rateLimited = await WithDbAsync(db => db.AdminDataAccessLogs.AsNoTracking()
            .Where(l => l.Outcome == AdminDataAccessOutcome.RateLimited).ToListAsync());
        rateLimited.ShouldHaveSingleItem().Resource.ShouldBe(AdminDataAccessResource.ParentAccessAudits);
    }

    [Fact]
    public async Task Only_admin_can_read_parent_access_audits()
    {
        await SeedAsync();
        foreach (var role in new[] { "Parent", "Student", "Teacher" })
        {
            var client = await ClientAsAsync(49210, role, $"kc-49210-{role}", role);
            (await client.GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
        }
        (await Anonymous().GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
