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

    /// <summary>
    /// Issue #125: <see cref="LoginEventService.GetPreviousSuccessfulLoginAsync"/> skips the most
    /// recent successful login (the current session) and returns the one before it.
    /// </summary>
    [Fact]
    public async Task GetPreviousSuccessfulLoginAsync_TwoSuccessfulLogins_ReturnsTheOlderOneNotTheLatest()
    {
        await using var ctx = _db.NewContext();
        var older = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);

        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-3",
            Role = "Student",
            OccurredAtUtc = older,
            Success = true,
        });
        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-3",
            Role = "Student",
            OccurredAtUtc = latest,
            Success = true,
        });

        var result = await NewService(ctx).GetPreviousSuccessfulLoginAsync("kc-sub-3");

        result.ShouldNotBeNull();
        result!.OccurredAtUtc.ShouldBe(older);
    }

    [Fact]
    public async Task GetPreviousSuccessfulLoginAsync_OnlyOneSuccessfulLogin_ReturnsNull()
    {
        await using var ctx = _db.NewContext();

        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-4",
            Role = "Student",
            OccurredAtUtc = DateTime.UtcNow,
            Success = true,
        });

        var result = await NewService(ctx).GetPreviousSuccessfulLoginAsync("kc-sub-4");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPreviousSuccessfulLoginAsync_NoLoginEventsAtAll_ReturnsNull()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetPreviousSuccessfulLoginAsync("kc-sub-unknown");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPreviousSuccessfulLoginAsync_FailedAttemptIsMostRecent_FailedRowIsIgnoredEntirely()
    {
        // The most recent row overall is a failed attempt; it must not be counted as "the current
        // session" (which would incorrectly make the successful login before it the "previous" one)
        // nor returned itself. Only successful rows participate in the ordering/skip logic.
        await using var ctx = _db.NewContext();
        var onlySuccessful = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-5",
            Role = "Student",
            OccurredAtUtc = onlySuccessful,
            Success = true,
        });
        await NewService(ctx).RecordAsync(new LoginEventCreateDto
        {
            KeycloakUserId = "kc-sub-5",
            Role = "Student",
            OccurredAtUtc = onlySuccessful.AddHours(1),
            Success = false,
        });

        var result = await NewService(ctx).GetPreviousSuccessfulLoginAsync("kc-sub-5");

        // Only one successful row exists, so it is treated as the current session and skipped.
        result.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
