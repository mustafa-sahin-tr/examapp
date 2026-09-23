using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #156: POST api/admin/{teachers|students}/{id}/reset-password — gerçek pipeline + Postgres; Keycloak ve
/// auth-api sahte (FakeKeycloakAccounts hata modlarıyla / FakeUserDirectory), log çıktısı CapturingLoggerProvider ile toplanır.
/// </summary>
public class AdminPasswordResetEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int MissingId = 987654321;

    private static string NewSub(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    // FakeUserDirectory/FakeKeycloakAccounts Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private static string ResetUrl(string kind, int id) => $"/api/admin/{kind}/{id}/reset-password";

    private FakeKeycloakAccounts Accounts => Factory.Services.GetRequiredService<FakeKeycloakAccounts>();

    private async Task<(int TeacherId, string Sub)> SeedTeacherAsync(
        string[]? realmRoles = null, string[]? clientRoles = null, FakeKeycloakFailure failure = FakeKeycloakFailure.None)
    {
        var userId = NewUserId();
        var sub = NewSub("kc-target");
        Factory.Services.GetRequiredService<FakeUserDirectory>().Add(new() { Id = userId, KeycloakId = sub, FullName = "Hedef" });
        Accounts.Add(sub, realmRoles ?? ["Teacher"], clientRoles, failure);
        var teacherId = await WithDbAsync(async db =>
        {
            var teacher = new Teacher { UserId = userId };
            db.Teachers.Add(teacher);
            await db.SaveChangesAsync();
            return teacher.Id;
        });
        return (teacherId, sub);
    }

    private async Task<(int StudentId, string Sub)> SeedStudentAsync()
    {
        var userId = NewUserId();
        var sub = NewSub("kc-target");
        Factory.Services.GetRequiredService<FakeUserDirectory>().Add(new() { Id = userId, KeycloakId = sub, FullName = "Hedef" });
        Accounts.Add(sub, ["Student"]);
        var studentId = await WithDbAsync(async db =>
        {
            var student = new Student { UserId = userId, StudentNumber = "R1" };
            db.Students.Add(student);
            await db.SaveChangesAsync();
            return student.Id;
        });
        return (studentId, sub);
    }

    private Task<HttpClient> AdminAsync(string? sub = null) =>
        ClientAsAsync(2, "Admin", sub ?? NewSub("kc-admin"), realmRoles: "Admin");

    private static async Task<string> MessageOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("temporaryPassword", out _).ShouldBeFalse();
        return doc.RootElement.GetProperty("message").GetString()!;
    }

    private Task<List<AdminUserActionLog>> AuditRowsAsync() =>
        WithDbAsync(db => db.AdminUserActionLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync());

    [Fact]
    public async Task Non_admin_roles_get_403_and_anonymous_gets_401_and_nothing_is_reset()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var (studentId, _) = await SeedStudentAsync();

        foreach (var role in new[] { "Teacher", "Student", "Parent" })
        {
            var client = await ClientAsAsync(1, role, NewSub("kc-" + role.ToLowerInvariant()), realmRoles: role);
            (await client.PostAsync(ResetUrl("teachers", teacherId), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PostAsync(ResetUrl("students", studentId), null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        (await Anonymous().PostAsync(ResetUrl("teachers", teacherId), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        Accounts.ResetCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Teacher_reset_returns_temporary_password_once_no_store_revokes_sessions_and_audits_without_secret()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var adminSub = NewSub("kc-admin");
        var admin = await AdminAsync(adminSub);

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.Pragma.ToString().ShouldContain("no-cache");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["temporaryPassword"]);
        var issued = doc.RootElement.GetProperty("temporaryPassword").GetString()!;
        issued.Length.ShouldBe(TemporaryPasswordGenerator.DefaultLength);

        Accounts.ResetCalls[targetSub].ShouldBe(1);
        Accounts.LogoutCalls[targetSub].ShouldBe(1);

        var row = (await AuditRowsAsync()).Single();
        row.ActorKeycloakId.ShouldBe(adminSub);
        row.Action.ShouldBe(AdminUserAction.PasswordReset);
        row.TargetType.ShouldBe(AdminUserTargetType.Teacher);
        row.TargetId.ShouldBe(teacherId);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);

        // Ham tablo içeriğinde şifre yok.
        var rawRow = await WithDbAsync(db => db.Database.SqlQueryRaw<string>(
            "SELECT concat_ws('|', \"Id\", \"ActorKeycloakId\", \"Action\", \"TargetType\", \"TargetId\", \"Outcome\", \"OccurredAtUtc\") AS \"Value\" FROM \"AdminUserActionLogs\"")
            .SingleAsync());
        rawRow.ShouldNotContain(issued);
        rawRow.ShouldContain("|PasswordReset|Teacher|");
        rawRow.ShouldContain("|Succeeded|");

        // Hiçbir log satırında (Trace dahil, exception metinleri dahil) şifre yok.
        var logs = Factory.Services.GetRequiredService<CapturingLoggerProvider>().Entries.ToList();
        logs.ShouldContain(entry => entry.Contains("[AdminPasswordReset] Şifre sıfırlandı"));
        logs.ShouldAllBe(entry => !entry.Contains(issued));
    }

    [Fact]
    public async Task Student_reset_works_with_the_same_contract()
    {
        var (studentId, targetSub) = await SeedStudentAsync();
        var admin = await AdminAsync();

        var response = await admin.PostAsync(ResetUrl("students", studentId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("temporaryPassword").GetString()!.Length.ShouldBe(16);
        Accounts.LogoutCalls[targetSub].ShouldBe(1);
        (await AuditRowsAsync()).Single().TargetType.ShouldBe(AdminUserTargetType.Student);
    }

    [Theory]
    [InlineData("Admin", null)]
    [InlineData("exam-service", null)]
    [InlineData("admin", null)] // büyük/küçük harf duyarsız
    [InlineData(null, "realm-admin")]
    [InlineData(null, "manage-users")]
    public async Task Protected_target_is_403_denied_audited_and_not_reset(string? realmRole, string? clientRole)
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(
            realmRoles: realmRole is null ? ["Teacher"] : ["Teacher", realmRole],
            clientRoles: clientRole is null ? null : [clientRole]);
        var admin = await AdminAsync();

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MessageOf(response)).ShouldBe("Yönetici veya servis hesaplarının şifresi bu ekrandan sıfırlanamaz.");
        Accounts.ResetCalls.ContainsKey(targetSub).ShouldBeFalse();
        Accounts.LogoutCalls.ContainsKey(targetSub).ShouldBeFalse();
        var row = (await AuditRowsAsync()).Single();
        row.Outcome.ShouldBe(AdminUserActionOutcome.Denied);
        row.TargetId.ShouldBe(teacherId);
    }

    [Fact]
    public async Task Admin_cannot_reset_own_password_403_and_denied_audited()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var admin = await AdminAsync(targetSub); // çağıran = hedef

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MessageOf(response)).ShouldBe("Kendi şifrenizi bu ekrandan sıfırlayamazsınız.");
        Accounts.ResetCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Unknown_teacher_or_student_is_404_with_message_no_store_and_notfound_audit()
    {
        var admin = await AdminAsync();

        var teacher = await admin.PostAsync(ResetUrl("teachers", MissingId), null);
        teacher.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageOf(teacher)).ShouldBe("Öğretmen bulunamadı.");
        teacher.Headers.CacheControl!.NoStore.ShouldBeTrue();

        var student = await admin.PostAsync(ResetUrl("students", MissingId), null);
        student.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageOf(student)).ShouldBe("Öğrenci bulunamadı.");

        (await AuditRowsAsync()).Select(r => r.Outcome).ShouldBe([AdminUserActionOutcome.NotFound, AdminUserActionOutcome.NotFound]);
    }

    [Theory]
    [InlineData(FakeKeycloakFailure.UserMissing)]
    [InlineData(FakeKeycloakFailure.ResetNotFound)]
    public async Task Keycloak_404_is_404_account_not_found(FakeKeycloakFailure failure)
    {
        var (teacherId, _) = await SeedTeacherAsync(failure: failure);
        var admin = await AdminAsync();

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageOf(response)).ShouldBe("Bu kullanıcıya bağlı bir oturum açma hesabı bulunamadı.");
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
    }

    [Fact]
    public async Task Keycloak_reset_failure_is_502_reset_failed_and_no_logout()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(failure: FakeKeycloakFailure.ResetFails);
        var admin = await AdminAsync();

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await MessageOf(response)).ShouldBe("Şifre şu anda sıfırlanamadı. Lütfen biraz sonra tekrar deneyin.");
        Accounts.LogoutCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.ResetFailed);
    }

    [Fact]
    public async Task Keycloak_role_lookup_failure_is_502_without_side_effect_or_audit()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(failure: FakeKeycloakFailure.RoleLookupFails);
        var admin = await AdminAsync();

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        Accounts.ResetCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Logout_failure_after_reset_is_502_session_revoke_failed_and_password_not_returned()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(failure: FakeKeycloakFailure.LogoutFails);
        var admin = await AdminAsync();

        var response = await admin.PostAsync(ResetUrl("teachers", teacherId), null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await MessageOf(response)).ShouldBe(
            "Şifre değiştirildi ancak kullanıcının açık oturumları kapatılamadı; yeni şifre gösterilmedi. Lütfen işlemi tekrar deneyin.");
        Accounts.ResetCalls[targetSub].ShouldBe(1);
        Accounts.LogoutCalls[targetSub].ShouldBe(1);
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.SessionRevokeFailed);
    }

    [Fact]
    public async Task Rate_limit_is_per_admin_shared_by_both_endpoints_and_separate_from_list_bucket()
    {
        var limits = Factory.Services.GetRequiredService<IOptionsMonitor<AdminPasswordResetRateLimitOptions>>().CurrentValue;
        var admin = await AdminAsync();

        for (var i = 0; i < limits.PermitLimit; i++)
        {
            var kind = i % 2 == 0 ? "teachers" : "students";
            (await admin.PostAsync(ResetUrl(kind, MissingId), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound, $"call {i + 1}");
        }

        var rejected = await admin.PostAsync(ResetUrl("teachers", MissingId), null);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Kısa sürede çok fazla şifre sıfırlama isteği yapıldı. Lütfen biraz bekleyip tekrar deneyin.");

        // Liste kovası ayrı: aynı admin listeyi çekmeye devam eder; başka admin etkilenmez.
        (await admin.GetAsync("/api/admin/teachers")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await AdminAsync()).PostAsync(ResetUrl("teachers", MissingId), null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
