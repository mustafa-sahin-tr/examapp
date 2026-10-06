using BadgeService;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace BadgeService.Tests;

/// <summary>
/// issue #396 on real PostgreSQL: the <c>AddUserResetMarkers</c> migration applies (and reverts), and the pre-reset
/// event guard holds with the Npgsql model (timestamptz round-trip, xmin concurrency tokens on the aggregates).
/// </summary>
public class UserResetEventGuardPostgresTests : IAsyncLifetime
{
    private const string PreviousMigration = "BackfillBadgeDefinitionIconsFromSeed";
    private const string ResetMarkerMigration = "AddUserResetMarkers";

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

    private static AnswerSubmittedEvent Answer(DateTime submittedAt, int revision) => new()
    {
        EventId = Guid.NewGuid(),
        UserId = 1,
        TestInstanceId = 5,
        QuestionId = 42,
        IsCorrect = true,
        QuestionPoint = 10,
        TimeTakenInSeconds = 10,
        SubjectId = 1,
        Subject = "Matematik",
        SubmittedAt = submittedAt,
        Revision = revision,
    };

    private async Task<bool> ProcessAsync(AnswerSubmittedEvent e)
    {
        await using var db = NewDb();
        return await new AnswerSubmissionAggregationService(db, Options.Create(new AnswerPointOptions())).ProcessAsync(e);
    }

    [Fact]
    public async Task A_pre_reset_event_gives_no_points_and_a_post_reset_one_does()
    {
        var beforeReset = DateTime.UtcNow.AddSeconds(-5);
        (await ProcessAsync(Answer(beforeReset, revision: 1))).ShouldBeTrue();

        await using (var db = NewDb())
            await new UserResetService(db).ResetAsync(1);

        (await ProcessAsync(Answer(beforeReset, revision: 2))).ShouldBeFalse();
        await using (var db = NewDb())
            (await db.StudentQuestionAggregates.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();

        await Task.Delay(5);
        (await ProcessAsync(Answer(DateTime.UtcNow, revision: 3))).ShouldBeTrue();
        await using (var db = NewDb())
            (await db.StudentQuestionAggregates.SingleAsync(x => x.UserId == 1)).TotalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task The_migration_reverts_and_reapplies()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync(PreviousMigration);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(m => m.EndsWith(ResetMarkerMigration));

        await db.Database.MigrateAsync(ResetMarkerMigration);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(m => m.EndsWith(ResetMarkerMigration));
    }
}
