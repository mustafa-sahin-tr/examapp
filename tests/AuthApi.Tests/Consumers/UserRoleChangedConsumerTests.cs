using AuthApi.Tests.Support;
using ExamApp.Api.Consumers;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuthApi.Tests.Consumers;

/// <summary>
/// Issue #277 (madde 4): exam API'nin register/complete-profile uçlarında yazdığı
/// <see cref="UserRoleChangedEvent"/>'i tüketip auth-api'nin <c>Users.Role</c> kolonunu
/// günceller. Idempotent/sırasız-teslim güvenliği <see cref="ExamApp.Api.Data.User.RoleUpdatedAtUtc"/>
/// ile (event'in <c>ChangedAtUtc</c>'i daha yeniyse güncelle, değilse no-op).
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

    private UserRoleChangedConsumer NewConsumer()
        => new(_db.NewContext(), NullLogger<UserRoleChangedConsumer>.Instance);

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
    public async Task Consume_ExistingUser_UpdatesRoleAndStampsRoleUpdatedAtUtc()
    {
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: null);
        var e = Evt(keycloakId: "kc-1", newRole: "Teacher");

        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Teacher");
        user.RoleUpdatedAtUtc.ShouldBe(e.ChangedAtUtc);
    }

    [Fact]
    public async Task Consume_SameEventTwice_SecondCallIsNoOp()
    {
        await SeedUserAsync(keycloakId: "kc-1", role: "Student");
        var e = Evt(keycloakId: "kc-1", newRole: "Teacher");

        await NewConsumer().Consume(Context(e));
        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Teacher");
        user.RoleUpdatedAtUtc.ShouldBe(e.ChangedAtUtc);
    }

    [Fact]
    public async Task Consume_OutOfOrderOlderEvent_DoesNotOverwriteNewerRole()
    {
        var now = DateTime.UtcNow;
        await SeedUserAsync(keycloakId: "kc-1", role: "Teacher", roleUpdatedAtUtc: now);

        // Eski (gecikmeli teslim edilen) event daha önceki bir zaman damgası taşıyor —
        // depoda zaten daha yeni bir Role var, bu event onu geri almamalı.
        var staleEvent = Evt(keycloakId: "kc-1", newRole: "Student", changedAtUtc: now.AddMinutes(-5));

        await NewConsumer().Consume(Context(staleEvent));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Teacher");
        user.RoleUpdatedAtUtc.ShouldBe(now);
    }

    [Fact]
    public async Task Consume_NewerEventAfterOlder_UpdatesRole()
    {
        var now = DateTime.UtcNow;
        await SeedUserAsync(keycloakId: "kc-1", role: "Student", roleUpdatedAtUtc: now);

        var newerEvent = Evt(keycloakId: "kc-1", newRole: "Teacher", changedAtUtc: now.AddMinutes(5));

        await NewConsumer().Consume(Context(newerEvent));

        await using var check = _db.NewContext();
        var user = await check.Users.SingleAsync(u => u.KeycloakId == "kc-1");
        user.Role.ShouldBe("Teacher");
        user.RoleUpdatedAtUtc.ShouldBe(newerEvent.ChangedAtUtc);
    }

    [Fact]
    public async Task Consume_UnknownKeycloakId_ThrowsForRetryDeadLetter()
    {
        var e = Evt(keycloakId: "kc-does-not-exist");

        await Should.ThrowAsync<UserNotFoundForRoleSyncException>(() => NewConsumer().Consume(Context(e)));
    }

    [Fact]
    public async Task Consume_MissingKeycloakId_DoesNotThrowAndIsNoOp()
    {
        var e = Evt(keycloakId: "");
        e.KeycloakId = "";

        await NewConsumer().Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Users.CountAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
