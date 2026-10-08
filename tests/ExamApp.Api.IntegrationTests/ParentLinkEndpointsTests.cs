using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #436 (#419 üzerine) — veli-öncelikli bağlantı uçları gerçek Postgres'te: birincil veli ikinci veli kodu üretir →
/// ikinci veli redeem (Pending, öğrenci verisi yok) → birincil veli onayı (Active + event) → listeler (JSON sözleşmesi);
/// öğrenci salt okunur (kod üretemez, yeni istek onaylayamaz, koparamaz), birincil olmayan veli 403, yabancı 404, yanlış rol
/// 403; admin koparması + denetim + birincil devri; geçiş dönemi öğrenci onayı; aynı kodun eşzamanlı kullanımı (advisory lock
/// + tek kullanım), eşzamanlı onay/iptal/ret, genel hata metni, veli başına dakikada 5 deneme ve filtreli tekil index'ler.
/// </summary>
public class ParentLinkEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private sealed record Seeded(int StudentId, int[] ParentIds);

    private async Task<Seeded> SeedAsync(int studentUserId, params int[] parentUserIds)
    {
        var directory = Factory.Services.GetRequiredService<FakeUserDirectory>();
        directory.Add(new() { Id = studentUserId, KeycloakId = $"kc-{studentUserId}", FullName = "Ayşe Kaya" });
        foreach (var p in parentUserIds)
            directory.Add(new() { Id = p, KeycloakId = $"kc-{p}", FullName = $"Veli {p}", Email = $"veli{p}@gmail.com" });

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

    private static async Task<string> NewCodeAsync(HttpClient primary, int primaryLinkId)
    {
        var response = await primary.PostAsync($"/api/parent-links/{primaryLinkId}/second-parent-code", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var expiresAt = doc.RootElement.GetProperty("expiresAt").GetDateTime();
        expiresAt.ShouldBeInRange(DateTime.UtcNow.AddDays(7).AddMinutes(-5), DateTime.UtcNow.AddDays(7).AddMinutes(5));
        var code = doc.RootElement.GetProperty("code").GetString()!;
        code.Length.ShouldBe(12);
        return code;
    }

    /// <summary>Birincil velinin kodu → ikinci veli redeem (Pending). Bağlantı id'sini döner.</summary>
    private static async Task<int> RequestAsync(HttpClient primary, int primaryLinkId, HttpClient parent)
    {
        var redeem = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = await NewCodeAsync(primary, primaryLinkId) });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await redeem.Content.ReadFromJsonAsync<LinkedChildDto>(Json))!.LinkId;
    }

    /// <summary>Kod → redeem (Pending) → birincil veli onayı (Active).</summary>
    private static async Task<int> LinkSecondAsync(HttpClient primary, int primaryLinkId, HttpClient parent)
    {
        var linkId = await RequestAsync(primary, primaryLinkId, parent);
        (await primary.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return linkId;
    }

    [Fact]
    public async Task Primary_parent_invites_second_parent_who_waits_for_primary_approval_then_lists_and_event()
    {
        const int studentUser = 41901, primaryUser = 41902, parentUser = 41903;
        var seeded = await SeedAsync(studentUser, primaryUser, parentUser);
        var student = await StudentAsync(studentUser);
        var primary = await ParentAsync(primaryUser);
        var parent = await ParentAsync(parentUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);

        var code = await NewCodeAsync(primary, primaryLink);
        var formatted = $"{code[..4]}-{code[4..8]}-{code[8..]}".ToLowerInvariant();
        var redeem = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = formatted });
        redeem.StatusCode.ShouldBe(HttpStatusCode.OK);
        int linkId;
        using (var doc = JsonDocument.Parse(await redeem.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("status").GetString().ShouldBe("Pending");
            // Onaydan önce öğrenciye ait hiçbir veri dönmez.
            doc.RootElement.GetProperty("studentId").ValueKind.ShouldBe(JsonValueKind.Null);
            doc.RootElement.GetProperty("studentName").ValueKind.ShouldBe(JsonValueKind.Null);
            doc.RootElement.GetProperty("gradeName").ValueKind.ShouldBe(JsonValueKind.Null);
            doc.RootElement.GetProperty("schoolName").ValueKind.ShouldBe(JsonValueKind.Null);
            linkId = doc.RootElement.GetProperty("linkId").GetInt32();
        }
        (await WithDbAsync(db => db.OutboxMessages.CountAsync())).ShouldBe(0);

        // Yeni istek öğrenciye düşmez; öğrenci onay ucu yeni bağlantıya kapalı.
        using (var doc = JsonDocument.Parse(await student.GetStringAsync("/api/parent-links/my-parents")))
            doc.RootElement.GetProperty("pendingRequests").GetArrayLength().ShouldBe(0);
        (await student.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Birincil veli isteği (ad + maskeli e-posta) görür ve onaylar.
        var mine = (await primary.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldHaveSingleItem();
        mine.IsPrimary.ShouldBeTrue();
        var request = mine.CoParents.ShouldHaveSingleItem();
        request.LinkId.ShouldBe(linkId);
        request.Status.ShouldBe("Pending");
        request.ParentName.ShouldBe($"Veli {parentUser}");
        request.ParentEmailMasked.ShouldBe("v***@g***.com");
        (await primary.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var child = (await parent.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldHaveSingleItem();
        child.Status.ShouldBe("Active");
        child.StudentId.ShouldBe(seeded.StudentId);
        child.StudentName.ShouldBe("Ayşe Kaya");
        child.GradeName.ShouldBe("7. Sınıf");
        child.SchoolName.ShouldBe("Atatürk Ortaokulu");
        child.IsPrimary.ShouldBeFalse();
        child.CoParents.ShouldBeEmpty();

        using (var doc = JsonDocument.Parse(await student.GetStringAsync("/api/parent-links/my-parents")))
        {
            var items = doc.RootElement.GetProperty("items");
            items.GetArrayLength().ShouldBe(2);
            items[0].GetProperty("parentName").GetString().ShouldBe($"Veli {primaryUser}");
            items[0].GetProperty("isPrimary").GetBoolean().ShouldBeTrue();
            items[1].GetProperty("isPrimary").GetBoolean().ShouldBeFalse();
            // Yalnızca ad: e-posta/kullanıcı id'si sızmaz.
            items[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .ShouldBe(new[] { "isPrimary", "linkId", "linkedAt", "parentName" });
            doc.RootElement.GetProperty("maxActiveParents").GetInt32().ShouldBe(4);
            doc.RootElement.GetProperty("requiresParent").GetBoolean().ShouldBeTrue(); // #437
            doc.RootElement.TryGetProperty("activeInviteExpiresAt", out _).ShouldBeFalse(); // öğrenci kodu yok
        }

        var (codes, outbox) = await WithDbAsync(async db => (
            await db.ParentInviteCodes.AsNoTracking().ToListAsync(),
            await db.OutboxMessages.AsNoTracking().ToListAsync()));
        var stored = codes.ShouldHaveSingleItem();
        stored.CodeHash.ShouldNotContain(code, Case.Insensitive);
        stored.CreatedByParentId.ShouldBe(seeded.ParentIds[0]);
        var evt = JsonSerializer.Deserialize<ParentLinkedEvent>(outbox.ShouldHaveSingleItem().Content)!;
        outbox[0].Type.ShouldBe(OutboxEventRegistry.NameFor<ParentLinkedEvent>());
        evt.StudentId.ShouldBe(seeded.StudentId);
        evt.ParentId.ShouldBe(seeded.ParentIds[1]);
        var link = await WithDbAsync(db => db.ParentStudentLinks.AsNoTracking().SingleAsync(l => l.Id == linkId));
        link.Origin.ShouldBe(ParentStudentLinkOrigin.InviteCode);
        link.IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task Students_non_primary_parents_strangers_and_other_roles_cannot_manage_links()
    {
        const int studentUser = 41931, primaryUser = 41932, secondUser = 41933, otherParentUser = 41934, otherStudentUser = 41935,
            pendingUser = 41936;
        await SeedAsync(studentUser, primaryUser, secondUser, otherParentUser, pendingUser);
        await WithDbAsync(async db =>
        {
            db.Students.Add(new Student { UserId = otherStudentUser, StudentNumber = "P41935" });
            await db.SaveChangesAsync();
        });
        var student = await StudentAsync(studentUser);
        var primary = await ParentAsync(primaryUser);
        var second = await ParentAsync(secondUser);
        var otherParent = await ParentAsync(otherParentUser);
        var otherStudent = await StudentAsync(otherStudentUser);
        var teacher = await ClientAsAsync(41937, "Teacher", "kc-41937", "Teacher");

        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);
        var secondLink = await LinkSecondAsync(primary, primaryLink, second);
        var pending = await RequestAsync(primary, primaryLink, await ParentAsync(pendingUser));

        // Öğrenci: kod üretme ucu yok, koparamaz (rol), yeni isteği onaylayamaz/reddedemez (404).
        (await student.PostAsync("/api/parent-links/invite-code", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await student.PostAsync($"/api/parent-links/{primaryLink}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsync($"/api/parent-links/{primaryLink}/second-parent-code", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsync($"/api/parent-links/{pending}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await student.PostAsync($"/api/parent-links/{pending}/reject", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Birincil olmayan veli: 403 NotPrimaryParent.
        foreach (var path in new[]
                 {
                     $"/api/parent-links/{pending}/approve", $"/api/parent-links/{pending}/reject",
                     $"/api/parent-links/{primaryLink}/revoke", $"/api/parent-links/{secondLink}/second-parent-code"
                 })
        {
            var response = await second.PostAsync(path, null);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.NotPrimaryParent);
        }

        // IDOR: çocuğa bağlı olmayan veli / başka öğrenci 404; yanlış rol 403.
        foreach (var path in new[]
                 {
                     $"/api/parent-links/{pending}/approve", $"/api/parent-links/{primaryLink}/revoke",
                     $"/api/parent-links/{primaryLink}/second-parent-code"
                 })
            (await otherParent.PostAsync(path, null)).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        (await otherStudent.PostAsync($"/api/parent-links/{pending}/reject", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await teacher.PostAsync($"/api/parent-links/{primaryLink}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.PostAsync($"/api/parent-links/{pending}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await teacher.GetAsync("/api/parent-links/my-children")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync("/api/parent-links/redeem", new { code = "ABCDEFGHJKMN" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await WithDbAsync(db => db.ParentStudentLinks.CountAsync(l => l.Status == ParentStudentLinkStatus.Active))).ShouldBe(2);
        (await WithDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == pending))).Status.ShouldBe(ParentStudentLinkStatus.Pending);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>()))).ShouldBe(0);

        // Birincil veli diğer veliyi koparır → erişim anında biter.
        (await primary.PostAsync($"/api/parent-links/{secondLink}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await second.GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!.ShouldBeEmpty();
        (await student.GetFromJsonAsync<StudentParentLinksDto>("/api/parent-links/my-parents", Json))!.Items.Count.ShouldBe(1);
        var unlinked = JsonSerializer.Deserialize<ParentUnlinkedEvent>((await WithDbAsync(db => db.OutboxMessages
            .SingleAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>()))).Content)!;
        unlinked.RevokedByRole.ShouldBe("PrimaryParent");
        unlinked.RevokedByUserId.ShouldBe(primaryUser);
    }

    [Fact]
    public async Task Admin_removes_the_primary_link_with_audit_and_primary_moves_to_the_oldest_remaining_parent()
    {
        const int studentUser = 41941, primaryUser = 41942, secondUser = 41943, thirdUser = 41944;
        await SeedAsync(studentUser, primaryUser, secondUser, thirdUser);
        var primary = await ParentAsync(primaryUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser, at: DateTime.UtcNow.AddDays(-30));
        var secondLink = await LinkSecondAsync(primary, primaryLink, await ParentAsync(secondUser));
        await LinkSecondAsync(primary, primaryLink, await ParentAsync(thirdUser));
        var admin = await ClientAsAsync(41945, "Admin", "kc-admin-41945", "Admin");

        (await admin.PostAsync($"/api/parent-links/{primaryLink}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/parent-links/{primaryLink + 99_999}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var links = await WithDbAsync(db => db.ParentStudentLinks.AsNoTracking().ToDictionaryAsync(l => l.Id));
        links[primaryLink].Status.ShouldBe(ParentStudentLinkStatus.Revoked);
        links.Values.Single(l => l.IsPrimary && l.Status == ParentStudentLinkStatus.Active).Id.ShouldBe(secondLink);
        var audits = await WithDbAsync(db => db.AdminUserActionLogs.AsNoTracking()
            .Where(a => a.TargetType == AdminUserTargetType.ParentLink).OrderBy(a => a.Id).ToListAsync());
        audits.Count.ShouldBe(2);
        // Yan etkisiz admin denemesi de iz bırakır (security: 404 / no-op).
        audits[1].TargetId.ShouldBe(primaryLink + 99_999);
        audits[1].Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
        var audit = audits[0];
        audit.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        audit.Action.ShouldBe(AdminUserAction.ParentLinkRevoked);
        audit.TargetType.ShouldBe(AdminUserTargetType.ParentLink);
        audit.TargetId.ShouldBe(primaryLink);
        audit.ActorKeycloakId.ShouldBe("kc-admin-41945");

        // Yeni birincil yönetir.
        var second = await ParentAsync(secondUser);
        (await second.PostAsync($"/api/parent-links/{secondLink}/second-parent-code", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await ParentAsync(thirdUser)).GetFromJsonAsync<List<LinkedChildDto>>("/api/parent-links/my-children", Json))!
            .Single().IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task Legacy_pending_request_is_still_approved_by_the_student_during_the_transition()
    {
        const int studentUser = 41951, parentUser = 41952;
        var seeded = await SeedAsync(studentUser, parentUser);
        var student = await StudentAsync(studentUser);
        var linkId = await WithDbAsync(async db =>
        {
            var link = new ParentStudentLink
            {
                ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Pending,
                Origin = ParentStudentLinkOrigin.LegacyV1, CreatedAt = DateTime.UtcNow.AddDays(-12)
            };
            db.ParentStudentLinks.Add(link);
            await db.SaveChangesAsync();
            return link.Id;
        });

        using (var doc = JsonDocument.Parse(await student.GetStringAsync("/api/parent-links/my-parents")))
        {
            var pending = doc.RootElement.GetProperty("pendingRequests");
            pending.GetArrayLength().ShouldBe(1);
            pending[0].GetProperty("linkId").GetInt32().ShouldBe(linkId);
        }
        // Veli eski isteği kendisi onaylayamaz (kendi isteği).
        (await (await ParentAsync(parentUser)).PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await student.PostAsync($"/api/parent-links/{linkId}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var link = await WithDbAsync(db => db.ParentStudentLinks.AsNoTracking().SingleAsync(l => l.Id == linkId));
        link.Status.ShouldBe(ParentStudentLinkStatus.Active);
        link.IsPrimary.ShouldBeTrue();
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentLinkedEvent>()))).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_redeems_of_the_same_code_create_exactly_one_request()
    {
        const int studentUser = 41911, primaryUser = 41916;
        var parentUsers = Enumerable.Range(41912, 4).ToArray();
        await SeedAsync(studentUser, parentUsers.Append(primaryUser).ToArray());
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);
        var code = await NewCodeAsync(await ParentAsync(primaryUser), primaryLink);
        var parents = await Task.WhenAll(parentUsers.Select(ParentAsync));

        var responses = await Task.WhenAll(parents.Select(p => p.PostAsJsonAsync("/api/parent-links/redeem", new { code })));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.BadRequest).ShouldBe(3);
        (await WithDbAsync(db => db.ParentStudentLinks.CountAsync(l => l.Status == ParentStudentLinkStatus.Pending))).ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_code_gets_generic_error_and_attempts_are_limited_to_five_per_minute()
    {
        const int studentUser = 41921, parentUser = 41922, primaryUser = 41923;
        await SeedAsync(studentUser, parentUser, primaryUser);
        var parent = await ParentAsync(parentUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);

        for (var i = 0; i < 5; i++)
        {
            var bad = await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code = i % 2 == 0 ? "ZZZZZZZZZZZZ" : "nope" });
            bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var doc = JsonDocument.Parse(await bad.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.InvalidCode);
            doc.RootElement.GetProperty("message").GetString().ShouldBe("Kod geçersiz veya süresi dolmuş.");
        }

        // Altıncı deneme (doğru kod olsa bile) 429.
        var code = await NewCodeAsync(await ParentAsync(primaryUser), primaryLink);
        (await parent.PostAsJsonAsync("/api/parent-links/redeem", new { code })).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await WithDbAsync(db => db.ParentStudentLinks.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Student_limit_counts_pending_requests_and_blocks_new_codes()
    {
        const int studentUser = 41961, primaryUser = 41962;
        var parentUsers = Enumerable.Range(41963, 3).ToArray();
        await SeedAsync(studentUser, parentUsers.Prepend(primaryUser).ToArray());
        var primary = await ParentAsync(primaryUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);

        foreach (var p in parentUsers)
            await RequestAsync(primary, primaryLink, await ParentAsync(p)); // birincil + 3 bekleyen = 4

        var full = await primary.PostAsync($"/api/parent-links/{primaryLink}/second-parent-code", null);
        full.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await full.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.StudentLimitReached);
    }

    [Fact]
    public async Task Partial_unique_indexes_and_origin_check_reject_duplicate_open_pair_second_primary_unknown_origin_and_duplicate_unused_code_hash()
    {
        const int studentUser = 41971, parentUser = 41972, otherParentUser = 41973;
        var seeded = await SeedAsync(studentUser, parentUser, otherParentUser);
        var now = DateTime.UtcNow;

        var pair = await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Pending, CreatedAt = now },
                new ParentStudentLink { Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Active, CreatedAt = now });
            await db.SaveChangesAsync();
        }));

        pair.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);

        // #436: öğrenci başına tek Active birincil.
        var primaries = await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Active, IsPrimary = true, CreatedAt = now },
                new ParentStudentLink { Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = seeded.ParentIds[1], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Active, IsPrimary = true, CreatedAt = now });
            await db.SaveChangesAsync();
        }));

        primaries.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);

        // Revoked satırlar index dışında: aynı çift için geçmiş (eski birincil dahil) + yeni açık satır birlikte durabilir.
        await WithDbAsync(async db =>
        {
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Revoked, IsPrimary = true, CreatedAt = now },
                new ParentStudentLink { Origin = ParentStudentLinkOrigin.ParentCreated, ParentId = seeded.ParentIds[0], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Active, IsPrimary = true, CreatedAt = now });
            await db.SaveChangesAsync();
        });
        // #436 security MINOR-2: Origin verilmeyen (Unknown) satır CHECK kısıtıyla reddedilir — "eski bağlantı" sayılmaz.
        var unknown = await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.ParentStudentLinks.Add(new ParentStudentLink
            {
                ParentId = seeded.ParentIds[1], StudentId = seeded.StudentId, Status = ParentStudentLinkStatus.Pending, CreatedAt = now
            });
            await db.SaveChangesAsync();
        }));
        unknown.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);

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
    /// #419 re-review A/B (#436 aktörleriyle): aynı Pending bağlantıda birincil velinin eşzamanlı onayı + isteyen velinin iptali.
    /// Her sıralamada HTTP sonucu ile DB/outbox tutarlı: onay önce → iptal, Active bağlantıdan AYRILMA olarak tekrarlanır (veli
    /// kendi bağlantısından ayrılabilir; Linked + Unlinked event, Revoked); iptal önce → onay 404, event yok.
    /// </summary>
    [Fact]
    public async Task Concurrent_primary_approval_and_requester_cancel_end_consistently()
    {
        const int studentUser = 41981, primaryUser = 41986;
        var parentUsers = Enumerable.Range(41982, 4).ToArray();
        await SeedAsync(studentUser, parentUsers.Append(primaryUser).ToArray());
        var primary = await ParentAsync(primaryUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);

        foreach (var p in parentUsers)
        {
            var parent = await ParentAsync(p);
            var linkId = await RequestAsync(primary, primaryLink, parent);

            var approve = primary.PostAsync($"/api/parent-links/{linkId}/approve", null);
            var cancel = parent.PostAsync($"/api/parent-links/{linkId}/revoke", null);
            await Task.WhenAll(approve, cancel);

            var state = await LinkStateAsync(linkId);
            if ((await approve).StatusCode == HttpStatusCode.NoContent)
            {
                (await cancel).StatusCode.ShouldBe(HttpStatusCode.NoContent); // artık Active → ayrılma
                state.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
                state.Linked.ShouldBe(1);
                state.Unlinked.ShouldBe(1);
            }
            else
            {
                (await approve).StatusCode.ShouldBe(HttpStatusCode.NotFound);
                (await cancel).StatusCode.ShouldBe(HttpStatusCode.NoContent);
                state.Status.ShouldBe(ParentStudentLinkStatus.Revoked);
                state.Linked.ShouldBe(0);
                state.Unlinked.ShouldBe(0);
            }
        }
    }

    /// <summary>#419 re-review A/B: birincil velinin eşzamanlı onay + reddi — tam olarak biri kazanır; kaybeden 404 alır.</summary>
    [Fact]
    public async Task Concurrent_approve_and_reject_on_a_pending_link_have_exactly_one_winner()
    {
        const int studentUser = 41991, primaryUser = 41996;
        var parentUsers = Enumerable.Range(41992, 4).ToArray();
        await SeedAsync(studentUser, parentUsers.Append(primaryUser).ToArray());
        var primary = await ParentAsync(primaryUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);

        foreach (var p in parentUsers)
        {
            var linkId = await RequestAsync(primary, primaryLink, await ParentAsync(p));

            var approve = primary.PostAsync($"/api/parent-links/{linkId}/approve", null);
            var reject = primary.PostAsync($"/api/parent-links/{linkId}/reject", null);
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
                (await primary.PostAsync($"/api/parent-links/{linkId}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
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
        const int studentUser = 41997, parentUser = 41998;
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

    /// <summary>issue #436 ürün kararı: birincil olmayan veli kendi bağlantısından ayrılır; öğrencinin tek velisi ayrılamaz (409).</summary>
    [Fact]
    public async Task Non_primary_parent_leaves_and_the_last_parent_gets_409()
    {
        const int studentUser = 41955, primaryUser = 41956, secondUser = 41957;
        await SeedAsync(studentUser, primaryUser, secondUser);
        var primary = await ParentAsync(primaryUser);
        var second = await ParentAsync(secondUser);
        var primaryLink = await SeedParentLinkAsync(primaryUser, studentUser);
        var secondLink = await LinkSecondAsync(primary, primaryLink, second);

        (await second.PostAsync($"/api/parent-links/{secondLink}/revoke", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unlinked = JsonSerializer.Deserialize<ParentUnlinkedEvent>((await WithDbAsync(db => db.OutboxMessages
            .SingleAsync(o => o.Type == OutboxEventRegistry.NameFor<ParentUnlinkedEvent>()))).Content)!;
        unlinked.RevokedByRole.ShouldBe("Parent");
        unlinked.PrimaryParentUserId.ShouldBe(primaryUser);

        var last = await primary.PostAsync($"/api/parent-links/{primaryLink}/revoke", null);
        last.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var doc = JsonDocument.Parse(await last.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("errorCode").GetString().ShouldBe(ParentLinkErrorCodes.LastParentCannotLeave);
        (await WithDbAsync(db => db.ParentStudentLinks.SingleAsync(l => l.Id == primaryLink))).Status.ShouldBe(ParentStudentLinkStatus.Active);
    }
}
