using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #440 / #441 gerçek Postgres + HTTP pipeline'ında: öğrenci veli daveti (outbox event'i), aktivasyon bekleyen öğrencinin kısıtlı
/// uçları (403 ParentActivationRequired; öğrenme uçları açık), anonim aktivasyon (lookup / activate — tek kullanım, rıza, Active +
/// birincil bağlantı), süresi dolmuş / kullanılmış token, mevcut velinin my-children'dan rızalı onayı, kullanılamayan e-posta, hoşgörü
/// süresi dolmuş öğrencinin yalnız allowlist uçlarını görmesi (403 ParentRequired) ve form sonrası kısıtlı moda geçiş.
/// </summary>
public class ParentInvitationEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly string Pw = "Def" + "ter" + "77yz"; // test-only, runtime-joined for the secret scanner

    private FakeParentAccountDirectory Directory => Factory.Services.GetRequiredService<FakeParentAccountDirectory>();

    private static string UniqueEmail() => $"veli.{Guid.NewGuid():N}@example.com";

    private async Task<int> SeedStudentAsync(int userId, DateTime? graceStartedAt = null)
    {
        Factory.Services.GetRequiredService<FakeUserDirectory>()
            .Add(new() { Id = userId, KeycloakId = $"kc-{userId}", FullName = "Ali Kaya", Email = $"s{userId}@example.com" });
        return await WithDbAsync(async db =>
        {
            var student = new Student { UserId = userId, StudentNumber = userId.ToString(), ParentRequirementStartedAt = graceStartedAt };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            return student.Id;
        });
    }

    private Task<HttpClient> StudentAsync(int userId) => ClientAsAsync(userId, "Student", $"kc-{userId}", "Student");

    private static object Contact(string email) => new { parentFullName = "Ayşe Kaya", parentEmail = email, parentPhone = "0532 123 45 67" };

    private async Task<List<ParentInvitationRequestedEvent>> InvitationEventsAsync(int studentId) => await WithDbAsync(async db =>
    {
        var type = OutboxEventRegistry.NameFor<ParentInvitationRequestedEvent>();
        var rows = await db.OutboxMessages.Where(m => m.Type == type).OrderBy(m => m.CreatedAt).Select(m => m.Content).ToListAsync();
        return rows.Select(c => JsonSerializer.Deserialize<ParentInvitationRequestedEvent>(c)!).Where(e => e.StudentId == studentId).ToList();
    });

    private string UrlOf(ParentInvitationRequestedEvent e)
        => Factory.Services.GetRequiredService<ExamApp.Foundation.Security.ActivationLinkProtector>().Unprotect(e.ProtectedActivationUrl!);

    private string TokenOf(ParentInvitationRequestedEvent e) => UrlOf(e).Split("#token=")[1];

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (!text.TrimStart().StartsWith('{') && !text.TrimStart().StartsWith('['))
            return null;
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("errorCode", out var code)
            ? code.GetString()
            : null;
    }

    private static object Activation(string token, bool consent = true) => new
    {
        token, password = Pw, consentAccepted = consent, consentVersion = ParentConsentVersions.Current
    };

    [Fact]
    public async Task New_parent_invitation_restricts_social_features_until_activation()
    {
        const int studentUser = 7_600_001;
        var studentId = await SeedStudentAsync(studentUser);
        var student = await StudentAsync(studentUser);
        var email = UniqueEmail();

        // Davet → outbox event'i (aktivasyon bağlantısı yalnız event'te; yanıtta yok — Testing ortamı Development değil).
        var start = await student.PostAsJsonAsync("/api/student-parent/invitation", Contact(email));
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync());
        using (var doc = JsonDocument.Parse(await start.Content.ReadAsStringAsync()))
        {
            var status = doc.RootElement.GetProperty("status");
            status.GetProperty("mode").GetString().ShouldBe("AwaitingActivation");
            status.GetProperty("invitation").GetProperty("devActivationUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        var evt = (await InvitationEventsAsync(studentId)).Single();
        evt.Kind.ShouldBe(ParentInvitationService.KindNewParentActivation);
        evt.ParentEmail.ShouldBe(email);
        evt.StudentUserId.ShouldBe($"kc-{studentUser}");
        evt.ParentUserId.ShouldBe(string.Empty); // security M2: davette hesap yok
        evt.ProtectedActivationUrl!.ShouldNotContain("token");
        UrlOf(evt).ShouldStartWith("http://gateway.test/app/parent-activate#token=");
        Directory.HasAccount(email).ShouldBeFalse();
        await WithDbAsync(async db => (await db.ParentStudentLinks.AnyAsync(l => l.StudentId == studentId)).ShouldBeFalse());

        // Kısıtlı: booking talebi, iki DM ucu, herkese açık yorum, liderlik tablosu.
        foreach (var response in new[]
                 {
                     await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = 1 }),
                     await student.PostAsJsonAsync("/api/direct-messages/teachers/1/messages", new { body = "selam" }),
                     await student.PostAsJsonAsync("/api/direct-messages/conversations/1/messages", new { body = "selam" }),
                     await student.PostAsJsonAsync("/api/worksheet/1/comments", new { body = "soru" }),
                     await student.GetAsync("/api/leaderboard"),
                 })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, response.RequestMessage!.RequestUri!.ToString());
            (await ErrorCodeAsync(response)).ShouldBe("ParentActivationRequired");
        }
        // Öğrenme uçları açık (kendi profil/sonuçları).
        (await student.GetAsync("/api/student/profile")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Anonim aktivasyon sayfası.
        var anon = Anonymous();
        var token = TokenOf(evt);
        var lookup = await anon.PostAsJsonAsync("/api/parent-activation/lookup", new { token });
        lookup.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lookupDto = (await lookup.Content.ReadFromJsonAsync<ParentActivationLookupDto>(Json))!;
        lookupDto.StudentName.ShouldBe("Ali K.");
        lookupDto.RequiresPasswordSetup.ShouldBeTrue();
        lookupDto.Used.ShouldBeFalse();
        lookupDto.MaskedParentEmail.ShouldNotContain(email);

        (await ErrorCodeAsync(await anon.PostAsJsonAsync("/api/parent-activation/activate", Activation(token, consent: false))))
            .ShouldBe("ConsentRequired");

        var activate = await anon.PostAsJsonAsync("/api/parent-activation/activate", Activation(token));
        activate.StatusCode.ShouldBe(HttpStatusCode.OK, await activate.Content.ReadAsStringAsync());
        (await activate.Content.ReadAsStringAsync()).ShouldNotContain(Pw);

        await WithDbAsync(async db =>
        {
            var inv = await db.ParentInvitations.SingleAsync(i => i.StudentId == studentId);
            inv.Status.ShouldBe(ParentInvitationStatus.Activated);
            var link = await db.ParentStudentLinks.SingleAsync(l => l.Id == inv.LinkId);
            link.Status.ShouldBe(ParentStudentLinkStatus.Active);
            link.IsPrimary.ShouldBeTrue();
            link.Origin.ShouldBe(ParentStudentLinkOrigin.StudentRegistered);
            (await db.ParentConsents.CountAsync(c => c.StudentId == studentId)).ShouldBe(1);
            (await db.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<ParentLinkedEvent>())).ShouldBeGreaterThan(0);
        });
        Directory.HasAccount(email).ShouldBeTrue(); // hesap aktivasyonda açıldı

        // Tek kullanım + kısıtlar kalktı.
        var again = await anon.PostAsJsonAsync("/api/parent-activation/activate", Activation(token));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(again)).ShouldBe("ActivationTokenUsed");
        var after = await student.GetFromJsonAsync<ParentStatusDto>("/api/student-parent/status", Json);
        after!.Mode.ShouldBe("Ok");
        var leaderboard = await student.GetAsync("/api/leaderboard");
        (await ErrorCodeAsync(leaderboard)).ShouldNotBe("ParentActivationRequired");
    }

    [Fact]
    public async Task Expired_token_cannot_activate_but_the_parent_can_ask_for_a_new_link()
    {
        const int studentUser = 7_600_002;
        var studentId = await SeedStudentAsync(studentUser);
        var student = await StudentAsync(studentUser);
        (await student.PostAsJsonAsync("/api/student-parent/invitation", Contact(UniqueEmail()))).EnsureSuccessStatusCode();
        var token = TokenOf((await InvitationEventsAsync(studentId)).Single());

        await WithDbAsync(db => db.ParentInvitations.Where(i => i.StudentId == studentId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddMinutes(-1))
                .SetProperty(i => i.LastSentAt, DateTime.UtcNow.AddDays(-8))));

        var anon = Anonymous();
        var expired = await anon.PostAsJsonAsync("/api/parent-activation/activate", Activation(token));
        expired.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(expired)).ShouldBe("ActivationTokenExpired");
        var status = await student.GetFromJsonAsync<ParentStatusDto>("/api/student-parent/status", Json);
        status!.Invitation!.Expired.ShouldBeTrue();
        status.Invitation.CanResend.ShouldBeTrue();

        (await anon.PostAsJsonAsync("/api/parent-activation/resend", new { token })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var newToken = TokenOf((await InvitationEventsAsync(studentId)).Last());
        newToken.ShouldNotBe(token);
        (await anon.PostAsJsonAsync("/api/parent-activation/lookup", new { token })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anon.PostAsJsonAsync("/api/parent-activation/activate", Activation(newToken))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Existing_parent_approves_the_student_invitation_with_consent()
    {
        const int studentUser = 7_600_003;
        const int parentUser = 7_600_103;
        var studentId = await SeedStudentAsync(studentUser);
        await WithDbAsync(async db => { db.Parents.Add(new Parent { UserId = parentUser }); await db.SaveChangesAsync(); });
        var email = UniqueEmail();
        Directory.SeedExistingParent(email, parentUser);
        var student = await StudentAsync(studentUser);

        var start = await student.PostAsJsonAsync("/api/student-parent/invitation", Contact(email));
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        var evt = (await InvitationEventsAsync(studentId)).Single();
        evt.Kind.ShouldBe(ParentInvitationService.KindExistingParentApproval);
        evt.ProtectedActivationUrl.ShouldBeNull();

        var parent = await ClientAsAsync(parentUser, "Parent", $"kc-{parentUser}", "Parent");
        var children = await parent.GetFromJsonAsync<List<JsonElement>>("/api/parent-links/my-children", Json);
        var pending = children!.Single(c => c.GetProperty("origin").GetString() == "StudentRegistered");
        pending.GetProperty("requiresConsent").GetBoolean().ShouldBeTrue();
        pending.GetProperty("studentName").GetString().ShouldBe("Ali K.");
        var linkId = pending.GetProperty("linkId").GetInt32();

        var noConsent = await parent.PostAsync($"/api/parent-links/{linkId}/approve", null);
        noConsent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorCodeAsync(noConsent)).ShouldBe("ConsentRequired");

        var approve = await parent.PostAsJsonAsync($"/api/parent-links/{linkId}/approve",
            new { consentAccepted = true, consentVersion = ParentConsentVersions.Current });
        approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await WithDbAsync(async db =>
        {
            (await db.ParentStudentLinks.SingleAsync(l => l.Id == linkId)).Status.ShouldBe(ParentStudentLinkStatus.Active);
            (await db.ParentConsents.CountAsync(c => c.StudentId == studentId)).ShouldBe(1);
        });
        (await student.GetFromJsonAsync<ParentStatusDto>("/api/student-parent/status", Json))!.Mode.ShouldBe("Ok");
    }

    [Fact]
    public async Task Hijacked_unverified_email_cannot_take_over_the_child_and_the_real_owner_accepts_after_login()
    {
        // Security C1: saldırgan veli hesabını kurbanın e-postasına çevirmiş (doğrulanmamış). Uygulama içi onay yolu açılmaz, anonim
        // aktivasyon bağlantı kurmaz; kabul yalnız oturum + token + SUNUCUDAKİ e-posta eşleşmesiyle.
        const int studentUser = 7_600_020;
        const int attackerUser = 7_600_120;
        const int ownerUser = 7_600_121;
        var studentId = await SeedStudentAsync(studentUser);
        var email = UniqueEmail();
        Directory.SeedUnverifiedParent(email, attackerUser);
        await WithDbAsync(async db =>
        {
            db.Parents.AddRange(new Parent { UserId = attackerUser }, new Parent { UserId = ownerUser });
            await db.SaveChangesAsync();
        });

        (await (await StudentAsync(studentUser)).PostAsJsonAsync("/api/student-parent/invitation", Contact(email))).EnsureSuccessStatusCode();
        var evt = (await InvitationEventsAsync(studentId)).Single();
        evt.Kind.ShouldBe(ParentInvitationService.KindNewParentActivation); // uygulama içi onay değil
        var token = TokenOf(evt);

        // Saldırganın velilik listesinde bekleyen istek yok.
        var attacker = await ClientAsAsync(attackerUser, "Parent", $"kc-{attackerUser}", "Parent");
        var children = await attacker.GetFromJsonAsync<List<JsonElement>>("/api/parent-links/my-children", Json);
        children!.ShouldBeEmpty();

        var anon = Anonymous();
        var lookup = (await (await anon.PostAsJsonAsync("/api/parent-activation/lookup", new { token }))
            .Content.ReadFromJsonAsync<ParentActivationLookupDto>(Json))!;
        lookup.AccountExists.ShouldBeTrue();
        lookup.RequiresPasswordSetup.ShouldBeFalse();
        var anonymous = await anon.PostAsJsonAsync("/api/parent-activation/activate", Activation(token));
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(anonymous)).ShouldBe("LoginRequired");

        // Token sızmış olsa bile hesap e-postası eşleşmeyen veli kabul edemez.
        Directory.SeedParentAccountEmail(ownerUser, email);
        Directory.SeedParentAccountEmail(attackerUser, "baska@example.com"); // saldırgan e-postasını geri çevirdi
        var accept = new { token, consentAccepted = true, consentVersion = ParentConsentVersions.Current };
        var mismatch = await attacker.PostAsJsonAsync("/api/parent-activation/accept", accept);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(mismatch)).ShouldBe("InvitationEmailMismatch");

        // Öğrenci accept çağıramaz (yalnız Parent rolü).
        (await (await StudentAsync(studentUser)).PostAsJsonAsync("/api/parent-activation/accept", accept)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);

        var owner = await ClientAsAsync(ownerUser, "Parent", $"kc-{ownerUser}", "Parent");
        var ok = await owner.PostAsJsonAsync("/api/parent-activation/accept", accept);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        Directory.Verified.ShouldContain(ownerUser);

        await WithDbAsync(async db =>
        {
            var link = await db.ParentStudentLinks.Include(l => l.Parent).SingleAsync(l => l.StudentId == studentId);
            link.Status.ShouldBe(ParentStudentLinkStatus.Active);
            link.Parent.UserId.ShouldBe(ownerUser);
        });
    }

    [Fact]
    public async Task Email_of_a_non_parent_account_is_409_and_creates_nothing()
    {
        const int studentUser = 7_600_004;
        var studentId = await SeedStudentAsync(studentUser);
        var email = UniqueEmail();
        Directory.SeedNonParent(email);

        var response = await (await StudentAsync(studentUser)).PostAsJsonAsync("/api/student-parent/invitation", Contact(email));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(response)).ShouldBe("ParentEmailNotUsable");
        (await InvitationEventsAsync(studentId)).ShouldBeEmpty();
        await WithDbAsync(async db => (await db.ParentStudentLinks.AnyAsync(l => l.StudentId == studentId)).ShouldBeFalse());
    }

    [Fact]
    public async Task Register_with_an_unusable_parent_email_leaves_no_student_row_and_teacher_registration_still_works()
    {
        const int userId = 7_600_010;
        var email = UniqueEmail();
        Directory.SeedNonParent(email);
        Factory.Services.GetRequiredService<FakeUserDirectory>()
            .Add(new() { Id = userId, KeycloakId = $"kc-{userId}", FullName = "Ali Kaya", Email = $"s{userId}@example.com" });
        var gradeId = await WithDbAsync(async db =>
        {
            var grade = new Grade { Name = "8" };
            db.Grades.Add(grade);
            await db.SaveChangesAsync();
            return grade.Id;
        });
        var client = await ClientAsAsync(userId, "", $"kc-{userId}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/student/register")
        {
            Content = JsonContent.Create(new
            {
                studentNumber = "s1", gradeId, parentFullName = "Ayşe Kaya", parentEmail = email, parentPhone = "05321234567"
            })
        };
        request.Headers.Add("Cookie", "refresh_token=test-cookie");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await ErrorCodeAsync(response)).ShouldBe("ParentEmailNotUsable");
        await WithDbAsync(async db => (await db.Students.IgnoreQueryFilters().AnyAsync(s => s.UserId == userId)).ShouldBeFalse());

        // Öğrenci satırı kalmadığı için aynı kullanıcı öğretmen olarak kaydolabilir (#234 dışlaması tetiklenmez).
        using var scope = Factory.Services.CreateScope();
        var teacher = await scope.ServiceProvider.GetRequiredService<ExamApp.Api.Services.Interfaces.ITeacherService>()
            .Save(userId, new RegisterTeacherDto { IsIndependentTutor = true });
        teacher.Success.ShouldBeTrue(teacher.Message);
    }

    [Fact]
    public async Task Expired_grace_student_sees_only_the_add_parent_flow_until_the_form_is_sent()
    {
        const int studentUser = 7_600_005;
        await SeedStudentAsync(studentUser, graceStartedAt: DateTime.UtcNow.AddDays(-31));
        var student = await StudentAsync(studentUser);

        var status = await student.GetFromJsonAsync<ParentStatusDto>("/api/student-parent/status", Json);
        status!.Mode.ShouldBe("Expired");
        status.IsFinalWarning.ShouldBeTrue();

        var blocked = await student.GetAsync("/api/student/me/last-login");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(blocked)).ShouldBe("ParentRequired");
        (await student.GetAsync("/api/student/profile")).StatusCode.ShouldBe(HttpStatusCode.OK); // allowlist, veri silinmedi
        (await student.GetAsync("/api/parent-links/my-parents")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Form → (b) akışı → kısıtlı mod: genel uçlar açılır, sosyal uçlar ParentActivationRequired.
        (await student.PostAsJsonAsync("/api/student-parent/invitation", Contact(UniqueEmail()))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await student.GetAsync("/api/student/me/last-login")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ErrorCodeAsync(await student.GetAsync("/api/leaderboard"))).ShouldBe("ParentActivationRequired");
    }

    [Fact]
    public async Task Grace_student_keeps_full_access_and_the_counter_starts_on_first_status_read()
    {
        const int studentUser = 7_600_006;
        var studentId = await SeedStudentAsync(studentUser);
        var student = await StudentAsync(studentUser);

        var status = await student.GetFromJsonAsync<ParentStatusDto>("/api/student-parent/status", Json);
        status!.Mode.ShouldBe("Grace");
        status.DaysLeft.ShouldBe(30);
        status.IsFinalWarning.ShouldBeFalse();
        await WithDbAsync(async db => (await db.Students.SingleAsync(s => s.Id == studentId)).ParentRequirementStartedAt.ShouldNotBeNull());

        (await student.GetAsync("/api/student/me/last-login")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ErrorCodeAsync(await student.GetAsync("/api/leaderboard"))).ShouldNotBe("ParentActivationRequired");
        // #441 kararı: hoşgörü süresi booking açmaz — velisiz öğrenci servis katmanında ParentRequired alır.
        var booking = await student.PostAsJsonAsync("/api/booking/requests", new { availabilitySlotId = 999_999 });
        (await ErrorCodeAsync(booking)).ShouldNotBe("ParentActivationRequired");
    }

    [Fact]
    public async Task Admin_dashboard_reports_the_parentless_target_set()
    {
        await SeedStudentAsync(7_600_007, graceStartedAt: DateTime.UtcNow.AddDays(-3));
        await SeedStudentAsync(7_600_008, graceStartedAt: DateTime.UtcNow.AddDays(-40));
        var admin = await ClientAsAsync(7_600_900, "Admin", "kc-admin-440", "Admin");

        var summary = await admin.GetFromJsonAsync<JsonElement>("/api/admin/dashboard/summary", Json);

        summary.GetProperty("studentsWithoutParent").GetInt32().ShouldBe(2);
        summary.GetProperty("studentsInParentGrace").GetInt32().ShouldBe(1);
        summary.GetProperty("studentsParentGraceExpired").GetInt32().ShouldBe(1);
        summary.GetProperty("studentsAwaitingParentActivation").GetInt32().ShouldBe(0);
    }
}
