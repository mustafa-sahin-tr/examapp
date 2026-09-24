using AuthApi.Tests.Support;
using ExamApp.Api.Consumers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthApi.Tests.Consumers;

/// <summary>
/// Issue #277 (madde 4, review sonrası + re-review LOW-1): exam API'nin register/complete-profile
/// uçlarında yazdığı <see cref="UserRoleChangedEvent"/>'i tüketip auth-api'nin <c>Users.Role</c>
/// kolonunu günceller. Event kör güvenilmez — yalnızca "Keycloak'ı yeniden oku" tetikleyicisidir;
/// gerçek değer <see cref="IKeycloakService.GetUserRealmRoleNamesAsync"/> ile HER ZAMAN taze
/// okunur ve allowlist (Student/Teacher/Parent) ile filtrelenir. KASITLI OLARAK bir "tazelik
/// kısayolu" (event eskiyse Keycloak'a gitmeden atla) YOKTUR — <c>RoleUpdatedAtUtc</c>
/// login/complete-profile'ın JWT/istek zaman damgalarından geldiği için Keycloak'a gerçek yazma
/// anıyla sıralı değildir; böyle bir kısayol gerekli bir senkronu atlayabilirdi (LOW-1).
/// <c>RoleUpdatedAtUtc</c> yalnızca TEŞHİS amaçlı damgalanır, karşılaştırma/atlamada kullanılmaz.
/// </summary>
public class UserRoleChangedConsumerTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private UserRoleChangedConsumer NewConsumer(IKeycloakService keycloak)
        => new(_db.NewContext(), keycloak, NullLogger<UserRoleChangedConsumer>.Instance);

    private static IKeycloakService KeycloakWithRoles(params string[] roleNames)
    {
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.GetUserRealmRoleNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)roleNames);
        return keycloak;
    }

    private async Task<ExamApp.Api.Data.User> SeedUserAsync(
        string keycloakId = "kc-1", string role = "Student", DateTime? roleUpdatedAtUtc = null)
    {
        await using var ctx = _db.NewContext();
        var user = new ExamApp.Api.Data.User
        {
            KeycloakId = keycloakId,
            FullName = "Test User",
            Email = $"{keycloakId}@example.com",
            Role = role,
            RoleUpdatedAtUtc = roleUpdatedAtUtc
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private static UserRoleChangedEvent Evt(
        Guid? eventId = null, string keycloakId = "kc-1", int userId = 7,
        string newRole = "Teacher", DateTime? changedAtUtc = null)
        => new()
        {
            EventId = eventId ?? Guid.NewGuid(),
            KeycloakId = keycloakId,
            UserId = userId,
            NewRole = newRole,
            ChangedAtUtc = changedAtUtc ?? DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_ExistingUser_WritesRoleReReadFromKeycloak_NotEventNewRole()
    {
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: null);
        // Event claims "Teacher" but Keycloak's actual current mapping is "Parent" — the
        // consumer must trust Keycloak, not the event payload (issue #277 review, HIGH).
        var keycloak = KeycloakWithRoles("Parent");
        var e = Evt(keycloakId: "kc-1", newRole: "Teacher");

        await NewConsumer(keycloak).Consume(Context(e));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Parent");
        user.RoleUpdatedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Consume_KeycloakHasNoAppRole_LeavesRoleUnchanged()
    {
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: null);
        // Realm role list has only non-app roles (e.g. default-roles-exam-realm) — no
        // Student/Teacher/Parent mapping yet.
        var keycloak = KeycloakWithRoles("offline_access", "default-roles-exam-realm");
        var e = Evt(keycloakId: "kc-1", newRole: "Teacher");

        await NewConsumer(keycloak).Consume(Context(e));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Student"); // unchanged
    }

    [Fact]
    public async Task Consume_KeycloakRoleOutsideAllowlist_IsIgnored()
    {
        // Some other realm role happens to be present, but it is not one of the three
        // allowlisted app roles — must never be written to Users.Role verbatim.
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: null);
        var keycloak = KeycloakWithRoles("SomeOtherRole");
        var e = Evt(keycloakId: "kc-1");

        await NewConsumer(keycloak).Consume(Context(e));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Student");
    }

    [Fact]
    public async Task Consume_SameEventTwice_BothCallsReReadKeycloakAndConverge()
    {
        // No "already synced, skip" shortcut (issue #277 LOW-1) — every delivery, including an
        // exact duplicate, re-reads Keycloak. Harmless/idempotent: both calls converge on the
        // same current truth.
        await SeedUserAsync(keycloakId: "kc-1", role: "Student");
        var keycloak = KeycloakWithRoles("Teacher");
        var e = Evt(keycloakId: "kc-1");

        await NewConsumer(keycloak).Consume(Context(e));
        await NewConsumer(keycloak).Consume(Context(e));

        await keycloak.Received(2).GetUserRealmRoleNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Teacher");
    }

    [Fact]
    public async Task Consume_StaleOutOfOrderEvent_StillReSyncsFromKeycloak()
    {
        // issue #277 re-review (LOW-1): RoleUpdatedAtUtc is stamped from login/complete-profile's
        // JWT/request timestamps, not a fresh Keycloak read — it is NOT ordered against Keycloak's
        // actual write time (concurrent complete-profile vs. exam API register, or clock skew
        // between hosts). A "skip if event looks older" shortcut could therefore skip a needed
        // sync. The consumer must always re-read Keycloak regardless of how old/out-of-order the
        // triggering event's ChangedAtUtc is, and write whatever Keycloak says NOW.
        var now = DateTime.UtcNow;
        await SeedUserAsync(keycloakId: "kc-1", role: "Teacher", roleUpdatedAtUtc: now);

        var keycloak = KeycloakWithRoles("Student");
        var staleEvent = Evt(keycloakId: "kc-1", changedAtUtc: now.AddMinutes(-5));

        await NewConsumer(keycloak).Consume(Context(staleEvent));

        await keycloak.Received(1).GetUserRealmRoleNamesAsync("kc-1", Arg.Any<CancellationToken>());

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Student");
    }

    [Fact]
    public async Task Consume_NewerEventAfterOlder_ReReadsKeycloakAndUpdatesRole()
    {
        var now = DateTime.UtcNow;
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: now);

        var keycloak = KeycloakWithRoles("Teacher");
        var newerEvent = Evt(keycloakId: "kc-1", changedAtUtc: now.AddMinutes(5));

        await NewConsumer(keycloak).Consume(Context(newerEvent));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Teacher");
    }

    [Fact]
    public async Task Consume_UnknownKeycloakId_ThrowsForRetryDeadLetter()
    {
        var keycloak = KeycloakWithRoles("Teacher");
        var e = Evt(keycloakId: "kc-does-not-exist");

        await Should.ThrowAsync<UserNotFoundForRoleSyncException>(() => NewConsumer(keycloak).Consume(Context(e)));
    }

    [Fact]
    public async Task Consume_MissingKeycloakId_DoesNotThrowAndIsNoOp()
    {
        var keycloak = KeycloakWithRoles("Teacher");
        var e = Evt(keycloakId: "");
        e.KeycloakId = "";

        await NewConsumer(keycloak).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
        await keycloak.DidNotReceive().GetUserRealmRoleNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_KeycloakThrowsTransientException_PropagatesForRetry()
    {
        await SeedUserAsync(keycloakId: "kc-1", role: "Student");
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.GetUserRealmRoleNamesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new ExamApp.Api.Helpers.KeycloakException("boom"));
        var e = Evt(keycloakId: "kc-1");

        await Should.ThrowAsync<ExamApp.Api.Helpers.KeycloakException>(() => NewConsumer(keycloak).Consume(Context(e)));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    [InlineData("Parent")]
    public async Task Consume_AllowlistedRole_IsWritten(string allowedRole)
    {
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: null);
        var keycloak = KeycloakWithRoles(allowedRole);
        var e = Evt(keycloakId: "kc-1");

        await NewConsumer(keycloak).Consume(Context(e));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe(allowedRole);
    }

    public void Dispose() => _db.Dispose();
}
