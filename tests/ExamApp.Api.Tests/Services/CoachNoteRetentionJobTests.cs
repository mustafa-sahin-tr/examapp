using ExamApp.Api.Data;
using ExamApp.Api.Services.Coaching;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #810: koç notları <c>UpdatedAt</c>'ten <c>CoachNoteRetention:RetentionDays</c> geçince parti parti, kalıcı silinir;
/// sınırdaki ve yeni notlar kalır, hedefler etkilenmez (<see cref="ParentAccessAuditRetentionJobTests"/> deseni).
/// </summary>
public class CoachNoteRetentionJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 4, 15, 0, TimeSpan.Zero);
    private readonly TestDb _db = TestDb.Create();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Monitor(CoachNoteRetentionOptions value) : IOptionsMonitor<CoachNoteRetentionOptions>
    {
        public CoachNoteRetentionOptions CurrentValue => value;
        public CoachNoteRetentionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<CoachNoteRetentionOptions, string?> listener) => null;
    }

    private CoachNoteRetentionJob NewJob(AppDbContext ctx, int retentionDays = 730, int batchSize = 5000)
        => new(ctx, new Monitor(new CoachNoteRetentionOptions { RetentionDays = retentionDays, DeleteBatchSize = batchSize }),
            new FixedClock(Now));

    private async Task<int> SeedStudentAsync()
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = 8100, StudentNumber = "810" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return student.Id;
    }

    private static CoachNote Note(int studentId, DateTime updatedAt) => new()
    {
        StudentId = studentId,
        TeacherUserId = 9,
        Body = "N",
        CreatedAt = updatedAt.AddDays(-30),
        UpdatedAt = updatedAt
    };

    [Fact]
    public async Task Deletes_only_notes_not_updated_within_the_retention_period()
    {
        var studentId = await SeedStudentAsync();
        var cutoff = Now.UtcDateTime.AddDays(-730);
        await using (var seed = _db.NewContext())
        {
            seed.CoachNotes.AddRange(
                Note(studentId, cutoff.AddDays(-300)),
                Note(studentId, cutoff.AddSeconds(-1)), // hemen dışında: silinir
                Note(studentId, cutoff),                // tam sınır: kalır
                Note(studentId, cutoff.AddSeconds(1)),  // hemen içinde: kalır
                Note(studentId, Now.UtcDateTime.AddDays(-1)));
            seed.CoachWeeklyGoals.Add(new CoachWeeklyGoal
            {
                StudentId = studentId, TeacherUserId = 9, WeekStart = new DateOnly(2023, 1, 2), Title = "G",
                CreatedAt = cutoff.AddDays(-300), UpdatedAt = cutoff.AddDays(-300)
            });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(2);

        await using var read = _db.NewContext();
        var remaining = await read.CoachNotes.AsNoTracking().Select(n => n.UpdatedAt).ToListAsync();
        remaining.Count.ShouldBe(3);
        remaining.ShouldAllBe(t => t >= cutoff);
        (await read.CoachWeeklyGoals.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Deletes_in_batches_until_nothing_old_is_left()
    {
        var studentId = await SeedStudentAsync();
        var old = Now.UtcDateTime.AddDays(-800);
        await using (var seed = _db.NewContext())
        {
            for (var i = 0; i < 7; i++)
                seed.CoachNotes.Add(Note(studentId, old.AddMinutes(i)));
            seed.CoachNotes.Add(Note(studentId, Now.UtcDateTime.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx, batchSize: 3).PurgeExpiredAsync()).ShouldBe(7);

        await using var read = _db.NewContext();
        (await read.CoachNotes.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Nothing_to_delete_returns_zero()
    {
        await using var ctx = _db.NewContext();
        (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
