using System.Net;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.TestSupport;
using ExamApp.Api.Models.Dtos.ParentLinks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #424 (epic #407 V6) — merkezi IDOR / yetki matrisi, gerçek Postgres'te. <see cref="ParentEndpointCatalog"/>'daki HER veli
/// okuma ucu için: başka velinin çocuğu, Pending bağlantı, Revoked bağlantı, soft-delete edilmiş öğrenci, olmayan öğrenci → 404;
/// Student / Teacher / Admin rolü → 403; anonim → 401; bu durumların hiçbiri <c>ParentAccessAudits</c>'e satır yazmaz. Pozitif
/// kontrol: bağlı veli → 200 + <c>Cache-Control: no-store</c> + audit; öğrenci koparınca BİR SONRAKİ istek 404 (sunucu önbelleği yok).
/// Kilitler: yansıma kilidi (ExamApp.Api.Tests <c>ParentEndpointCatalogTests</c>) + ters keşif (aşağıda: veliye açık her uç).
/// </summary>
public class ParentAccessMatrixTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    // ---- ters keşif: Parent rolüyle ERİŞİLEBİLEN her uç (yetki politikası değerlendirmesi)

    /// <summary>
    /// Uygulamanın gerçek <see cref="EndpointDataSource"/>'undaki HER <see cref="RouteEndpoint"/> (controller aksiyonları, AcceptVerbs,
    /// miras alınan attribute'lar, policy'ler, SignalR hub'ları, health uçları) için endpoint'in birleşik yetki politikası yalnızca
    /// <c>Parent</c> rolü taşıyan bir kullanıcıyla değerlendirilir (MVC/routing'in kullandığı <see cref="AuthorizationPolicy.CombineAsync(IAuthorizationPolicyProvider, IEnumerable{IAuthorizeData}, IEnumerable{AuthorizationPolicy})"/>).
    /// 401/403 vermeyen (anonim ya da politikası geçen) her uç ya <see cref="ParentEndpointCatalog.ReadEndpoints"/>'te, ya
    /// <see cref="ParentEndpointCatalog.Exempt"/>'te ya da gerekçesiyle <see cref="ParentEndpointCatalog.ParentReachableAllowlist"/>'te
    /// olmalı. <see cref="ParentEndpointCatalog.MustDenyParent"/> uçları (ör. liderlik tablosu) veliye KAPALI olmalı.
    /// </summary>
    [Fact]
    public async Task Every_endpoint_a_parent_can_reach_is_in_the_matrix_or_allowlisted()
    {
        var reachable = await ParentReachableEndpointsAsync();

        foreach (var key in ParentEndpointCatalog.MustDenyParent)
            reachable.ShouldNotContain(key, $"{key} veliye kapalı olmalı (403).");

        var known = ParentEndpointCatalog.ReadEndpoints.Select(e => e.Action)
            .Concat(ParentEndpointCatalog.Exempt.Keys)
            .Concat(ParentEndpointCatalog.ParentReachableAllowlist.Keys)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = reachable.Except(known).OrderBy(k => k, StringComparer.Ordinal).ToList();
        unknown.ShouldBeEmpty(
            "Veli (Parent rolü) bu uçlara erişebiliyor ama matriste/allowlist'te yok. Ya rolü daraltın ya veli okuma ucuysa " +
            "ParentEndpointCatalog.ReadEndpoints'e, değilse gerekçesiyle ParentEndpointCatalog.ParentReachableAllowlist'e ekleyin: " +
            string.Join(", ", unknown));

        // Allowlist bayatlamasın: listedeki her uç gerçekten veliye açık olmalı.
        var stale = ParentEndpointCatalog.ParentReachableAllowlist.Keys.Where(k => !reachable.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        stale.ShouldBeEmpty("Allowlist'te olup veliye artık açık olmayan uç(lar): " + string.Join(", ", stale));

        // Matristeki okuma uçları da veliye açık olmalı (aksi halde matris yanlış ucu test ediyor).
        foreach (var endpoint in ParentEndpointCatalog.ReadEndpoints)
            reachable.ShouldContain(endpoint.Action);
    }

    private async Task<HashSet<string>> ParentReachableEndpointsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var policyProvider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = services.GetRequiredService<IAuthorizationService>();

        // Gerçek token'ın KeycloakRoleTransformer sonrası hali: sub + realm_access + yalnızca Parent rol claim'i.
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "kc-reverse-parent"),
            new Claim("preferred_username", "kc-reverse-parent"),
            new Claim("realm_access", "{\"roles\":[\"Parent\"]}"),
            new Claim(ClaimTypes.Role, "Parent"),
        ], TestAuthHandler.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);

        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        endpoints.Count.ShouldBeGreaterThan(100); // gerçekten uygulamanın uçları okundu

        foreach (var endpoint in endpoints)
        {
            var key = EndpointKey(endpoint);
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
            {
                reachable.Add(key);
                continue;
            }

            var policy = await AuthorizationPolicy.CombineAsync(
                policyProvider,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
            policy ??= await policyProvider.GetFallbackPolicyAsync();
            if (policy == null)
            {
                reachable.Add(key); // yetki metadata'sı yok ve fallback policy yok → herkese açık
                continue;
            }

            var httpContext = new DefaultHttpContext { RequestServices = services, User = principal };
            httpContext.SetEndpoint(endpoint);
            if ((await authorization.AuthorizeAsync(principal, httpContext, policy)).Succeeded)
                reachable.Add(key);
        }

        return reachable;
    }

    /// <summary>Controller aksiyonu → <c>Controller.Action</c>; diğer uçlar → <c>route:</c> + şablon.</summary>
    private static string EndpointKey(RouteEndpoint endpoint)
        => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
            ? $"{action.ControllerTypeInfo.Name}.{action.MethodInfo.Name}"
            : $"route:/{endpoint.RoutePattern.RawText?.TrimStart('/')}";

    // ---- matris

    public static TheoryData<string> ReadEndpointKeys()
    {
        var data = new TheoryData<string>();
        foreach (var endpoint in ParentEndpointCatalog.ReadEndpoints)
            data.Add(endpoint.Action);
        return data;
    }

    /// <summary>
    /// A: P'nin (Active) çocuğu — A'nın birincil velisi U (#436; koparmayı o yapar); B: Q'nun çocuğu; C: T'ye bağlı ama
    /// soft-delete; R A'da Pending (ikinci veli isteği), S A'da Revoked.
    /// </summary>
    private sealed record World(
        int StudentA, int StudentB, int StudentC, int InstanceA, int InstanceB, int InstanceC, int LinkPA,
        int ParentP, int StudentUserA, int StudentUserB, int StudentUserC,
        int UserP, int UserQ, int UserR, int UserS, int UserT, int UserTeacher, int UserAdmin, int UserPrimaryU);

    private async Task<World> SeedWorldAsync(int baseUser)
    {
        var w = new
        {
            A = baseUser + 1, B = baseUser + 2, C = baseUser + 3,
            P = baseUser + 4, Q = baseUser + 5, R = baseUser + 6, S = baseUser + 7, T = baseUser + 8,
            Teacher = baseUser + 9, Admin = baseUser + 10, U = baseUser + 11
        };
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        foreach (var (id, name) in new[] { (w.A, "Ayşe Kaya"), (w.B, "Rakip Öğrenci"), (w.C, "Silinmiş Öğrenci"), (w.Teacher, "Zeynep Hoca") })
            directory.Add(new() { Id = id, KeycloakId = $"kc-{id}", FullName = name, Email = $"secret-{id}@x.com" });
        foreach (var p in new[] { w.P, w.Q, w.R, w.S, w.T, w.U })
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}" });

        return await WithDbAsync(async db =>
        {
            var school = new School { Name = "Atatürk Ortaokulu" };
            var grade = new Grade { Name = "7. Sınıf" };
            db.AddRange(school, grade);
            await db.SaveChangesAsync();

            Student NewStudent(int userId) => new()
            {
                UserId = userId, StudentNumber = $"P{userId}", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = grade.Id
            };
            var a = NewStudent(w.A);
            var b = NewStudent(w.B);
            var c = NewStudent(w.C);
            db.Students.AddRange(a, b, c);
            var parents = new[] { w.P, w.Q, w.R, w.S, w.T, w.U }.Select(u => new Parent { UserId = u }).ToArray();
            db.Parents.AddRange(parents);
            await db.SaveChangesAsync();
            var (p, q, r, s, t, u) = (parents[0], parents[1], parents[2], parents[3], parents[4], parents[5]);

            var now = DateTime.UtcNow;
            // #436: her öğrencinin tek Active birincil velisi; diğerleri birincil velinin onayladığı / bekleyen ikinci veli istekleri.
            ParentStudentLink Link(Parent parent, Student student, ParentStudentLinkStatus status, bool primary = false) => new()
            {
                ParentId = parent.Id, StudentId = student.Id, Status = status, CreatedAt = now.AddDays(-2),
                Origin = primary ? ParentStudentLinkOrigin.ParentCreated : ParentStudentLinkOrigin.InviteCode, IsPrimary = primary,
                ActivatedAt = status == ParentStudentLinkStatus.Pending ? null : now.AddDays(-1),
                RevokedAt = status == ParentStudentLinkStatus.Revoked ? now.AddHours(-1) : null,
                RevokedByUserId = status == ParentStudentLinkStatus.Revoked ? w.A : null
            };
            var linkPA = Link(p, a, ParentStudentLinkStatus.Active);
            db.ParentStudentLinks.AddRange(
                linkPA,
                Link(u, a, ParentStudentLinkStatus.Active, primary: true),
                Link(q, b, ParentStudentLinkStatus.Active, primary: true),
                Link(r, a, ParentStudentLinkStatus.Pending),
                Link(s, a, ParentStudentLinkStatus.Revoked),
                Link(t, c, ParentStudentLinkStatus.Active, primary: true));

            // Her öğrencinin bitmiş bir test oturumu (test sonucu ucu için).
            var worksheet = new Worksheet { Name = "Kesirler", Description = "", GradeId = grade.Id };
            db.Worksheets.Add(worksheet);
            await db.SaveChangesAsync();
            db.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheet.Id, GradeId = grade.Id, SchoolId = school.Id, StartAt = now.AddDays(-3), EndAt = now.AddDays(3)
            });
            WorksheetInstance Done(Student student) => new()
            {
                StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = now.AddMinutes(-30), EndTime = now.AddMinutes(-10),
                Status = WorksheetInstanceStatus.Completed
            };
            var (ia, ib, ic) = (Done(a), Done(b), Done(c));
            db.TestInstances.AddRange(ia, ib, ic);
            await db.SaveChangesAsync();

            // C soft-delete (bağlantısı Active kalır — kapı silinmiş tarafı yine de elemeli).
            c.IsDeleted = true;
            await db.SaveChangesAsync();

            return new World(a.Id, b.Id, c.Id, ia.Id, ib.Id, ic.Id, linkPA.Id, p.Id, w.A, w.B, w.C,
                w.P, w.Q, w.R, w.S, w.T, w.Teacher, w.Admin, w.U);
        });
    }

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private Task<int> AuditCountAsync() => WithDbAsync(db => db.ParentAccessAudits.CountAsync());

    private static async Task ShouldBeChildNotFoundAsync(HttpResponseMessage response, string because)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, because);
        response.Headers.CacheControl.ShouldNotBeNull($"{because}: 404 de önbelleğe alınmamalı").NoStore.ShouldBeTrue(because);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.NotFound, because);
        // 404 gövdesi hangi koşulun tutmadığını söylemez: yalnızca mesaj + kod.
        string.Join(",", doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal))
            .ShouldBe("errorCode,message", because);
    }

    [Theory]
    [MemberData(nameof(ReadEndpointKeys))]
    public async Task Parent_endpoint_authorization_matrix(string action)
    {
        var endpoint = ParentEndpointCatalog.Get(action);
        var index = ParentEndpointCatalog.ReadEndpoints.ToList().IndexOf(endpoint);
        // Uç başına ayrı kullanıcı aralığı: süreç içi rate limit sayacı (sub başına) koşular arasında paylaşılmasın.
        var world = await SeedWorldAsync(48_000 + index * 20);

        var p = await ParentAsync(world.UserP);
        var q = await ParentAsync(world.UserQ);
        var r = await ParentAsync(world.UserR);
        var s = await ParentAsync(world.UserS);
        var t = await ParentAsync(world.UserT);
        var studentA = await ClientAsAsync(world.StudentUserA, "Student", $"kc-{world.StudentUserA}", "Student");
        var teacher = await ClientAsAsync(world.UserTeacher, "Teacher", $"kc-{world.UserTeacher}", "Teacher");
        var admin = await ClientAsAsync(world.UserAdmin, "Admin", $"kc-{world.UserAdmin}", "Admin");

        var urlA = endpoint.For(world.StudentA, world.InstanceA);

        // ---- yanlış rol → 403, anonim → 401 (veri ve audit yok)
        foreach (var (client, role) in new[] { (studentA, "Student"), (teacher, "Teacher"), (admin, "Admin") })
            (await client.GetAsync(urlA)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{action} as {role}");
        (await Anonymous().GetAsync(urlA)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{action} anonymous");

        if (endpoint.Scope == ParentEndpointScope.Child)
            await AssertChildScopedDenialsAsync(endpoint, world, p, q, r, s, t);
        else
            await AssertParentOnlyListAsync(endpoint, world, q, r, s, t);

        (await AuditCountAsync()).ShouldBe(0, $"{action}: reddedilen hiçbir istek audit yazmamalı");

        // ---- pozitif kontrol: bağlı veli
        var ok = await p.GetAsync(urlA);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, action);
        ok.Headers.CacheControl!.NoStore.ShouldBeTrue(action);
        if (endpoint.Scope == ParentEndpointScope.Child)
        {
            var audit = (await WithDbAsync(db => db.ParentAccessAudits.AsNoTracking().ToListAsync())).ShouldHaveSingleItem();
            audit.ParentId.ShouldBe(world.ParentP);
            audit.StudentId.ShouldBe(world.StudentA);
            audit.Endpoint.ShouldBe(endpoint.AuditEndpoint);
        }
        else
        {
            (await AuditCountAsync()).ShouldBe(0, "çocuk listesi audit'lenmez (çocuk verisi değil, bağlantı listesi)");
            var children = JsonSerializer.Deserialize<List<LinkedChildDto>>(await ok.Content.ReadAsStringAsync(), Json)!;
            children.ShouldContain(c => c.StudentId == world.StudentA && c.Status == "Active");
        }
        var auditsBeforeRevoke = await AuditCountAsync();

        // ---- koparma erişimi ANINDA kapatır: bir sonraki istek 404 (ya da listede yok); sunucu önbelleği yok
        // #436: öğrenci koparamaz; A'nın birincil velisi (U) P'nin bağlantısını koparır.
        (await studentA.PostAsync($"/api/parent-links/{world.LinkPA}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var primaryU = await ParentAsync(world.UserPrimaryU);
        (await primaryU.PostAsync($"/api/parent-links/{world.LinkPA}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await p.GetAsync(urlA);
        if (endpoint.Scope == ParentEndpointScope.Child)
        {
            await ShouldBeChildNotFoundAsync(after, $"{action} after revoke");
        }
        else
        {
            after.StatusCode.ShouldBe(HttpStatusCode.OK);
            after.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var children = JsonSerializer.Deserialize<List<LinkedChildDto>>(await after.Content.ReadAsStringAsync(), Json)!;
            children.ShouldNotContain(c => c.StudentId == world.StudentA, "koparılan çocuk listede kalmamalı");
        }
        (await AuditCountAsync()).ShouldBe(auditsBeforeRevoke, $"{action}: koparma sonrası istek audit yazmamalı");
    }

    private async Task AssertChildScopedDenialsAsync(
        ParentReadEndpoint endpoint, World world, HttpClient p, HttpClient q, HttpClient r, HttpClient s, HttpClient t)
    {
        var action = endpoint.Action;
        var urlA = endpoint.For(world.StudentA, world.InstanceA);

        // Başka velinin çocuğu (iki yön).
        await ShouldBeChildNotFoundAsync(await q.GetAsync(urlA), $"{action}: other parent's child (Q→A)");
        await ShouldBeChildNotFoundAsync(await p.GetAsync(endpoint.For(world.StudentB, world.InstanceB)), $"{action}: other parent's child (P→B)");
        // Bağlantı durumu.
        await ShouldBeChildNotFoundAsync(await r.GetAsync(urlA), $"{action}: pending link");
        await ShouldBeChildNotFoundAsync(await s.GetAsync(urlA), $"{action}: revoked link");
        // Soft-delete edilmiş öğrenci (bağlantısı Active).
        await ShouldBeChildNotFoundAsync(await t.GetAsync(endpoint.For(world.StudentC, world.InstanceC)), $"{action}: soft-deleted student");
        // Olmayan öğrenci.
        await ShouldBeChildNotFoundAsync(await p.GetAsync(endpoint.For(999_999, world.InstanceA)), $"{action}: unknown student");

        if (endpoint.AuditEndpoint == ParentAccessEndpoints.ChildTestResult)
        {
            // Nesne seviyesinde IDOR: kendi çocuğunun yolunda BAŞKA öğrencinin oturumu → 404 (çocuk kapısı geçer, audit yazılır
            // ama sonuç dönmez). Audit satırı bu kontrolden sonra temizlenir ki matrisin "reddedilen istek audit yazmaz"
            // sayımı yalnızca çocuk kapısı retlerini ölçsün.
            var cross = await p.GetAsync(endpoint.For(world.StudentA, world.InstanceB));
            cross.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{action}: another student's test instance");
            cross.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
            var body = await cross.Content.ReadAsStringAsync();
            body.ShouldNotContain("Kesirler");
            await WithDbAsync(db => db.ParentAccessAudits.ExecuteDeleteAsync());
        }
    }

    private static async Task AssertParentOnlyListAsync(
        ParentReadEndpoint endpoint, World world, HttpClient q, HttpClient r, HttpClient s, HttpClient t)
    {
        var url = endpoint.For(0, 0);

        async Task<List<LinkedChildDto>> ListAsync(HttpClient client)
        {
            var response = await client.GetAsync(url);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            return JsonSerializer.Deserialize<List<LinkedChildDto>>(await response.Content.ReadAsStringAsync(), Json)!;
        }

        // Başka velinin çocuğu listede yok.
        (await ListAsync(q)).ShouldNotContain(c => c.StudentId == world.StudentA || c.StudentName == "Ayşe Kaya");
        // Pending: satır var ama öğrenci verisi yok.
        var pending = (await ListAsync(r)).ShouldHaveSingleItem();
        pending.Status.ShouldBe("Pending");
        pending.StudentId.ShouldBeNull();
        pending.StudentName.ShouldBeNull();
        pending.SchoolName.ShouldBeNull();
        pending.GradeName.ShouldBeNull();
        // Revoked: hiç görünmez.
        (await ListAsync(s)).ShouldBeEmpty();
        // Soft-delete edilmiş öğrenci: görünmez.
        (await ListAsync(t)).ShouldNotContain(c => c.StudentId == world.StudentC || c.StudentName == "Silinmiş Öğrenci");
    }
}
