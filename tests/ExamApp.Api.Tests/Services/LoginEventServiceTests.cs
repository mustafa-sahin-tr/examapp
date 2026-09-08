using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #84: <see cref="LoginEventService.RecordAsync"/> is the exam-api write path that
/// BadgeService's LoginAttemptedConsumer calls into — this is where a login attempt actually
/// becomes a durable row.
/// </summary>
public class LoginEventServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private LoginEventService NewService(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task RecordAsync_persists_a_successful_login_event_with_all_fields()
    {
        await using var ctx = _db.NewContext();
        var occurredAt = new DateTime(2026, 9, 8, 9, 30, 0, DateTimeKind.Utc);

        var result = await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-1",
            Role = "Student",
            OccurredAtUtc = occurredAt,
            Success = true,
        });

        result.Id.ShouldBeGreaterThan(0);

        await using var check = _db.NewContext();
        var row = await check.LoginEvents.SingleAsync();
        row.KeycloakUserId.ShouldBe("kc-sub-1");
        row.Role.ShouldBe("Student");
        row.OccurredAtUtc.ShouldBe(occurredAt);
        row.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task RecordAsync_persists_a_failed_login_event()
    {
        await using var ctx = _db.NewContext();

        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "unknown@test.local",
            Role = "Unknown",
            OccurredAtUtc = DateTime.UtcNow,
            Success = false,
        });

        await using var check = _db.NewContext();
        var row = await check.LoginEvents.SingleAsync();
        row.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task RecordAsync_converts_a_local_kind_datetime_to_the_equivalent_utc_instant()
    {
        // SQLite (the test DB provider) doesn't round-trip DateTimeKind, so this asserts on the
        // actual instant (Ticks — DateTime equality ignores Kind) rather than on Kind after
        // reload, which would pass trivially regardless of whether ToUtc() converts correctly.
        await using var ctx = _db.NewContext();
        var local = DateTime.SpecifyKind(new DateTime(2026, 9, 8, 15, 0, 0), DateTimeKind.Local);
        var expectedUtc = local.ToUniversalTime();

        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-2",
            Role = "Teacher",
            OccurredAtUtc = local,
            Success = true,
        });

        await using var check = _db.NewContext();
        var row = await check.LoginEvents.SingleAsync();
        row.OccurredAtUtc.ShouldBe(expectedUtc);
    }

    public void Dispose() => _db.Dispose();
}
