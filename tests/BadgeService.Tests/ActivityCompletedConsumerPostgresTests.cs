using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace BadgeService.Tests;

/// <summary>
/// issue #572 on real PostgreSQL: <see cref="ActivityCompletedConsumer"/> eşzamanlı teslimlerde (aynı öğrenci + aynı deneme,
/// farklı EventId) yalnız bir ledger satırı yazar; 23505 unique ihlali exception olarak sızmaz (no-op); farklı deneme yeni satırdır.
/// Test DB'si Npgsql + <c>IX_StudentActivityCompletions_*</c> unique index'leriyle koşar (SQLite'ta 23505 kodu üretilmez).
/// </summary>
public class ActivityCompletedConsumerPostgresTests : IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _pg.DisposeAsync();

    private BadgeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BadgeDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    private static ActivityCompletedEvent Event(int userId, int instanceId, Guid? eventId = null) => new()
    {
        EventId = eventId ?? Guid.NewGuid(),
        UserId = userId,
        StudentId = userId,
        ClientId = $"kc-{userId}",
        ActivityId = 10,
        WorksheetInstanceId = instanceId,
        ItemCount = 5,
        ActiveSeconds = 300,
        CompletedAtUtc = Noon,
    };

    private async Task ConsumeAsync(ActivityCompletedEvent e)
    {
        await using var db = NewDb();
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var consumer = new ActivityCompletedConsumer(
            db, BadgeEvaluatorFactory.Create(db, hub), NullLogger<ActivityCompletedConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<ActivityCompletedEvent>>();
        context.Message.Returns(e);
        context.CancellationToken.Returns(CancellationToken.None);
        await consumer.Consume(context);
    }

    private async Task<int> LedgerCountAsync(int userId, int? instanceId = null)
    {
        await using var db = NewDb();
        return await db.StudentActivityCompletions.CountAsync(x =>
            x.UserId == userId && (instanceId == null || x.WorksheetInstanceId == instanceId));
    }

    [Fact]
    public async Task Concurrent_deliveries_of_the_same_attempt_write_exactly_one_ledger_row()
    {
        const int userId = 7201;
        const int instanceId = 9001;

        // Farklı EventId'li beş teslim: exists kontrolleri yarışabilir; unique index tek satırı garanti eder.
        var deliveries = Enumerable.Range(0, 5).Select(_ => Event(userId, instanceId)).ToList();
        await Task.WhenAll(deliveries.Select(ConsumeAsync));

        (await LedgerCountAsync(userId, instanceId)).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_redelivery_of_the_same_event_id_writes_one_row()
    {
        const int userId = 7202;
        const int instanceId = 9002;
        var e = Event(userId, instanceId);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => ConsumeAsync(e)));

        (await LedgerCountAsync(userId, instanceId)).ShouldBe(1);
    }

    [Fact]
    public async Task Different_attempts_of_the_same_activity_each_get_their_own_ledger_row()
    {
        const int userId = 7203;

        await Task.WhenAll(
            ConsumeAsync(Event(userId, 9101)),
            ConsumeAsync(Event(userId, 9102)),
            ConsumeAsync(Event(userId, 9103)));

        (await LedgerCountAsync(userId)).ShouldBe(3);
    }
}
