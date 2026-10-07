using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #419 (+ review) — veli–öğrenci bağlantısı uçları gerçek Postgres'te: kod üret → redeem (Pending, öğrenci verisi yok)
/// → öğrenci onayı (Active + event) → listeler (JSON sözleşmesi), ret, iki taraftan koparma, IDOR (başkasının bağlantısı 404,
/// yanlış rol 403), aynı kodun eşzamanlı kullanımı (advisory lock + tek kullanım), genel hata metni, veli başına dakikada 5
/// deneme ve filtreli tekil index'ler.
/// </summary>
public class ParentLinkEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private sealed record Seeded(int StudentId, int[] ParentIds);

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
            return new Seeded(student.Id, parents.Select(p => p.Id).ToArray());
        });
    }

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private Task<HttpClient> ParentAsync(int userId) => ClientAsAsync(userId, "Parent", $"kc-{userId}", "Parent");

    private static async Task<string> NewCodeAsync(HttpClient student)
    {
        var response = await student.PostAsync("/api/parent-links/invite-code", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var expiresAt = doc.RootElement.GetProperty("expiresAt").GetDateTime();
        expiresAt.ShouldBeInRange(DateTime.UtcNow.AddHours(47), DateTime.UtcNow.AddHours(49));
        var code = doc.RootElement.GetProperty("code").GetString()!;
        code.Length.ShouldBe(12);
        return code;
    }

    /// <summary>Kod → redeem (Pending) → öğrenci onayı. Bağlantı id'sini döner.</summary>
    private static async Task<int> LinkAsync(HttpClient student, HttpClient parent)
    {
        var redeem = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = await NewCodeAsync(student) });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        var linkId = (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.LinkId;
        (await student.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return linkId;
    }

    [Fact]
    public async Task Redeem_creates_pending_request_student_approves_then_both_lists_and_event()
    {
        const int studentUser = 41901, parentUser = 41902;
        var seeded = await SeedAsync(studentUser, parentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);

        var code = await NewCodeAsync(student);
        var formatted = $"{code[..4]}-{code[4..8]}-{code[8..]}".ToLowerInvariant();
        var redeem = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = formatted });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        int linkId;
        using (var doc = JsonDocument.Parse(await redeem.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("status").GetString().ShouldBe("Pending");
            // Onaydan önce öğrenciye ait hiçbir veri dönmez.
            doc.RootElement.GetProperty("studentName").ValueKind.ShouldBe(JsonValueKind.Null);
            doc.RootElement.GetProperty("gradeName").ValueKind.ShouldBe(JsonValueKind.Null);
            doc.RootElement.GetProperty("schoolName").ValueKind.ShouldBe(JsonValueKind.Null);
            linkId = doc.RootElement.GetProperty("linkId").GetInt32();
        }
        (await WithDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);

        using (var doc = JsonDocument.Parse(await student.GetStringAsync("/api/parent-links/my-parents")))
        {
            doc.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
            var pending = doc.RootElement.GetProperty("pendingRequests");
            pending.GetArrayLength().ShouldBe(1);
            pending[0].GetProperty("parentName").GetString().ShouldBe($"Veli {parentUser}");
            pending[0].GetProperty("linkId").GetInt32().ShouldBe(linkId);
        }

        (await student.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var child = (await parent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldHaveSingleItem();
        child.Status.ShouldBe("Active");
        child.StudentName.ShouldBe("Ayşe Kaya");
        child.GradeName.ShouldBe("7. Sınıf");
        child.SchoolName.ShouldBe("Atatürk Ortaokulu");

        using (var doc = JsonDocument.Parse(await student.GetStringAsync("/api/parent-links/my-parents")))
        {
            var items = doc.RootElement.GetProperty("items");
            items.GetArrayLength().ShouldBe(1);
            items[0].GetProperty("parentName").GetString().ShouldBe($"Veli {parentUser}");
            // Yalnızca ad: e-posta/kullanıcı id'si sızmaz.
            items[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .ShouldBe(new[] { "linkId", "linkedAt", "parentName" });
            doc.RootElement.GetProperty("maxActiveParents").GetInt32().ShouldBe(4);
            doc.RootElement.GetProperty("activeInviteExpiresAt").ValueKind.ShouldBe(JsonValueKind.Null);
        }

        var (codes, outbox) = await WithDbAsync(async db => (
            await db.ParentInviteCodes.AsNoTracking().ToListAsync(),
            await db.OutboxMessages.AsNoTracking().ToListAsync()));
        codes.ShouldHaveSingleItem().CodeHash.ShouldNotContain(code, Case.Insensitive);
        var evt = JsonSerializer.Deserialize<ParentLinkedEvent>(outbox.ShouldHaveSingleItem().Content)!;
        outbox[0].Type.ShouldBe(OutboxEventRegistry.NameFor<ParentLinkedEvent>());
        evt.StudentId.ShouldBe(seeded.StudentId);
        evt.ParentId.ShouldBe(seeded.ParentIds[0]);
    }

    [Fact]
    public async Task Student_rejects_pending_request_and_parent_never_sees_student_data()
    {
        const int studentUser = 41951, parentUser = 41952;
        await SeedAsync(studentUser, parentUser);
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);

        var redeem = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = await NewCodeAsync(student) });
        var linkId = (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.LinkId;

        (await parent.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsync($"/api/parent-links/{linkId}/reject", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await student.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await parent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldBeEmpty();
        (await WithDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_redeems_of_the_same_code_create_exactly_one_request()
    {
        const int studentUser = 41911;
        var parentUsers = Enumerable.Range(41912, 4).ToArray();
        await SeedAsync(studentUser, parentUsers);
        var code = await NewCodeAsync(await StudentAsync(studentUser));
        var parents = await Task.WhenAll(parentUsers.Select(ParentAsync));

        var responses = await Task.WhenAll(parents.Select(p => p.PostAsJsonAsync("/api/parent-links/redeem", new { code })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).ShouldBe(3);
        (await WithDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_code_gets_generic_error_and_attempts_are_limited_to_five_per_minute()
    {
        const int studentUser = 41921, parentUser = 41922;
        await SeedAsync(studentUser, parentUser);
        var parent = await ParentAsync(parentUser);

        for (var i = 0; i < 5; i++)
        {
            var bad = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = i % 2 == 0 ? "ZZZZZZZZZZZZ" : "nope" });
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await bad.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.InvalidCode);
            doc.RootElement.GetProperty("message").GetString().ShouldBe("Kod geçersiz veya süresi dolmuş.");
        }

        // Altıncı deneme (doğru kod olsa bile) 429.
        var code = await NewCodeAsync(await StudentAsync(studentUser));
        (await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code })).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await WithDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Revoke_by_either_side_ends_access_and_strangers_get_404()
    {
        const int studentUser = 41931, parentUser = 41932, otherParentUser = 41933, otherStudentUser = 41934;
        await SeedAsync(studentUser, parentUser, otherParentUser);
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = otherStudentUser, StudentNumber = "P41934" });
            await db.SaveChangesAsync();
        });
        var student = await StudentAsync(studentUser);
        var parent = await ParentAsync(parentUser);
        var otherParent = await ParentAsync(otherParentUser);
        var otherStudent = await StudentAsync(otherStudentUser);
        var teacher = await ClientAsAsync(41935, "Teacher", "kc-41935", "Teacher");

        var first = await LinkAsync(student, parent);
        var second = await LinkAsync(student, otherParent);

        // IDOR: başka veli / başka öğrenci 404, yanlış rol 403; hiçbir şey değişmez.
        (await otherParent.PostAsync($"/api/parent-links/{first}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherStudent.PostAsync($"/api/parent-links/{first}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherStudent.PostAsync($"/api/parent-links/{first}/reject", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacher.PostAsync($"/api/parent-links/{first}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.GetAsync("/api/parent-links/my-children")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync("/api/parent-links/redeem", new { code = "ABCDEFGHJKMN" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await parent.PostAsync("/api/parent-links/invite-code", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.ParentStudentLinks.CountAsync(l => l.Status == ParentStudentLinkStatus.Active))).ShouldBe(2);

        // Veli koparır → çocuk listesi boşalır; öğrenci diğerini koparır → veli listesi boşalır.
        (await parent.PostAsync($"/api/parent-links/{first}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await parent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldBeEmpty();
        (await student.PostAsync($"/api/parent-links/{second}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await otherParent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldBeEmpty();
        (await student.GetFromJsonAsync<StudentParentLinksDto>("/api/parent-links/my-parents", Json))!.Items.ShouldBeEmpty();

        var links = await WithDbAsync(db => db.ParentStudentLinks.AsNoTracking().OrderBy(l => l.Id).ToListAsync());
        links.ShouldAllBe(l => l.Status == ParentStudentLinkStatus.Revoked && l.RevokedAt != null);
        links[0].RevokedByUserId.ShouldBe(parentUser);
        links[1].RevokedByUserId.ShouldBe(studentUser);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>())))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Student_limit_counts_pending_requests_and_blocks_new_codes()
    {
        const int studentUser = 41941;
        var parentUsers = Enumerable.Range(41942, 4).ToArray();
        await SeedAsync(studentUser, parentUsers);
        var student = await StudentAsync(studentUser);

        foreach (var p in parentUsers)
        {
            var client = await ParentAsync(p);
            (await client.PostAsJsonAsync("/api/parent-links/redeem", new { code = await NewCodeAsync(student) }))
                .StatusCode.ShouldBe(HttpStatusCode.OK); // 4 bekleyen istek
        }

        var full = await student.PostAsync("/api/parent-links/invite-code", null);
        full.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await full.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.StudentLimitReached);
    }

    [Fact]
    public async Task Partial_unique_indexes_reject_duplicate_open_pair_and_duplicate_unused_code_hash()
    {
        const int studentUser = 41961, parentUser = 41962;
        var seeded = await SeedAsync(studentUser, parentUser);
        var now = DateTime.UtcNow;

        await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Pending, CreatedAt = now },
                new ParentStudentLink { ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Active, CreatedAt = now });
            await db.SaveChangesAsync();
        }));

        // Revoked satırlar index dışında: aynı çift için geçmiş + yeni açık satır birlikte durabilir.
        await WithDbAsync(async db =>
        {
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Revoked, CreatedAt = now },
                new ParentStudentLink { ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Active, CreatedAt = now });
            await db.SaveChangesAsync();
        });

        var hash = new string('a', 64);
        await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.ParentInviteCodes.AddRange(
                new ParentInviteCode { StudentId = seeded.StudentId, CodeHash = hash, CreatedAt = now, ExpiresAt = now.AddHours(1) },
                new ParentInviteCode { StudentId = seeded.StudentId, CodeHash = hash, CreatedAt = now, ExpiresAt = now.AddHours(1) });
            await db.SaveChangesAsync();
        }));

        // Kullanılmış kodla aynı hash'e sahip yeni kullanılmamış kod yazılabilir.
        await WithDbAsync(async db =>
        {
            db.ParentInviteCodes.AddRange(
                new ParentInviteCode { StudentId = seeded.StudentId, CodeHash = hash, CreatedAt = now, ExpiresAt = now, UsedAt = now },
                new ParentInviteCode { StudentId = seeded.StudentId, CodeHash = hash, CreatedAt = now, ExpiresAt = now.AddHours(1) });
            await db.SaveChangesAsync();
        });
    }

    private static async Task<int> RequestAsync(HttpClient student, HttpClient parent)
    {
        var redeem = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = await NewCodeAsync(student) });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.LinkId;
    }

    private Task<(ParentStudentLinkStatus Status, int Linked, int Unlinked)> LinkStateAsync(int linkId) => WithDbAsync(async db =>
    {
        var status = await db.ParentStudentLinks.Where(l => l.Id == linkId).Select(l => l.Status).SingleAsync();
        var events = await db.OutboxMessages.AsNoTracking().ToListAsync();
        bool For(string content) => content.Contains($"\"LinkId\":{linkId},", StringComparison.Ordinal);
        return (status,
            events.Count(e => e.Type == OutboxEventRegistry.NameFor<ParentLinkedEvent>() && For(e.Content)),
            events.Count(e => e.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>() && For(e.Content)));
    });

    /// <summary>
    /// Re-review A/B: aynı Pending bağlantıda eşzamanlı onay + veli iptali. Her sıralamada HTTP sonucu ile DB/outbox tutarlı:
    /// onay önce → koparma Active olarak tekrarlanır (Linked + Unlinked event, Revoked); iptal önce → onay 404, event yok.
    /// </summary>
    [Fact]
    public async Task Concurrent_approve_and_revoke_on_a_pending_link_end_consistently()
    {
        const int studentUser = 41971;
        var parentUsers = Enumerable.Range(41972, 4).ToArray();
        await SeedAsync(studentUser, parentUsers);
        var student = await StudentAsync(studentUser);

        foreach (var p in parentUsers)
        {
            var parent = await ParentAsync(p);
            var linkId = await RequestAsync(student, parent);

            var approve = student.PostAsync($"/api/parent-links/{linkId}/approve", null);
            var revoke = parent.PostAsync($"/api/parent-links/{linkId}/revoke", null);
            await Task.WhenAll(approve, revoke);

            (await revoke).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            var state = await LinkStateAsync(linkId);
            state.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
            if ((await approve).StatusCode == HttpStatusCode.NoContent)
            {
                state.Linked.ShouldBe(1);
                state.Unlinked.ShouldBe(1); // onaylanmış bağlantının koparılması bildirilir
            }
            else
            {
                (await approve).StatusCode.ShouldBe(HttpStatusCode.NotFound);
                state.Linked.ShouldBe(0);
                state.Unlinked.ShouldBe(0);
            }
        }
    }

    /// <summary>Re-review A/B: eşzamanlı onay + ret — tam olarak biri kazanır; kaybeden 404 alır, DB kazananı yansıtır.</summary>
    [Fact]
    public async Task Concurrent_approve_and_reject_on_a_pending_link_have_exactly_one_winner()
    {
        const int studentUser = 41981;
        var parentUsers = Enumerable.Range(41982, 4).ToArray();
        await SeedAsync(studentUser, parentUsers);
        var student = await StudentAsync(studentUser);

        foreach (var p in parentUsers)
        {
            var parent = await ParentAsync(p);
            var linkId = await RequestAsync(student, parent);

            var approve = student.PostAsync($"/api/parent-links/{linkId}/approve", null);
            var reject = student.PostAsync($"/api/parent-links/{linkId}/reject", null);
            await Task.WhenAll(approve, reject);

            var approved = (await approve).StatusCode;
            var rejected = (await reject).StatusCode;
            new[] { approved, rejected }.Count(c => c == HttpStatusCode.NoContent).ShouldBe(1);
            new[] { approved, rejected }.Count(c => c == HttpStatusCode.NotFound).ShouldBe(1);

            var state = await LinkStateAsync(linkId);
            if (approved == HttpStatusCode.NoContent)
            {
                state.Status.ShouldBe(ParentStudentLinkStatus.Active);
                state.Linked.ShouldBe(1);
                // tavanı boşalt (sonraki tur için)
                (await student.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }
            else
            {
                state.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
                state.Linked.ShouldBe(0);
            }
        }
    }

    /// <summary>Re-review madde 8: boş gövde / boş / çok uzun kod ProblemDetails değil genel InvalidCode gövdesi alır.</summary>
    [Fact]
    public async Task Empty_or_oversized_code_returns_the_generic_invalid_code_shape()
    {
        const int studentUser = 41991, parentUser = 41992;
        await SeedAsync(studentUser, parentUser);
        var parent = await ParentAsync(parentUser);

        var responses = new[]
        {
            await parent.PostAsync("/api/parent-links/redeem", null),
            await parent.PostAsJsonAsync("/api/parent-links/redeem", new { }),
            await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = "" }),
            await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = new string('A', 300) }),
        };

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.InvalidCode);
            doc.RootElement.GetProperty("message").GetString().ShouldBe("Kod geçersiz veya süresi dolmuş.");
        }
    }
}
