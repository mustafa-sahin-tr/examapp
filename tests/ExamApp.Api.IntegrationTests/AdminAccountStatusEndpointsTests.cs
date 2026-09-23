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
/// Issue #155: PATCH api/admin/{teachers|students}/{id}/account-status — gerçek pipeline + Postgres; Keycloak ve auth-api
/// sahte (FakeKeycloakAccounts / FakeUserDirectory). Sahte Keycloak hesabın <c>enabled</c> durumunu tutar ve liste uçlarının
/// hesap durumu (auth-api lookup) bu durumu yansıtır. Keycloak admin client'ı (exam-admin) <c>manage-users</c> rolüne sahiptir;
/// gerçek Keycloak'ta enabled=false kullanıcıya login/refresh token verilmez (yerel doğrulama #155 raporunda).
/// </summary>
public class AdminAccountStatusEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int MissingId = 987654321;

    private static string NewSub(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    // FakeUserDirectory/FakeKeycloakAccounts Respawn ile sıfırlanmaz → benzersiz, büyük UserId.
    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private static string StatusUrl(string kind, int id) => $"/api/admin/{kind}/{id}/account-status";

    private static JsonContent Body(bool enabled) => JsonContent.Create(new { enabled });

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
            var student = new Student { UserId = userId, StudentNumber = "S1" };
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
        return doc.RootElement.GetProperty("message").GetString()!;
    }

    private static async Task<bool> EnabledOf(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["enabled"]);
        return doc.RootElement.GetProperty("enabled").GetBoolean();
    }

    private Task<List<AdminUserActionLog>> AuditRowsAsync() =>
        WithDbAsync(db => db.AdminUserActionLogs.AsNoTracking().OrderBy(r => r.Id).ToListAsync());

    [Fact]
    public async Task Non_admin_roles_get_403_and_anonymous_gets_401_and_nothing_changes()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var (studentId, _) = await SeedStudentAsync();

        foreach (var role in new[] { "Teacher", "Student", "Parent" })
        {
            var client = await ClientAsAsync(1, role, NewSub("kc-" + role.ToLowerInvariant()), realmRoles: role);
            (await client.PatchAsync(StatusUrl("teachers", teacherId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await client.PatchAsync(StatusUrl("students", studentId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        (await Anonymous().PatchAsync(StatusUrl("teachers", teacherId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        Accounts.SetEnabledCalls.ContainsKey(targetSub).ShouldBeFalse();
        Accounts.Enabled[targetSub].ShouldBeTrue();
        (await AuditRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Disable_sets_keycloak_enabled_false_revokes_sessions_audits_and_list_reflects_it()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var adminSub = NewSub("kc-admin");
        var admin = await AdminAsync(adminSub);

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await EnabledOf(response)).ShouldBeFalse();

        // Kriter 2'nin sahte tarafı: Keycloak'ta hesap kapalı (gerçek Keycloak kapalı hesaba login/refresh token vermez).
        Accounts.Enabled[targetSub].ShouldBeFalse();
        Accounts.LogoutCalls[targetSub].ShouldBe(1);

        var row = (await AuditRowsAsync()).Single();
        row.ActorKeycloakId.ShouldBe(adminSub);
        row.Action.ShouldBe(AdminUserAction.AccountDisabled);
        row.TargetType.ShouldBe(AdminUserTargetType.Teacher);
        row.TargetId.ShouldBe(teacherId);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        var raw = await WithDbAsync(db => db.Database.SqlQueryRaw<string>(
            "SELECT concat_ws('|', \"Action\", \"Outcome\") AS \"Value\" FROM \"AdminUserActionLogs\"").SingleAsync());
        raw.ShouldBe("AccountDisabled|Succeeded");

        // Liste (auth-api hesap durumu) yeni durumu gösterir.
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/admin/teachers?pageSize=100");
        var item = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == teacherId);
        item.GetProperty("isEnabled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Enable_sets_enabled_true_without_logout_and_audits_AccountEnabled()
    {
        var (studentId, targetSub) = await SeedStudentAsync();
        Accounts.Enabled[targetSub] = false;
        var admin = await AdminAsync();

        var response = await admin.PatchAsync(StatusUrl("students", studentId), Body(true));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EnabledOf(response)).ShouldBeTrue();
        Accounts.Enabled[targetSub].ShouldBeTrue();
        Accounts.LogoutCalls.ContainsKey(targetSub).ShouldBeFalse();
        var row = (await AuditRowsAsync()).Single();
        row.Action.ShouldBe(AdminUserAction.AccountEnabled);
        row.TargetType.ShouldBe(AdminUserTargetType.Student);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Disabling_twice_is_idempotent_200()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var admin = await AdminAsync();

        (await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EnabledOf(second)).ShouldBeFalse();
        Accounts.Enabled[targetSub].ShouldBeFalse();
        (await AuditRowsAsync()).Select(r => r.Outcome).ShouldBe([AdminUserActionOutcome.Succeeded, AdminUserActionOutcome.Succeeded]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"enabled\":null}")]
    public async Task Missing_enabled_is_400_without_side_effect(string json)
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var admin = await AdminAsync();

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId),
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await MessageOf(response)).ShouldBe("enabled alanı zorunludur (true veya false).");
        Accounts.SetEnabledCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Admin", null)]
    [InlineData("exam-service", null)]
    [InlineData(null, "realm-admin")]
    [InlineData(null, "manage-users")]
    public async Task Protected_target_is_403_denied_audited_and_not_changed(string? realmRole, string? clientRole)
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(
            realmRoles: realmRole is null ? ["Teacher"] : ["Teacher", realmRole],
            clientRoles: clientRole is null ? null : [clientRole]);
        var admin = await AdminAsync();

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MessageOf(response)).ShouldBe("Yönetici veya servis hesaplarının durumu bu ekrandan değiştirilemez.");
        Accounts.SetEnabledCalls.ContainsKey(targetSub).ShouldBeFalse();
        Accounts.LogoutCalls.ContainsKey(targetSub).ShouldBeFalse();
        Accounts.Enabled[targetSub].ShouldBeTrue();
        var row = (await AuditRowsAsync()).Single();
        row.Action.ShouldBe(AdminUserAction.AccountDisabled);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Admin_cannot_disable_own_account_403_and_denied_audited()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync();
        var admin = await AdminAsync(targetSub); // çağıran = hedef

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await MessageOf(response)).ShouldBe("Kendi hesabınızın durumunu bu ekrandan değiştiremezsiniz.");
        Accounts.SetEnabledCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Unknown_teacher_or_student_is_404_with_message()
    {
        var admin = await AdminAsync();

        var teacher = await admin.PatchAsync(StatusUrl("teachers", MissingId), Body(false));
        teacher.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageOf(teacher)).ShouldBe("Öğretmen bulunamadı.");

        var student = await admin.PatchAsync(StatusUrl("students", MissingId), Body(true));
        student.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageOf(student)).ShouldBe("Öğrenci bulunamadı.");

        (await AuditRowsAsync()).Select(r => r.Outcome).ShouldBe([AdminUserActionOutcome.NotFound, AdminUserActionOutcome.NotFound]);
    }

    [Theory]
    [InlineData(FakeKeycloakFailure.UserMissing)]
    [InlineData(FakeKeycloakFailure.StatusChangeNotFound)]
    public async Task Keycloak_404_is_404_account_not_found(FakeKeycloakFailure failure)
    {
        var (teacherId, _) = await SeedTeacherAsync(failure: failure);
        var admin = await AdminAsync();

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await MessageOf(response)).ShouldBe("Bu kullanıcıya bağlı bir oturum açma hesabı bulunamadı.");
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
    }

    [Fact]
    public async Task Keycloak_status_change_failure_is_502_and_no_logout()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(failure: FakeKeycloakFailure.StatusChangeFails);
        var admin = await AdminAsync();

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await MessageOf(response)).ShouldBe("Hesap durumu şu anda değiştirilemedi. Lütfen biraz sonra tekrar deneyin.");
        Accounts.Enabled[targetSub].ShouldBeTrue();
        Accounts.LogoutCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.StatusChangeFailed);
    }

    [Fact]
    public async Task Role_lookup_failure_is_502_without_side_effect_or_audit()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(failure: FakeKeycloakFailure.RoleLookupFails);
        var admin = await AdminAsync();

        (await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        Accounts.SetEnabledCalls.ContainsKey(targetSub).ShouldBeFalse();
        (await AuditRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Logout_failure_after_disable_is_502_session_revoke_failed()
    {
        var (teacherId, targetSub) = await SeedTeacherAsync(failure: FakeKeycloakFailure.LogoutFails);
        var admin = await AdminAsync();

        var response = await admin.PatchAsync(StatusUrl("teachers", teacherId), Body(false));

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await MessageOf(response)).ShouldBe(
            "Hesap devre dışı bırakıldı ancak kullanıcının açık oturumları kapatılamadı. Lütfen işlemi tekrar deneyin.");
        Accounts.Enabled[targetSub].ShouldBeFalse();
        (await AuditRowsAsync()).Single().Outcome.ShouldBe(AdminUserActionOutcome.SessionRevokeFailed);
    }

    [Fact]
    public async Task Rate_limit_is_per_admin_shared_by_both_endpoints_and_separate_from_password_reset_bucket()
    {
        var limits = Factory.Services.GetRequiredService<IOptionsMonitor<AdminAccountStatusRateLimitOptions>>().CurrentValue;
        var admin = await AdminAsync();

        for (var i = 0; i < limits.PermitLimit; i++)
        {
            var kind = i % 2 == 0 ? "teachers" : "students";
            (await admin.PatchAsync(StatusUrl(kind, MissingId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.NotFound, $"call {i + 1}");
        }

        var rejected = await admin.PatchAsync(StatusUrl("teachers", MissingId), Body(false));
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadAsStringAsync())
            .ShouldBe("Kısa sürede çok fazla hesap durumu değişikliği yapıldı. Lütfen biraz bekleyip tekrar deneyin.");

        // Şifre sıfırlama kovası ayrı; başka admin etkilenmez.
        (await admin.PostAsync($"/api/admin/teachers/{MissingId}/reset-password", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await AdminAsync()).PatchAsync(StatusUrl("teachers", MissingId), Body(false))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
