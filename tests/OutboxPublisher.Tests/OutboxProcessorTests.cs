using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OutboxPublisherService.Data;
using OutboxPublisherService.Publishers;
using Testcontainers.PostgreSql;

namespace OutboxPublisher.Tests;

/// <summary>
/// Exercises OutboxProcessor.ProcessBatchAsync against a real PostgreSQL container
/// so the FOR UPDATE SKIP LOCKED claim and the retry/dead-letter state machine run
/// against real SQL.
/// </summary>
public class OutboxProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RecordingPublishEndpoint _publisher = new();
    private ServiceProvider _sp = null!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_pg.GetConnectionString()));
        services.AddSingleton<IPublishEndpoint>(_publisher);
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _pg.DisposeAsync();
    }

    private OutboxProcessor NewProcessor(OutboxOptions? options = null) =>
        new(_sp, NullLogger<OutboxProcessor>.Instance,
            Options.Create(options ?? new OutboxOptions { BatchSize = 20, MaxRetries = 3 }));

    private async Task AddAsync(params OutboxMessage[] messages)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.OutboxMessages.AddRange(messages);
        await db.SaveChangesAsync();
    }

    private async Task<OutboxMessage> ReloadAsync(Guid id)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static OutboxMessage Pending(string type, string content) => new()
    {
        Id = Guid.NewGuid(), Type = type, Content = content, CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Publishes_a_valid_message_and_marks_it_processed()
    {
        var msg = Pending(
            OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
            """{"QuestionId":7,"Text":"q"}""");
        await AddAsync(msg);

        var processed = await NewProcessor().ProcessBatchAsync(CancellationToken.None);

        processed.ShouldBe(1);
        _publisher.Published.ShouldHaveSingleItem().ShouldBeOfType<QuestionCreatedEvent>().QuestionId.ShouldBe(7);

        var reloaded = await ReloadAsync(msg.Id);
        reloaded.ProcessedAt.ShouldNotBeNull();
        reloaded.RetryCount.ShouldBe(0);
        reloaded.Error.ShouldBeNull();
    }

    [Fact]
    public async Task Dead_letters_an_unresolvable_type_immediately()
    {
        var msg = Pending("Totally.Unknown.Event", "{}");
        await AddAsync(msg);

        await NewProcessor(new OutboxOptions { MaxRetries = 3 }).ProcessBatchAsync(CancellationToken.None);

        var reloaded = await ReloadAsync(msg.Id);
        reloaded.ProcessedAt.ShouldBeNull();
        reloaded.RetryCount.ShouldBe(3); // == MaxRetries → no longer picked up
        reloaded.Error.ShouldContain("Unresolved event type");
        _publisher.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failing_publish_is_retried_with_backoff_then_dead_lettered()
    {
        _publisher.FailNext = true;
        var msg = Pending(OutboxEventRegistry.NameFor<AnswerSubmittedEvent>(), """{"UserId":1}""");
        await AddAsync(msg);

        var opts = new OutboxOptions { MaxRetries = 3, RetryBackoffBase = TimeSpan.FromMinutes(5) };

        await NewProcessor(opts).ProcessBatchAsync(CancellationToken.None);
        var afterFirst = await ReloadAsync(msg.Id);
        afterFirst.RetryCount.ShouldBe(1);
        afterFirst.NextAttemptAt.ShouldNotBeNull();
        afterFirst.ProcessedAt.ShouldBeNull();

        // still backed off → not claimed on the next poll
        (await NewProcessor(opts).ProcessBatchAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task Skips_rows_that_are_still_within_their_backoff_window()
    {
        await AddAsync(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
            Content = """{"QuestionId":1}""",
            CreatedAt = DateTime.UtcNow,
            RetryCount = 1,
            NextAttemptAt = DateTime.UtcNow.AddMinutes(10),
        });

        (await NewProcessor().ProcessBatchAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    public async Task Respects_the_batch_size_and_orders_by_creation()
    {
        for (var i = 0; i < 5; i++)
            await AddAsync(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = OutboxEventRegistry.NameFor<QuestionCreatedEvent>(),
                Content = $$"""{"QuestionId":{{i}}}""",
                CreatedAt = DateTime.UtcNow.AddSeconds(i),
            });

        var processed = await NewProcessor(new OutboxOptions { BatchSize = 2, MaxRetries = 3 })
            .ProcessBatchAsync(CancellationToken.None);

        processed.ShouldBe(2);
        _publisher.Published.Cast<QuestionCreatedEvent>().Select(e => e.QuestionId).ShouldBe(new[] { 0, 1 });
    }
}
