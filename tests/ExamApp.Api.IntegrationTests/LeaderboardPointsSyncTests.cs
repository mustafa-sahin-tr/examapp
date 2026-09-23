using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Foundation.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #225 uçtan uca (exam API tarafı): BadgeService outbox satırı → OutboxPublisher'ın yaptığı gibi
/// registry + JSON ile çözülür → bus'a publish → gerçek StudentPointsChangedConsumer (retry definition dahil)
/// → PostgreSQL StudentPoints → GET /api/leaderboard gerçek sıralama döner.
/// </summary>
public class LeaderboardPointsSyncTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly TimeSpan SyncTimeout = TimeSpan.FromSeconds(15);

    private async Task<int> SeedStudentAsync(int userId)
        => await WithDbAsync(async db =>
        {
            var s = new Student { UserId = userId, StudentNumber = $"N{userId}" };
            db.Students.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        });

    /// <summary>
    /// BadgeService'in outbox'a yazdığı içerik (<c>JsonSerializer.Serialize(evt)</c>) ve OutboxPublisher'ın
    /// çözüm yolu (<c>OutboxEventRegistry.Resolve</c> + <c>JsonSerializer.Deserialize(content, type)</c> +
    /// <c>Publish(object, type)</c>) birebir izlenir.
    /// </summary>
    private async Task PublishLikeOutboxPublisherAsync(int userId, int totalPoints, DateTime updatedAtUtc)
    {
        var harness = await StartedHarnessAsync();

        var content = JsonSerializer.Serialize(new StudentPointsChangedEvent
        {
            UserId = userId, TotalPoints = totalPoints, UpdatedAtUtc = updatedAtUtc,
        });
        var type = OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<StudentPointsChangedEvent>())!;
        var evt = JsonSerializer.Deserialize(content, type)!;

        await harness.Bus.Publish(evt, type);
    }

    private static readonly SemaphoreSlim HarnessStartLock = new(1, 1);
    private static bool _harnessStarted;

    /// <summary>Harness bus'ı test koleksiyonu (paylaşılan factory) boyunca bir kez başlatılır.</summary>
    private async Task<ITestHarness> StartedHarnessAsync()
    {
        var harness = Factory.Services.GetRequiredService<ITestHarness>();
        if (_harnessStarted)
            return harness;

        await HarnessStartLock.WaitAsync();
        try
        {
            if (!_harnessStarted)
            {
                await harness.Start();
                _harnessStarted = true;
            }
        }
        finally
        {
            HarnessStartLock.Release();
        }
        return harness;
    }

    private async Task<StudentPoint?> WaitForPointsAsync(int studentId, Func<StudentPoint, bool> done)
    {
        var deadline = DateTime.UtcNow + SyncTimeout;
        while (true)
        {
            var row = await WithDbAsync(db => db.StudentPoints.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(p => p.StudentId == studentId));
            if (row != null && done(row))
                return row;
            if (DateTime.UtcNow > deadline)
                return row;
            await Task.Delay(100);
        }
    }

    private async Task WaitForConsumedAsync(int userId, int expectedCount)
    {
        var consumer = Factory.Services.GetRequiredService<ITestHarness>()
            .GetConsumerHarness<ExamApp.Api.Consumers.StudentPointsChangedConsumer>();
        var deadline = DateTime.UtcNow + SyncTimeout;
        while (consumer.Consumed.Select<StudentPointsChangedEvent>(x => x.Context.Message.UserId == userId).Count() < expectedCount)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"UserId={userId} için {expectedCount} tüketim beklendi.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Points_event_is_consumed_and_the_leaderboard_ranks_by_real_points()
    {
        var a = await SeedStudentAsync(9101);
        var b = await SeedStudentAsync(9102);
        var c = await SeedStudentAsync(9103); // hiç puan event'i yok → 0 XP
        var t0 = DateTime.UtcNow.AddMinutes(-1);

        await PublishLikeOutboxPublisherAsync(9101, 300, t0);
        await PublishLikeOutboxPublisherAsync(9102, 500, t0);

        (await WaitForPointsAsync(a, p => p.XP == 300)).ShouldNotBeNull().XP.ShouldBe(300);
        (await WaitForPointsAsync(b, p => p.XP == 500)).ShouldNotBeNull().XP.ShouldBe(500);

        var client = await ClientAsAsync(9101, "Student", "kc-9101", "Student");
        var board = await client.GetFromJsonAsync<LeaderboardDto>("/api/leaderboard?scope=global", Json);

        board.ShouldNotBeNull();
        board.Success.ShouldBeTrue();
        board.Entries.Select(e => e.Xp).ShouldBe(new[] { 500, 300, 0 });
        board.Entries.Single(e => e.IsMe).Xp.ShouldBe(300);
        board.MyRank.ShouldBe(2);
        board.MyXp.ShouldBe(300);
        _ = c;
    }

    [Fact]
    public async Task Redelivered_and_stale_events_do_not_write_duplicate_or_older_points()
    {
        var a = await SeedStudentAsync(9201);
        var t0 = DateTime.UtcNow.AddMinutes(-5);

        await PublishLikeOutboxPublisherAsync(9201, 120, t0);
        await PublishLikeOutboxPublisherAsync(9201, 120, t0);                 // aynı event tekrar teslim
        await PublishLikeOutboxPublisherAsync(9201, 180, t0.AddSeconds(10));  // daha yeni
        await WaitForConsumedAsync(9201, 3);
        await PublishLikeOutboxPublisherAsync(9201, 120, t0);                 // geç gelen eski event
        await WaitForConsumedAsync(9201, 4);

        var rows = await WithDbAsync(db => db.StudentPoints.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.StudentId == a).ToListAsync());
        var row = rows.ShouldHaveSingleItem();
        row.XP.ShouldBe(180);
        row.SourceUpdatedAtUtc!.Value.ShouldBe(t0.AddSeconds(10), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Event_for_a_user_without_a_student_record_is_acknowledged_without_error()
    {
        await PublishLikeOutboxPublisherAsync(9301, 50, DateTime.UtcNow);
        await WaitForConsumedAsync(9301, 1);

        var harness = Factory.Services.GetRequiredService<ITestHarness>();
        (await harness.Consumed.Any<StudentPointsChangedEvent>(x => x.Context.Message.UserId == 9301 && x.Exception != null))
            .ShouldBeFalse();
        (await WithDbAsync(db => db.StudentPoints.IgnoreQueryFilters().CountAsync())).ShouldBe(0);
    }
}
