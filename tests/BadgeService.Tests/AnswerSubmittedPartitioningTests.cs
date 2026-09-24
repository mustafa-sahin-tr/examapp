using System.Collections.Concurrent;
using System.Text.Json;
using BadgeService;
using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BadgeService.Tests;

/// <summary>
/// issue #225 (review U1): aynı kullanıcının AnswerSubmittedEvent'leri gerçek MassTransit pipeline'ında
/// (AnswerSubmittedConsumerDefinition partitioner'ı dahil) paralel yayınlansa da sıralı işlenir; böylece
/// en yeni versiyonlu StudentPointsChangedEvent son toplamı taşır. Farklı kullanıcılar paralel işlenebilir.
/// Dosya tabanlı SQLite: her DbContext kendi bağlantısını açar (paylaşılan in-memory bağlantı eşzamanlı
/// kullanıma uygun değil); yazma kilitleri "Default Timeout" ile beklenir.
/// </summary>
public class AnswerSubmittedPartitioningTests : IAsyncLifetime
{
    private const int AnswersPerUser = 15;
    private const int PointPerAnswer = 10;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"badge-partition-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_path};Default Timeout=30;Pooling=False";

    public ValueTask InitializeAsync()
    {
        using var ctx = new BadgeDbContext(new DbContextOptionsBuilder<BadgeDbContext>().UseSqlite(ConnectionString).Options);
        ctx.Database.EnsureCreated();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try { File.Delete(_path); } catch (IOException) { /* temp dosyası; en kötü OS temizler */ }
        return ValueTask.CompletedTask;
    }

    // issue #279 item 4: her yayınlanan cevap FARKLI bir soruyu temsil eder (TestInstanceId, QuestionId) —
    // aksi halde aynı anahtara sahip 15 "cevap" birbirinin puanını ezerdi (bu test aggregate additivity'yi
    // ölçüyor, tek soruya tekrar cevap verme senaryosunu değil).
    private static int _questionSeq;

    private static AnswerSubmittedEvent Answer(int userId) => new()
    {
        UserId = userId, IsCorrect = true, QuestionPoint = PointPerAnswer, TimeTakenInSeconds = 1,
        SubjectId = 1, Subject = "Matematik", SubmittedAt = DateTime.UtcNow, ClientId = "kc",
        TestInstanceId = 1, QuestionId = System.Threading.Interlocked.Increment(ref _questionSeq),
    };

    [Fact]
    public async Task Same_user_answers_are_processed_sequentially_and_the_newest_version_carries_the_final_total()
    {
        var probe = new PerUserSaveConcurrencyProbe();
        await using var provider = new ServiceCollection()
            .AddDbContext<BadgeDbContext>(o => o.UseSqlite(ConnectionString).AddInterceptors(probe))
            .AddScoped<AnswerSubmissionAggregationService>()
            .AddScoped<BadgeEvaluator>()
            .AddSingleton(Substitute.For<IHubContext<BadgeNotificationHub>>())
            .AddMassTransitTestHarness(x =>
            {
                x.SetTestTimeouts(testTimeout: TimeSpan.FromMinutes(2));
                x.AddConsumer<AnswerSubmittedConsumer, AnswerSubmittedConsumerDefinition>();
            })
            .BuildServiceProvider(validateScopes: true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var publishes = new List<Task>();
            for (var i = 0; i < AnswersPerUser; i++)
            {
                publishes.Add(harness.Bus.Publish(Answer(userId: 1)));
                publishes.Add(harness.Bus.Publish(Answer(userId: 2)));
            }
            await Task.WhenAll(publishes);

            var consumer = harness.GetConsumerHarness<AnswerSubmittedConsumer>();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (consumer.Consumed.Select<AnswerSubmittedEvent>().Count() < 2 * AnswersPerUser)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("AnswerSubmittedEvent'ler zamanında tüketilmedi.");
                await Task.Delay(50);
            }
            consumer.Consumed.Select<AnswerSubmittedEvent>(m => m.Exception != null).ShouldBeEmpty();
        }
        finally
        {
            await harness.Stop();
        }

        probe.MaxConcurrentSavesForOneUser.ShouldBe(1);

        await using var check = new BadgeDbContext(new DbContextOptionsBuilder<BadgeDbContext>().UseSqlite(ConnectionString).Options);
        var events = (await check.OutboxMessages.ToListAsync())
            .Select(m => JsonSerializer.Deserialize<StudentPointsChangedEvent>(m.Content)!)
            .ToList();

        foreach (var userId in new[] { 1, 2 })
        {
            (await check.StudentQuestionAggregates.SingleAsync(a => a.UserId == userId))
                .TotalPoints.ShouldBe(AnswersPerUser * PointPerAnswer);

            var byVersion = events.Where(e => e.UserId == userId).OrderBy(e => e.UpdatedAtUtc).ToList();
            byVersion.Count.ShouldBe(AnswersPerUser);
            // Versiyon sırası = commit sırası: toplam her adımda tam bir cevap kadar artar, en yeni versiyon son toplamı taşır.
            byVersion.Select(e => e.TotalPoints)
                .ShouldBe(Enumerable.Range(1, AnswersPerUser).Select(i => i * PointPerAnswer));
            byVersion.Select(e => e.UpdatedAtUtc).Distinct().Count().ShouldBe(AnswersPerUser);
        }
    }

    /// <summary>
    /// Aynı kullanıcı için eşzamanlı SaveChanges sayısının tepe değerini ölçer; pencereyi genişletmek için
    /// her kaydetmeden önce kısa bir gecikme ekler (partitioner olmasaydı çakışmalar burada görünürdü).
    /// </summary>
    private sealed class PerUserSaveConcurrencyProbe : SaveChangesInterceptor
    {
        private readonly ConcurrentDictionary<int, int> _inFlight = new();
        private readonly ConcurrentDictionary<Guid, int> _userByContext = new();
        private int _max;

        public int MaxConcurrentSavesForOneUser => Volatile.Read(ref _max);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var user = eventData.Context!.ChangeTracker.Entries<StudentQuestionAggregate>().Select(e => e.Entity.UserId).FirstOrDefault();
            if (user != 0)
            {
                _userByContext[eventData.Context.ContextId.InstanceId] = user;
                var now = _inFlight.AddOrUpdate(user, 1, (_, v) => v + 1);
                int seen;
                while ((seen = Volatile.Read(ref _max)) < now && Interlocked.CompareExchange(ref _max, now, seen) != seen) { }
                await Task.Delay(15, cancellationToken);
            }
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Leave(eventData.Context!);
            return ValueTask.FromResult(result);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Leave(eventData.Context!);
            return Task.CompletedTask;
        }

        private void Leave(DbContext context)
        {
            if (_userByContext.TryRemove(context.ContextId.InstanceId, out var user))
                _inFlight.AddOrUpdate(user, 0, (_, v) => v - 1);
        }
    }
}
